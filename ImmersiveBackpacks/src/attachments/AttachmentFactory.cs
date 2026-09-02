#nullable enable
using Vintagestory.API.Common;

namespace ImmersiveModularBackpacks.Attachments;

/// <summary>
/// Helper for converting ItemStack to <see cref="IAttachment"/> node.
/// </summary>
public static class AttachmentFactory
{
    /// <summary>Builds a node from the complete state carried by its stack.</summary>
    public static IAttachment? For(ItemStack? itemStack, IWorldAccessor world,
        IAttachmentPoint? attachmentPoint = null)
    {
        if (itemStack?.Collectible is not { } collectible) return null;

        var builder = collectible.GetCollectibleInterface<IAttachmentBuilder>();
        IAttachment attachment = builder != null ? builder.Build(itemStack, world) : new ItemAttachment(itemStack);
        return attachmentPoint == null ? attachment : WithPointContext(attachment, attachmentPoint);
    }

    /// <summary>Supplies optional parent-point metadata without adding it to the core attachment contract.</summary>
    public static IAttachment? WithPointContext(IAttachment? attachment, IAttachmentPoint point)
    {
        if (attachment is IAttachmentPointContextReceiver receiver)
            receiver.SetAttachmentPointContext(point);
        return attachment;
    }
}
