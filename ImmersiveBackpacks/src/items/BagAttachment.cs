using System.Collections.Generic;
using ImmersiveBackpacks.attachments;
using Vintagestory.API.Common;

namespace ImmersiveBackpacks.items;

/// <summary>
/// Value-host node view of a bag stack for the attachment composer: its points come from the bag's
/// <c>immersiveBackpack.attachmentPoints</c> config, and children (addons) are read from the shared
/// <see cref="ContainerAttachment.ChildrenTree"/>. For a container addon whose contents live in the bag's
/// unified cargo (a toolstrap and its tools), the host precomputes the owned cargo slice per point and hands
/// it in via <paramref name="toolsByPoint"/>, so <see cref="ForBagChild"/> can build the container over its
/// cargo. Reconstructed per render (the bag Item is a singleton); holds no state beyond the stack + slices.
/// </summary>
public sealed class BagAttachment : ContainerAttachment
{
    private readonly IReadOnlyList<IAttachmentPoint> points;
    private readonly IReadOnlyDictionary<string, IReadOnlyList<ItemStack>> toolsByPoint;

    public BagAttachment(ItemStack stack, IReadOnlyList<IAttachmentPoint> points,
        IReadOnlyDictionary<string, IReadOnlyList<ItemStack>> toolsByPoint, IWorldAccessor world)
        : base(stack, world)
    {
        this.points = points;
        this.toolsByPoint = toolsByPoint;
    }

    public override IReadOnlyList<IAttachmentPoint> Points => points;

    public override IAttachment GetAttached(string pointCode)
    {
        var tree = Stack.Attributes?.GetTreeAttribute(ChildrenTree);
        var s = tree?.GetItemstack(pointCode);
        if (s == null) return null;
        s.ResolveBlockOrItem(World);

        IReadOnlyList<ItemStack> owned = null;
        toolsByPoint?.TryGetValue(pointCode, out owned);
        return AttachmentFactory.For(s, World, owned);
    }
}
