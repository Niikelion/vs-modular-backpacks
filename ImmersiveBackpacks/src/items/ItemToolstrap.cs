using System.Collections.Generic;
using ImmersiveBackpacks.inventory;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

namespace ImmersiveBackpacks.items;

/// <summary>
/// A standalone attachment-bag (the toolstrap) that filters its OWN worn/held slots by its declared
/// <c>immersiveBackpackAttachment.slotType</c>. Vanilla's <c>HeldBag</c> behavior gives such a bag unfiltered
/// general slots, so a player could wear the strap alone, stash any item, then attach it to a backpack - the
/// items would flow into the backpack's tool-only slots, bypassing the filter. Implementing <see cref="IHeldBag"/>
/// on the item class wins over the behavior (vanilla resolves the class before behaviors), so the strap's slots
/// carry the same <see cref="ItemSlotBagFiltered"/> filter everywhere. Storage uses the vanilla
/// <c>backpack.slots</c> tree so contents map 1:1 in and out on attach/detach.
/// </summary>
public class ItemToolstrap : Item, IHeldBag
{
    private BackpackSlotType SlotTypeOf()
        => BackpackSlotLayout.TypeFromString(Attributes?["immersiveBackpackAttachment"]?["slotType"]?.AsString());

    private BackpackSlotLayout.SlotSpec SpecFor()
        => BackpackSlotLayout.SpecOf(SlotTypeOf());

    public int GetQuantitySlots(ItemStack bagstack)
        => Attributes?["backpack"]?["quantitySlots"]?.AsInt(0) ?? 0;

    public List<ItemSlotBagContent> GetOrCreateSlots(ItemStack bagstack, InventoryBase parentinv,
        int bagIndex, IWorldAccessor world)
    {
        int n = GetQuantitySlots(bagstack);
        var spec = SpecFor();
        var slots = SlotsTree(bagstack, create: true);

        var list = new List<ItemSlotBagContent>(n);
        for (int i = 0; i < n; i++)
        {
            var slot = new ItemSlotBagFiltered(parentinv, bagIndex, i, spec);
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

    public ItemStack[] GetContents(ItemStack bagstack, IWorldAccessor world)
    {
        var slots = SlotsTree(bagstack, create: false);
        if (slots == null) return null;

        int n = GetQuantitySlots(bagstack);
        var contents = new ItemStack[n];
        for (int i = 0; i < n; i++)
        {
            var stack = (slots["slot-" + i] as ItemstackAttribute)?.value;
            stack?.ResolveBlockOrItem(world);
            contents[i] = stack;
        }
        return contents;
    }

    public void Store(ItemStack bagstack, ItemSlotBagContent slot)
        => SlotsTree(bagstack, create: true)["slot-" + slot.SlotIndex]
            = new ItemstackAttribute(slot.Itemstack);

    public void Clear(ItemStack bagstack)
    {
        var backpack = bagstack.Attributes.GetTreeAttribute("backpack");
        if (backpack != null) backpack["slots"] = new TreeAttribute();
    }

    public bool IsEmpty(ItemStack bagstack)
    {
        var slots = SlotsTree(bagstack, create: false);
        if (slots == null) return true;
        foreach (var kv in slots)
            if ((kv.Value as ItemstackAttribute)?.value?.StackSize > 0) return false;
        return true;
    }

    public string GetSlotBgColor(ItemStack bagstack) => null;   // colour comes from the per-slot spec

    EnumItemStorageFlags IHeldBag.GetStorageFlags(ItemStack bagstack) => SpecFor().Flags;

    public TagSet GetStorageTags(ItemStack bagStack) => TagSet.Empty;

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
        if (slots == null)
        {
            if (!create) return null;
            slots = new TreeAttribute();
            backpack["slots"] = slots;
        }
        return slots;
    }
}
