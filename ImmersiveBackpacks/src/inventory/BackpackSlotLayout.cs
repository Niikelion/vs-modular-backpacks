using System.Collections.Generic;
using Vintagestory.API.Common;

namespace ImmersiveBackpacks.inventory;

public enum BackpackSlotType { General, Ore, Tools }

/// <summary>
/// Single source of truth for how a backpack's cargo slots are laid out: the base slots plus a run of
/// slots contributed by each attached addon, each with its filter type, storage flags and GUI colour.
/// Shared by the placed block entity's dialog inventory and the worn-bag <c>IHeldBag</c> slots so both
/// show the same coloured, filtered slots and store into the same vanilla <c>backpack.slots</c> tree.
/// </summary>
public static class BackpackSlotLayout
{
    // Vanilla default bag storage flags (everything except Backpack-nesting and Outfit). Matches
    // CollectibleBehaviorHeldBag.defaultFlags.
    public const int DefaultStorageFlags = 189;

    public record SlotSpec(BackpackSlotType Type, EnumItemStorageFlags Flags, string Color);

    private static SlotSpec Spec(BackpackSlotType type) => type switch
    {
        // Ore: only metallic/ore items, vanilla mining-bag teal.
        BackpackSlotType.Ore => new(type, EnumItemStorageFlags.Metallurgy, "#c2ffe8"),
        // Tools: general flags but additionally gated to tools via CanHold; warm tint.
        BackpackSlotType.Tools => new(type, (EnumItemStorageFlags)DefaultStorageFlags, "#ffddaa"),
        _ => new(type, (EnumItemStorageFlags)DefaultStorageFlags, null)
    };

    /// <summary>Builds the full slot layout: base general slots followed by each addon's slots.</summary>
    public static SlotSpec[] Build(int baseSlots, IReadOnlyList<ItemStack> addonStacks)
    {
        var list = new List<SlotSpec>(baseSlots);
        for (int i = 0; i < baseSlots; i++)
            list.Add(Spec(BackpackSlotType.General));

        if (addonStacks != null)
            foreach (var stack in addonStacks)
            {
                var attr = stack?.Collectible?.Attributes?["immersiveBackpackAttachment"];
                if (attr == null || !attr.Exists) continue;
                int qty = attr["quantitySlots"].AsInt(0);
                var type = attr["slotType"].AsString() switch
                {
                    "ore" => BackpackSlotType.Ore,
                    "tools" => BackpackSlotType.Tools,
                    _ => BackpackSlotType.General
                };
                for (int j = 0; j < qty; j++)
                    list.Add(Spec(type));
            }

        return list.ToArray();
    }

    /// <summary>Type-specific acceptance beyond storage flags (tools must be actual tools).</summary>
    public static bool CanHold(BackpackSlotType type, ItemSlot sourceSlot)
    {
        if (type != BackpackSlotType.Tools) return true;
        return sourceSlot.Itemstack?.Collectible.ToolTier > 0;
    }
}

/// <summary>Worn-bag content slot carrying a layout spec for per-slot colour and tool filtering.</summary>
public class ItemSlotBagFiltered : ItemSlotBagContent
{
    private readonly BackpackSlotType type;

    public ItemSlotBagFiltered(InventoryBase inventory, int bagIndex, int slotIndex,
        BackpackSlotLayout.SlotSpec spec)
        : base(inventory, bagIndex, slotIndex, spec.Flags)
    {
        type = spec.Type;
        HexBackgroundColor = spec.Color;
    }

    public override bool CanHold(ItemSlot sourceSlot)
    {
        if (!base.CanHold(sourceSlot)) return false;
        return BackpackSlotLayout.CanHold(type, sourceSlot);
    }
}
