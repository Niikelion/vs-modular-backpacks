using System.Collections.Generic;
using ImmersiveBackpacks.inventory;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

namespace ImmersiveBackpacks.behaviours;

/// <summary>
/// A standalone bag whose OWN worn/held slots carry the same filter the parent backpack would apply to this
/// addon (from <c>immersiveBackpackAttachment.slotType</c>) - so items can't be laundered through unfiltered
/// vanilla slots before attaching. Extends vanilla <see cref="CollectibleBehaviorHeldBag"/> (keeping its
/// attach/interaction plumbing) and only replaces the slot construction + storage flags. Replaces the vanilla
/// <c>HeldBag</c> behavior in JSON rather than coexisting, so it is the single <see cref="IHeldBag"/> provider.
/// Generic: any filtered wearable addon can use it, not just the toolstrap.
/// </summary>
public class FilteredBagBehavior(CollectibleObject collObj) : CollectibleBehaviorHeldBag(collObj), IHeldBag
{
    // Same spec the parent bag builds for this addon, so the filter matches whether worn standalone or attached.
    private BackpackSlotLayout.SlotSpec SpecFor() => BackpackSlotLayout.AddonSpec(collObj);

    public override EnumItemStorageFlags GetStorageFlags(ItemStack bagstack) => SpecFor().Flags;

    public override string GetSlotBgColor(ItemStack bagstack) => null;   // colour comes from the per-slot spec

    // Non-virtual in the base and builds unfiltered ItemSlotBagContent; re-implemented (interface re-mapped via
    // the IHeldBag re-declaration above) to build filtered slots through CreateBagSlot.
    public new List<ItemSlotBagContent> GetOrCreateSlots(ItemStack bagstack, InventoryBase parentinv,
        int bagIndex, IWorldAccessor world)
    {
        int n = GetQuantitySlots(bagstack);
        var spec = SpecFor();
        var slots = SlotsTree(bagstack, create: true);

        var list = new List<ItemSlotBagContent>(n);
        for (int i = 0; i < n; i++)
        {
            var slot = BackpackSlotLayout.CreateBagSlot(parentinv, bagIndex, i, spec);
            string key = "slot-" + i;
            if (slots[key] is ItemstackAttribute { value: { } stored })
            {
                slot.Itemstack = stored;
                slot.Itemstack.ResolveBlockOrItem(world);
            }
            else
            {
                slots[key] = new ItemstackAttribute(null);
            }
            list.Add(slot);
        }
        return list;
    }

    // The vanilla IHeldBag content tree on a bag stack: backpack -> slots -> slot-{i}.
    private static ITreeAttribute SlotsTree(ItemStack bagstack, bool create)
    {
        var backpack = bagstack.Attributes.GetTreeAttribute("backpack");
        if (backpack == null)
        {
            if (!create) return null;
            backpack = new TreeAttribute();
            bagstack.Attributes["backpack"] = backpack;
        }
        var slots = backpack.GetTreeAttribute("slots");
        if (slots != null) return slots;
        if (!create) return null;
        slots = new TreeAttribute();
        backpack["slots"] = slots;
        return slots;
    }
}
