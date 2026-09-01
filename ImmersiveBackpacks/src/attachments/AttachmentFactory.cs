#nullable enable
using Vintagestory.API.Common;

namespace ImmersiveModularBackpacks.Attachments;

/// <summary>
/// Helper for converting ItemStack to <see cref="IAttachment"/> node.
/// </summary>
public static class AttachmentFactory
{
    /// <summary>Builds a node from the complete state carried by its stack.</summary>
    public static IAttachment? For(ItemStack? itemStack, IWorldAccessor world)
    {
        if (itemStack?.Collectible is not { } collectible) return null;

        var builder = collectible.GetCollectibleInterface<IAttachmentBuilder>();
        return builder != null ? builder.Build(itemStack, world) : new ItemAttachment(itemStack);
    }
}
