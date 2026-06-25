using ImmersiveBackpacks.items;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;

namespace ImmersiveBackpacks.behaviours;

/// <summary>
/// Makes a worn backpack's attached light addons (e.g. a torch) light up the wearer. Vanilla only
/// derives entity light from the character/gear inventory, never from bag contents, so we top up
/// <see cref="Entity.LightHsv"/> here. We only ever raise it (max-boost) and let vanilla own the base
/// value: taking the bag off re-tesselates the player, which resets LightHsv, after which we stop boosting.
/// Client-side only — entity dynamic light is a client render concern and only the local player has its
/// own backpack contents resolved.
/// </summary>
public class EntityBehaviorBackpackLight : EntityBehavior
{
    private float accum;

    public EntityBehaviorBackpackLight(Entity entity) : base(entity) { }

    public override string PropertyName() => "immersivebackpacklight";

    public override void OnGameTick(float deltaTime)
    {
        if (entity.World.Side != EnumAppSide.Client) return;

        accum += deltaTime;
        if (accum < 0.5f) return;
        accum = 0;

        var light = ComputeWornLight();
        if (light != null && (entity.LightHsv == null || light[2] > entity.LightHsv[2]))
            entity.LightHsv = light;
    }

    private byte[] ComputeWornLight()
    {
        if (entity is not EntityPlayer ep) return null;
        var inv = ep.Player?.InventoryManager?.GetOwnInventory(GlobalConstants.backpackInvClassName);
        if (inv == null) return null;

        var blockAccessor = entity.World.BlockAccessor;
        byte[] best = null;
        for (int i = 0; i < inv.Count; i++)
        {
            var stack = inv[i]?.Itemstack;
            if (stack?.Collectible is not ItemImmersiveBag bag) continue;

            var light = bag.GetWornLight(stack, blockAccessor);
            if (light != null && light[2] > 0 && (best == null || light[2] > best[2]))
                best = light;
        }
        return best;
    }
}
