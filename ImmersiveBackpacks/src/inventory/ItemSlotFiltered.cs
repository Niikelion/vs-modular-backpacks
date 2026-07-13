using Vintagestory.API.Common;

namespace ImmersiveBackpacks.inventory;

/// <summary>
/// Filtered cargo slot for the placed-backpack dialog. Mirrors the worn <see cref="ItemSlotBagFiltered"/>
/// so the placed and worn views show the same coloured, type-restricted slots.
/// </summary>
public class ItemSlotFiltered : ItemSlotSurvival
{
    private readonly BackpackSlotType type;

    public ItemSlotFiltered(InventoryBase inventory, BackpackSlotLayout.SlotSpec spec) : base(inventory)
    {
        type = spec.Type;
        StorageType = spec.Flags;
        HexBackgroundColor = spec.Color;
    }

    public override bool CanHold(ItemSlot sourceSlot)
    {
        if (!base.CanHold(sourceSlot)) return false;
        return BackpackSlotLayout.CanHold(type, sourceSlot);
    }

    // See ItemSlotBagFiltered.CanTakeFrom: vanilla's auto-fill path bypasses CanHold, so the type filter has to
    // be repeated here or a shift-click/quick-stack drops any item into a tool slot.
    public override bool CanTakeFrom(ItemSlot sourceSlot, EnumMergePriority priority = EnumMergePriority.AutoMerge)
    {
        if (!base.CanTakeFrom(sourceSlot, priority)) return false;
        return BackpackSlotLayout.CanHold(type, sourceSlot);
    }
}
