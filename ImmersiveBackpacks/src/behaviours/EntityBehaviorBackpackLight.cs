using System;
using ImmersiveBackpacks.inventory;
using ImmersiveBackpacks.items;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace ImmersiveBackpacks.behaviours;

/// <summary>
/// Lights the wearer from a worn backpack's light addons (e.g. a lantern). The server owns the data: it reads
/// the player's authoritative backpack inventory, computes the brightest worn addon light and publishes it as a
/// synced watched attribute. Every client then drives a dynamic <see cref="IPointLight"/> from that attribute -
/// for the local player and for remote players alike, whose backpack contents aren't resolved client-side. The
/// point light is client-render-only and counts against the "Dynamic lights" graphics-setting budget.
/// </summary>
public class EntityBehaviorBackpackLight(Entity entity) : EntityBehavior(entity)
{
    private const string LightKey = "immersivebackpacklight";

    // Pos is read live by the renderer each frame (follows the player for free); Color is set on the tick.
    private sealed class BagPointLight(Entity entity) : IPointLight
    {
        public Vec3f Color { get; set; } = new();
        public Vec3d Pos => entity.Pos.XYZ.Add(0, 1.0, 0);   // ~torso/back height
    }

    private float accum;
    private ICoreClientAPI capi;
    private BagPointLight light;
    private bool registered;
    private byte[] lastPublishedHsv;
    private long lastBagSig = long.MinValue;
    private long lastCargoSig = long.MinValue;

    public override string PropertyName() => "immersivebackpacklight";

    public override void OnGameTick(float deltaTime)
    {
        accum += deltaTime;
        if (accum < 0.5f) return;
        accum = 0;

        if (entity.World.Side == EnumAppSide.Server) ServerTick();
        else ClientTick();
    }

    // Server: recompute from the authoritative inventory and publish only on change.
    private void ServerTick()
    {
        BroadcastWornCargoIfChanged();

        byte[] hsv = ComputeWornLight();
        if (HsvEquals(hsv, lastPublishedHsv)) return;
        lastPublishedHsv = hsv;
        if (hsv != null) entity.WatchedAttributes.SetBytes(LightKey, hsv);
        else entity.WatchedAttributes.RemoveAttribute(LightKey);
        entity.WatchedAttributes.MarkPathDirty(LightKey);
    }

    // Vanilla re-broadcasts a player's backpack to other clients only when a whole bag is added/removed
    // (InventoryPlayerBackpacks.OnItemSlotModified), never when a worn bag's CONTENTS change - so a tool
    // placed on a worn toolstrap never reaches observers. Re-broadcast on any worn-cargo change so remote
    // clients receive the updated bag stacks (whose full attributes carry backpack.slots); their worn shape
    // then re-tesselates via SyncWornShape's cargo hash. Server-only, throttled to this 0.5s tick.
    private void BroadcastWornCargoIfChanged()
    {
        if (entity is not EntityPlayer ep) return;
        var inv = ep.Player?.InventoryManager?.GetOwnInventory(GlobalConstants.backpackInvClassName);
        if (inv == null) return;

        long sig = 17;
        int n = Math.Min(4, inv.Count);
        for (int i = 0; i < n; i++)
        {
            var slots = inv[i]?.Itemstack?.Attributes?.GetTreeAttribute("backpack")?.GetTreeAttribute("slots");
            sig = sig * 31 + BackpackSlotLayout.CargoHash(slots);
        }

        if (sig == lastCargoSig) return;
        bool first = lastCargoSig == long.MinValue;   // join sync already carries the initial cargo
        lastCargoSig = sig;
        if (!first) (ep.Player as IServerPlayer)?.BroadcastPlayerData();
    }

    // Client: render whatever the server published for this player (works for any player entity).
    private void ClientTick()
    {
        capi ??= entity.World.Api as ICoreClientAPI;
        if (capi == null) return;

        SyncWornShape();

        byte[] hsv = entity.WatchedAttributes.GetBytes(LightKey);
        if (hsv is { Length: >= 3 } && hsv[2] > 0)
        {
            light ??= new(entity);
            light.Color = ToPointLightColor(hsv);
            if (registered) return;
            capi.Render.AddPointLight(light);
            registered = true;
        }
        else if (registered)
        {
            capi.Render.RemovePointLight(light);
            registered = false;
        }
    }

