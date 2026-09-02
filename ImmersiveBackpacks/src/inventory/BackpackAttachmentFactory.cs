#nullable enable
using System.Collections.Generic;
using ImmersiveModularBackpacks.Attachments;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace ImmersiveBackpacks.inventory;

/// <summary>Builds attachment views whose host-owned cargo is projected back through <see cref="IHeldBag"/>.</summary>
internal static class BackpackAttachmentFactory
{
    public static IAttachment? For(ItemStack? stack, IWorldAccessor world,
        IReadOnlyList<ItemStack?>? ownedCargo)
    {
        if (stack?.Collectible?.GetCollectibleInterface<IHeldBag>() is not { } bag)
            return AttachmentFactory.For(stack, world);

        var hydrated = stack.Clone();
        var slots = new TreeAttribute();
        int count = bag.GetQuantitySlots(hydrated);
        for (int i = 0; i < count; i++)
            slots[$"slot-{i}"] = new ItemstackAttribute(
                i < (ownedCargo?.Count ?? 0) ? ownedCargo![i]?.Clone() : null);
        BackpackSaveData.SetHeldSlots(hydrated.Attributes, slots);

        return AttachmentFactory.For(hydrated, world);
    }
}
