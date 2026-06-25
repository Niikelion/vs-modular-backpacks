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
}
