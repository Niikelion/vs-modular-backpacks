using System.Collections.Generic;
using ImmersiveBackpacks.attachments;
using Vintagestory.API.Common;

namespace ImmersiveBackpacks.items;

/// <summary>
/// Value-host node view of a bag stack for the attachment composer: its points come from the bag's
/// <c>immersiveBackpack.attachmentPoints</c> config, and each occupant is read from the stack's
/// <c>placed_addons</c> tree and wrapped as a leaf <see cref="ItemAttachment"/>. Reconstructed per render
/// (the bag Item is a singleton), so it holds no state beyond the stack — matching the reconstructible-from-tree
/// contract for value hosts. The worn root's base shape is built by <c>ItemImmersiveBag</c> itself and composed
/// via <see cref="AttachmentComposer.ComposeChildrenInto"/>; the held/GUI mesh uses
/// <see cref="AttachmentComposer.ComposeMesh"/> over this node.
/// </summary>
public sealed class BagAttachment : AttachmentBase
{
    private readonly IReadOnlyList<IAttachmentPoint> points;
    private readonly IWorldAccessor world;

    public BagAttachment(ItemStack stack, IReadOnlyList<IAttachmentPoint> points, IWorldAccessor world)
        : base(stack)
    {
        this.points = points;
        this.world = world;
    }

    public override IReadOnlyList<IAttachmentPoint> Points => points;

    public override IAttachment GetAttached(string pointCode)
    {
        var tree = Stack.Attributes?.GetTreeAttribute("placed_addons");
        var s = tree?.GetItemstack(pointCode);
        if (s == null) return null;
        s.ResolveBlockOrItem(world);
        return AttachmentFactory.For(s, world);
    }
}
