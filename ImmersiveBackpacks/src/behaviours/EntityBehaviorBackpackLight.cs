using ImmersiveBackpacks.items;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace ImmersiveBackpacks.behaviours;

/// <summary>
/// Lights the wearer from a worn backpack's light addons (e.g., a lantern) via a dynamic <see cref="IPointLight"/>.
/// Client-side only (dynamic light is client-render-only, and only the local player has resolved backpack
/// contents); counts against the "Dynamic lights" graphics-setting budget like any other dynamic light.
/// </summary>
public class EntityBehaviorBackpackLight(Entity entity) : EntityBehavior(entity)
{
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

    public override string PropertyName() => "immersivebackpacklight";

    public override void OnGameTick(float deltaTime)
    {
        if (entity.World.Side != EnumAppSide.Client) return;
        capi ??= entity.World.Api as ICoreClientAPI;
        if (capi == null) return;

        accum += deltaTime;
        if (accum < 0.5f) return;
        accum = 0;

        byte[] hsv = ComputeWornLight();
        if (hsv != null)
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
