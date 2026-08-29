using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace ImmersiveBackpacks.inventory;

/// <summary>Builds the shared placed and worn cargo layout from vanilla bag contracts.</summary>
public static class BackpackSlotLayout
{
    public const int DefaultStorageFlags = 189;

    public record SlotSpec(EnumItemStorageFlags Flags, TagSet Tags, string Color);

    public static int AddonSlotCount(ItemStack stack)
        => stack?.Collectible?.GetCollectibleInterface<IHeldBag>()?.GetQuantitySlots(stack) ?? 0;

    public static (int offset, int count)[] AddonRanges(int baseSlots, IReadOnlyList<ItemStack> addonStacks)
    {
        int n = addonStacks?.Count ?? 0;
        var ranges = new (int, int)[n];
        int off = baseSlots;
        for (int i = 0; i < n; i++)
        {
            int count = AddonSlotCount(addonStacks![i]);
            ranges[i] = (off, count);
            off += count;
        }
        return ranges;
    }

    public static SlotSpec BaseSpec(JsonObject bagAttributes)
    {
        var config = bagAttributes?["backpack"];
        return new(
            (EnumItemStorageFlags)(config?["storageFlags"].AsInt(DefaultStorageFlags) ?? DefaultStorageFlags),
            TagSet.Empty,
            config?["slotBgColor"].AsString());
    }

    public static SlotSpec AddonSpec(ItemStack stack)
    {
        var bag = stack?.Collectible?.GetCollectibleInterface<IHeldBag>();
        return bag == null
            ? new((EnumItemStorageFlags)DefaultStorageFlags, TagSet.Empty, null)
            : new(bag.GetStorageFlags(stack), bag.GetStorageTags(stack), bag.GetSlotBgColor(stack));
    }

    public static SlotSpec[] Build(JsonObject bagAttributes, int baseSlots, IReadOnlyList<ItemStack> addonStacks)
    {
        var list = new List<SlotSpec>(baseSlots);
        var baseSpec = BaseSpec(bagAttributes);
        for (int i = 0; i < baseSlots; i++) list.Add(baseSpec);

        if (addonStacks == null) return list.ToArray();

        foreach (var stack in addonStacks)
        {
            int count = AddonSlotCount(stack);
            if (count <= 0) continue;
            var spec = AddonSpec(stack);
            for (int i = 0; i < count; i++) list.Add(spec);
        }
        return list.ToArray();
    }

    public static ItemSlotBagContent CreateBagSlot(InventoryBase inv, int bagIndex, int slotIndex, SlotSpec spec)
        => new(inv, bagIndex, slotIndex, spec.Flags)
        {
            CanStoreTags = spec.Tags,
            HexBackgroundColor = spec.Color
        };

    public static ItemSlotSurvival CreateDialogSlot(InventoryBase inv, SlotSpec spec)
        => new(inv)
        {
            StorageType = spec.Flags,
            CanStoreTags = spec.Tags,
            HexBackgroundColor = spec.Color
        };

    public static int CargoHash(ITreeAttribute slots)
    {
        if (slots == null) return 0;
        int hash = 17;
        for (int i = 0; slots.HasAttribute("slot-" + i); i++)
            hash = hash * 31 + ((slots["slot-" + i] as ItemstackAttribute)?.value?.GetHashCode() ?? 0);
        return hash;
    }
}
