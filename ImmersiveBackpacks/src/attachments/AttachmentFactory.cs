using Vintagestory.API.Common;

namespace ImmersiveBackpacks.attachments;

/// <summary>
/// Turns a stored addon stack into its <see cref="IAttachment"/> node. The single dispatch point every render
/// path (placed renderer, held/GUI mesh, worn shape) goes through, so adding a container attachment type
/// (a toolstrap that hosts tools) is a one-line change here rather than a change in each host. Today every
/// addon is a leaf <see cref="ItemAttachment"/>.
/// </summary>
public static class AttachmentFactory
{
    public static IAttachment For(ItemStack stack, IWorldAccessor world)
    {
        if (stack?.Collectible == null) return null;

        // Future: dispatch to a container node (e.g. ToolstrapAttachment) by collectible attribute/type,
        // passing `world` so it can resolve its own children. Leaves ignore `world`.
        return new ItemAttachment(stack);
    }
}
