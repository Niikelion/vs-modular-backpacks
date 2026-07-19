using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

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

    /// <summary>The slot spec (filter/flags/colour) for a slot type. Public so a standalone attachment-bag
    /// (the toolstrap worn on its own) can filter its own slots identically to when it's attached.</summary>
    public static SlotSpec SpecOf(BackpackSlotType type) => Spec(type);

    /// <summary>
    /// The slot spec for a config block that may override the preset picked by its <c>slotType</c>:
    /// <c>storageFlags</c> (int bitmask, or one/many <see cref="EnumItemStorageFlags"/> names) and
    /// <c>slotBgColor</c> (hex tint). Lets a compat patch give a bag or addon an arbitrary filter instead
    /// of choosing between our three presets. <paramref name="config"/> is the bag's <c>backpack</c> or the
    /// addon's <c>immersiveBackpackAttachment</c> attribute block.
    /// </summary>
    public static SlotSpec SpecFrom(BackpackSlotType type, JsonObject config)
    {
        var spec = Spec(type);
        if (config is not { Exists: true }) return spec;

        var flags = ParseStorageFlags(config["storageFlags"]);
        string color = config["slotBgColor"].AsString(spec.Color);
        return spec with { Flags = flags ?? spec.Flags, Color = color };
    }

    /// <summary>
    /// Storage flags from JSON, or null when unset/unparseable. Accepts a raw bitmask (<c>189</c>) or flag
    /// names, single or as a list (<c>"Metallurgy"</c>, <c>["General", "Agriculture"]</c>).
    /// </summary>
    public static EnumItemStorageFlags? ParseStorageFlags(JsonObject json)
    {
        if (json is not { Exists: true }) return null;

        // Enum.TryParse already handles a comma-separated list of names, so an array just joins into one.
        string text = json.IsArray()
            ? string.Join(",", json.AsArray<string>() ?? [])
            : json.AsString();
        if (string.IsNullOrWhiteSpace(text)) return null;

        if (int.TryParse(text, out int bits)) return (EnumItemStorageFlags)bits;
        return System.Enum.TryParse(text, ignoreCase: true, out EnumItemStorageFlags parsed) ? parsed : null;
    }

    /// <summary>Maps the JSON <c>immersiveBackpackAttachment.slotType</c> string to a slot type.</summary>
    public static BackpackSlotType TypeFromString(string slotType) => slotType switch
    {
        "ore" => BackpackSlotType.Ore,
        "tools" => BackpackSlotType.Tools,
        _ => BackpackSlotType.General
    };

    private static SlotSpec Spec(BackpackSlotType type) => type switch
    {
        // Ore: only metallic/ore items, vanilla mining-bag teal.
        BackpackSlotType.Ore => new(type, EnumItemStorageFlags.Metallurgy, "#c2ffe8"),
        // Tools: general flags but additionally gated to tools via CanHold; warm tint.
        BackpackSlotType.Tools => new(type, (EnumItemStorageFlags)DefaultStorageFlags, "#ffddaa"),
        _ => new(type, (EnumItemStorageFlags)DefaultStorageFlags, null)
    };

    /// <summary>
    /// Number of cargo slots an addon contributes. Slot-bearing addons are always bags (vanilla sacks and
    /// our pouches/toolstrap, all via the HeldBag behavior), so the count is exactly what the bag provides
    /// when worn and its contents map 1:1 in and out on attach/detach. Non-bag addons (lantern) contribute 0.
    /// </summary>
    public static int AddonSlotCount(ItemStack stack)
        => stack?.Collectible?.GetCollectibleInterface<IHeldBag>()?.GetQuantitySlots(stack) ?? 0;

    /// <summary>
    /// The unified-cargo slot range <c>[offset, offset+count)</c> each addon owns, aligned 1:1 with
    /// <paramref name="addonStacks"/> (and thus with the attachment points). Base slots come first, then each
    /// addon's run in order — the same ordering <see cref="Build"/> produces — so a toolstrap can find which
    /// cargo slots hold the tools that render on it. A non-slot addon (lantern, or an empty point) gets count 0.
    /// </summary>
    public static (int offset, int count)[] AddonRanges(int baseSlots, IReadOnlyList<ItemStack> addonStacks)
    {
        int n = addonStacks?.Count ?? 0;
        var ranges = new (int, int)[n];
        int off = baseSlots;
        for (int i = 0; i < n; i++)
        {
            int c = AddonSlotCount(addonStacks![i]);
            ranges[i] = (off, c);
            off += c;
        }
        return ranges;
    }

    /// <summary>
    /// The spec of the bag's own (base) slots: general unless its <c>backpack</c> attribute block overrides
    /// the flags/colour. <paramref name="bagAttributes"/> is the bag collectible's <c>Attributes</c>.
    /// </summary>
    public static SlotSpec BaseSpec(JsonObject bagAttributes)
        => SpecFrom(BackpackSlotType.General, bagAttributes?["backpack"]);

    /// <summary>The spec an addon contributes: its <c>slotType</c> preset, with any per-addon overrides.</summary>
    public static SlotSpec AddonSpec(CollectibleObject addon)
    {
        var config = addon?.Attributes?["immersiveBackpackAttachment"];
        return SpecFrom(TypeFromString(config?["slotType"].AsString()), config);
    }

    /// <summary>Builds the full slot layout: the bag's base slots followed by each addon's slots.</summary>
    public static SlotSpec[] Build(JsonObject bagAttributes, int baseSlots, IReadOnlyList<ItemStack> addonStacks)
    {
        var baseSpec = BaseSpec(bagAttributes);
        var list = new List<SlotSpec>(baseSlots);
        for (int i = 0; i < baseSlots; i++)
            list.Add(baseSpec);

        if (addonStacks == null) return list.ToArray();

        foreach (var stack in addonStacks)
        {
            int qty = AddonSlotCount(stack);
            if (qty <= 0) continue;
            var spec = AddonSpec(stack.Collectible);
            for (int j = 0; j < qty; j++)
                list.Add(spec);
        }

        return list.ToArray();
    }

    /// <summary>
    /// A worn-bag cargo slot for a spec. Only a tool slot needs a class of ours - ore is enforced by its storage
    /// flag and a general slot restricts nothing - and staying vanilla keeps those slots sortable by mods that
    /// identify slots by type name (Storage Tweaks). See <see cref="slots.ItemSlotToolBagContent"/>.
    /// </summary>
    public static ItemSlotBagContent CreateBagSlot(InventoryBase inv, int bagIndex, int slotIndex, SlotSpec spec)
        => spec.Type == BackpackSlotType.Tools
            ? new slots.ItemSlotToolBagContent(inv, bagIndex, slotIndex, spec)
            : new ItemSlotBagContent(inv, bagIndex, slotIndex, spec.Flags) { HexBackgroundColor = spec.Color };

    /// <summary>The same, for the placed backpack's dialog inventory.</summary>
    public static ItemSlotSurvival CreateDialogSlot(InventoryBase inv, SlotSpec spec)
        => spec.Type == BackpackSlotType.Tools
            ? new slots.ItemSlotToolSurvival(inv, spec)
            : new ItemSlotSurvival(inv) { StorageType = spec.Flags, HexBackgroundColor = spec.Color };

    /// <summary>Type-specific acceptance beyond storage flags (the Tools slot only takes digging tools).</summary>
    public static bool CanHold(BackpackSlotType type, ItemSlot sourceSlot)
    {
        if (type != BackpackSlotType.Tools) return true;
        return IsToolSlotItem(sourceSlot.Itemstack?.Collectible);
    }

    /// <summary>The tools a Tools slot (and a toolstrap) accepts: pickaxes, axes, shovels, hoes and prospecting picks.</summary>
    public static bool IsToolSlotItem(CollectibleObject collectible)
        => collectible?.Tool is EnumTool.Pickaxe or EnumTool.Axe or EnumTool.Shovel or EnumTool.Hoe or EnumTool.Probe;

    /// <summary>
    /// Position-sensitive hash of a bag's unified cargo (<c>backpack.slots</c>), used to invalidate the composed
    /// worn/held/GUI meshes when the contents change. Folds each slot's stack by index, so moving a tool between
    /// slots changes the hash - unlike <see cref="TreeAttribute.GetHashCode"/>, which XORs entries and so is
    /// unchanged when two slots swap their stacks (an axe moving off a toolstrap tool slot would render stale).
    /// </summary>
    public static int CargoHash(ITreeAttribute slots)
    {
        if (slots == null) return 0;
        int h = 17;
        for (int i = 0; slots.HasAttribute("slot-" + i); i++)
            h = h * 31 + ((slots["slot-" + i] as ItemstackAttribute)?.value?.GetHashCode() ?? 0);
        return h;
    }
}
