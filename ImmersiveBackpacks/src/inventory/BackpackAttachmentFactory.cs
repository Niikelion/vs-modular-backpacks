#nullable enable
using System.Collections.Generic;
using ImmersiveModularBackpacks.Attachments;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

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
        bag.Clear(hydrated);

        var parent = new InventoryGeneric(1, null, null);
        var slots = bag.GetOrCreateSlots(hydrated, parent, 0, world);
        for (int i = 0; i < slots.Count; i++)
        {
            slots[i].Itemstack = i < (ownedCargo?.Count ?? 0) ? ownedCargo![i]?.Clone() : null;
            bag.Store(hydrated, slots[i]);
        }

        return AttachmentFactory.For(hydrated, world);
    }
}
