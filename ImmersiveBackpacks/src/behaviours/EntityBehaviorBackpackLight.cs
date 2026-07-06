using ImmersiveBackpacks.items;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

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
        byte[] hsv = ComputeWornLight();
        if (HsvEquals(hsv, lastPublishedHsv)) return;
        lastPublishedHsv = hsv;
        if (hsv != null) entity.WatchedAttributes.SetBytes(LightKey, hsv);
        else entity.WatchedAttributes.RemoveAttribute(LightKey);
        entity.WatchedAttributes.MarkPathDirty(LightKey);
    }

    // Client: render whatever the server published for this player (works for any player entity).
    private void ClientTick()
    {
        capi ??= entity.World.Api as ICoreClientAPI;
        if (capi == null) return;

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
