#nullable enable
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace ImmersiveBackpacks.inventory;

/// <summary>Stable access to the pre-2.0 backpack save layout.</summary>
internal static class BackpackSaveData
{
    internal const string AddonsKey = "placed_addons";
    internal const string BackpackKey = "backpack";
    internal const string SlotsKey = "slots";

    internal static ITreeAttribute? GetAddons(ITreeAttribute? root)
        => root?.GetTreeAttribute(AddonsKey);

    internal static void SetAddons(ITreeAttribute root, ITreeAttribute addons)
        => root[AddonsKey] = addons;

    internal static ITreeAttribute? GetHeldSlots(ITreeAttribute? root)
        => root?.GetTreeAttribute(BackpackKey)?.GetTreeAttribute(SlotsKey);

    internal static void SetHeldSlots(ITreeAttribute root, ITreeAttribute slots)
    {
        var backpack = root.GetTreeAttribute(BackpackKey);
        if (backpack == null)
        {
            backpack = new TreeAttribute();
            root[BackpackKey] = backpack;
        }
        backpack[SlotsKey] = slots;
    }

    internal static ItemStack? GetStack(ITreeAttribute? tree, string key)
        => tree?.GetItemstack(key);
}