    // Re-tesselate the worn shape when this player's set of worn bags (or their addons) changes.
    // Vanilla EntityBehaviorPlayerInventory only subscribes to the backpack SlotModified event if the
    // inventory happens to be non-null on its first tick - for REMOTE players it usually isn't yet, so it
    // never re-tesselates and a bag equipped after spawn stays invisible to observers (the bag slots ARE
    // synced via ToPacketForOtherPlayers, only the shape rebuild is missing). We drive it ourselves.
    private void SyncWornShape()
    {
        if (entity is not EntityPlayer ep) return;
        var inv = ep.Player?.InventoryManager?.GetOwnInventory(GlobalConstants.backpackInvClassName);
        if (inv == null) return;

        long sig = 17;
        int n = Math.Min(4, inv.Count);
        for (int i = 0; i < n; i++)
        {
            var stack = inv[i]?.Itemstack;
            sig = sig * 31 + (stack?.Collectible?.Id ?? 0);
            // Fold placed_addons so attaching/detaching an addon (also a synced stack attribute) re-tesselates.
            sig = sig * 31 + (stack?.Attributes?.GetTreeAttribute("placed_addons")?.GetHashCode() ?? 0);
            // Fold the cargo (position-sensitive) so a tool moving in/out of a worn toolstrap's slot re-tesselates
            // - otherwise the tool renders stale on the body until the whole bag is moved or re-equipped.
            var slots = stack?.Attributes?.GetTreeAttribute("backpack")?.GetTreeAttribute("slots");
            sig = sig * 31 + BackpackSlotLayout.CargoHash(slots);
            // Fold Deven's "Immersive Backpacks" hide flag so selecting/deselecting a worn bag re-tesselates
            // and our GetShape guard hides/shows it, rather than relying only on that mod's own retessellation.
            sig = sig * 31 + (stack?.Attributes?.GetInt("immersiveBackpacksHideAttachmentWhileSelected", 0) ?? 0);
        }

        if (sig == lastBagSig) return;
        lastBagSig = sig;
        entity.MarkShapeModified();
    }

    public override void OnEntityDespawn(EntityDespawnData despawn)
    {
        if (registered && capi != null)
        {
            capi.Render.RemovePointLight(light);
            registered = false;
        }
        base.OnEntityDespawn(despawn);
    }

    private static bool HsvEquals(byte[] a, byte[] b)
    {
        if (a == null || b == null) return a == b;
        return a.Length == b.Length && a[0] == b[0] && a[1] == b[1] && a[2] == b[2];
    }

    // Converts an addon's light HSV to the point light's RGB colour. Approximates the engine's own
    // LightHsv -> colour mapping (its exact palette tables are engine-internal): hue+sat give the colour
    // direction, brightness the magnitude (which drives both intensity and reach in the shader).
    private static Vec3f ToPointLightColor(byte[] hsv)
    {
        int h = GameMath.Clamp(hsv[0] * ColorUtil.HueMul, 0, 255);
        int s = GameMath.Clamp(hsv[1] * ColorUtil.SatMul, 0, 255);
        int rgb = ColorUtil.HsvToRgb(h, s, 255);                        // v=255: hue+sat direction only; brightness applied via mag below
        float r = ((rgb >> 16) & 0xff) / 255f;
        float g = ((rgb >> 8) & 0xff) / 255f;
        float b = (rgb & 0xff) / 255f;
        float mag = hsv[2] * hsv[2] / (float)ColorUtil.BrightQuantities; // ~ vanilla's non-linear brightness curve
        // Shader reads Color.Z,Y,X as R,G,B (per ColorUtil.ToRGBVec3f), so pack B,G,R into X,Y,Z.
        return new(b * mag, g * mag, r * mag);
    }

    private byte[] ComputeWornLight()
    {
        if (entity is not EntityPlayer ep) return null;
        var inv = ep.Player?.InventoryManager?.GetOwnInventory(GlobalConstants.backpackInvClassName);
        if (inv == null) return null;

        var blockAccessor = entity.World.BlockAccessor;
        byte[] best = null;
        foreach (var item in inv)
        {
            var stack = item?.Itemstack;
            if (stack?.Collectible is not ItemImmersiveBag bag) continue;

            byte[] itemLight = bag.GetWornLight(stack, blockAccessor);
            if (itemLight != null && itemLight[2] > 0 && (best == null || itemLight[2] > best[2]))
                best = itemLight;
        }
        return best;
    }
}
