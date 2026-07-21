#nullable enable
using System.Collections.Generic;
using Vintagestory.API.Common;

namespace ImmersiveBackpacks.attachments;

/// <summary>
/// Collectible-level capability: this addon builds its own <see cref="IAttachment"/> node when attached to a
/// backpack point, instead of resolving to a plain leaf. Implemented either directly on the collectible's
/// Item/Block class or via a <c>CollectibleBehavior</c> (a toolstrap) — the class wins, mirroring vanilla's
/// IAttachableToEntity/IWearableShapeSupplier resolution — so <see cref="AttachmentFactory.For(ItemStack,IWorldAccessor,System.Collections.Generic.IReadOnlyList{Vintagestory.API.Common.ItemStack})"/> stays
/// generic: it asks the collectible how to build its node rather than switching on a category string. A
/// collectible supplying neither is a leaf. The category attribute remains data for point acceptance; only
/// construction moves here.
/// </summary>
public interface IAttachmentBuilder
{
    /// <summary>Builds the node for this addon, given the unified-cargo slots it owns on the host (a toolstrap's
    /// tools; empty/null for a container that doesn't draw from cargo).</summary>
    IAttachment Build(ItemStack stack, IWorldAccessor world, IReadOnlyList<ItemStack>? ownedCargo = null);
}
