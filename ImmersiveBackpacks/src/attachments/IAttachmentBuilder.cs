#nullable enable
using Vintagestory.API.Common;

namespace ImmersiveModularBackpacks.Attachments;

/// <summary>
/// Collectible-level capability: this addon builds its own <see cref="IAttachment"/> node when attached to a
/// attachment point, instead of resolving to a plain leaf. Implemented either directly on the collectible's
/// Item/Block class or via a <c>CollectibleBehavior</c>. The stack contains all instance state needed to build
/// the node; host-specific storage projection happens before this boundary.
/// </summary>
public interface IAttachmentBuilder
{
    IAttachment Build(ItemStack stack, IWorldAccessor world);
}
