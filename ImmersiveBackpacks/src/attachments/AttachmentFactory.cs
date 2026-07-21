using System.Collections.Generic;
using Vintagestory.API.Common;

namespace ImmersiveBackpacks.attachments;

/// <summary>
/// Helper for converting ItemStack to <see cref="IAttachment"/> node.
/// </summary>
public static class AttachmentFactory
{
    /// <summary>Builds an addon's node, given the host cargo it owns. An addon supplying an
    /// <see cref="IAttachmentBuilder"/> (class or behavior) builds its own container over that cargo; else a leaf.</summary>
    public static IAttachment For(ItemStack itemStack, IWorldAccessor world, IReadOnlyList<ItemStack> ownedCargo = null)
    {
        if (itemStack?.Collectible is not { } collectible) return null;

        var builder = collectible.GetCollectibleInterface<IAttachmentBuilder>();
        return builder != null ? builder.Build(itemStack, world, ownedCargo) : new ItemAttachment(itemStack);
    }
}
