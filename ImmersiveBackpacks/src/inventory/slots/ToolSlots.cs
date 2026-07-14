using Vintagestory.API.Common;

namespace ImmersiveBackpacks.inventory.slots;

// Only tool slots subclass a vanilla slot; general and ore slots are plain vanilla ones (see
// BackpackSlotLayout.CreateBagSlot / CreateDialogSlot). Two reasons:
//
// 1. They don't need a subclass. Ore is enforced by the Metallurgy storage flag, which vanilla already checks in
//    CanTakeFrom, and a general slot restricts nothing - only the tool filter needs code.
// 2. Mods identify slots by type. Storage Tweaks decides what its sort may touch with
//    !SlotTypes.Contains(slot.GetType().Name) against a fixed list of vanilla names - a name match, not an
//    is-check, so any subclass is silently skipped. Keeping the ordinary slots vanilla keeps them sortable,
//    while tool slots stay ours and are therefore left alone by such a sort. Which is what we want: shuffling a
//    pickaxe off its toolstrap into general storage would be a poor sort.

/// <summary>Worn-bag tool slot: vanilla bag content, restricted to the tools a toolstrap renders.</summary>
public class ItemSlotToolBagContent : ItemSlotBagContent
{
    public ItemSlotToolBagContent(InventoryBase inventory, int bagIndex, int slotIndex,
        BackpackSlotLayout.SlotSpec spec)
        : base(inventory, bagIndex, slotIndex, spec.Flags)
        => HexBackgroundColor = spec.Color;

    public override bool CanHold(ItemSlot sourceSlot)
        => base.CanHold(sourceSlot) && BackpackSlotLayout.IsToolSlotItem(sourceSlot.Itemstack?.Collectible);

    // CanHold only gates the GUI drag path. Auto-fill - a pickup landing anywhere with room once the hotbar and
    // inventory are full - goes through CanTakeFrom, which vanilla does NOT route through CanHold; without this
    // a firelog ends up displayed on a toolstrap.
    public override bool CanTakeFrom(ItemSlot sourceSlot, EnumMergePriority priority = EnumMergePriority.AutoMerge)
        => base.CanTakeFrom(sourceSlot, priority)
           && BackpackSlotLayout.IsToolSlotItem(sourceSlot.Itemstack?.Collectible);
}

/// <summary>The same tool slot for the placed-backpack dialog, so placed and worn views filter alike.</summary>
public class ItemSlotToolSurvival : ItemSlotSurvival
{
    public ItemSlotToolSurvival(InventoryBase inventory, BackpackSlotLayout.SlotSpec spec) : base(inventory)
    {
        StorageType = spec.Flags;
        HexBackgroundColor = spec.Color;
    }

    public override bool CanHold(ItemSlot sourceSlot)
        => base.CanHold(sourceSlot) && BackpackSlotLayout.IsToolSlotItem(sourceSlot.Itemstack?.Collectible);

    public override bool CanTakeFrom(ItemSlot sourceSlot, EnumMergePriority priority = EnumMergePriority.AutoMerge)
        => base.CanTakeFrom(sourceSlot, priority)
           && BackpackSlotLayout.IsToolSlotItem(sourceSlot.Itemstack?.Collectible);
}
