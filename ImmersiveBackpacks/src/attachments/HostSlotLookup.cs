using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

namespace ImmersiveBackpacks.attachments;

/// <summary>
/// Recovers the attachment slot a stack occupies on a host entity (an Equus horse, a mannequin, ...).
///
/// Vanilla passes the slot code to <see cref="IAttachableToEntity.GetAttachedShape"/> but NOT to
/// <c>IWearableShapeSupplier.GetShape</c> - and GetShape is the one that can compose addons, since it returns a
/// live Shape rather than a path. Without the code, a mounted bag could only ever be the bare pre-authored asset.
///
/// The host's <c>EntityBehaviorAttachable</c> keeps its gear in an inventory whose indices line up 1:1 with its
/// <c>wearableSlots</c> config - vanilla indexes them that way itself - so the slot holding the stack identifies
/// the config entry, whose Code is what we need. That config array is internal, but the JSON it is parsed from is
/// public: entity load merges <c>behaviorConfigs</c> into <see cref="EntitySidedProperties.BehaviorsAsJsonObj"/>.
/// </summary>
public static class HostSlotLookup
{
    /// <summary>Registered name of EntityBehaviorAttachable.</summary>
    private const string BehaviorCode = "rideableaccessories";

    /// <summary>The host slot code holding <paramref name="stack"/>, or null if the host has no such slot.</summary>
    public static string SlotCodeFor(Entity host, ItemStack stack)
    {
        InventoryBase inv = host?.GetBehavior<EntityBehaviorAttachable>()?.Inventory;
        if (inv == null || stack == null) return null;

        int index = -1;
        for (int i = 0; i < inv.Count; i++)
        {
            if (ReferenceEquals(inv[i]?.Itemstack, stack))
            {
                index = i;
                break;
            }
        }

        if (index < 0) return null;

        WearableSlotConfig[] slots = SlotConfigs(host);
        return slots != null && index < slots.Length ? slots[index].Code : null;
    }

    private static WearableSlotConfig[] SlotConfigs(Entity host)
    {
        JsonObject[][] sides =
        [
            host.Properties?.Client?.BehaviorsAsJsonObj,
            host.Properties?.Server?.BehaviorsAsJsonObj
        ];

        foreach (JsonObject[] behaviors in sides)
        {
            if (behaviors == null) continue;
            foreach (JsonObject behavior in behaviors)
            {
                if (behavior["code"].AsString() != BehaviorCode) continue;

                var slots = behavior["wearableSlots"].AsObject<WearableSlotConfig[]>(null);
                if (slots != null) return slots;
            }
        }

        return null;
    }
}
