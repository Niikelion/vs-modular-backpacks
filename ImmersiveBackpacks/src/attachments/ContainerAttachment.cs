using Vintagestory.API.Common;

namespace ImmersiveBackpacks.attachments;

/// <summary>
/// A node that hosts children at its points, with the uniform storage every container shares: children live in
/// the node's OWN stack tree under <see cref="ChildrenTree"/>, keyed by point code. Because the children ride
/// inside the stack, they travel with it and render identically whether the node is placed, held or worn — and
/// a change to them moves the stack's hash, which is what the render caches key on. Subclasses differ only in
/// how they enumerate <see cref="IAttachment.Points"/> (a bag from config, a toolstrap from shape markers).
/// </summary>
public abstract class ContainerAttachment : AttachmentBase
{
    protected readonly IWorldAccessor World;

    protected ContainerAttachment(ItemStack stack, IWorldAccessor world) : base(stack) => World = world;

    /// <summary>Stack sub-tree holding this container's children, keyed by point code. Shared with the bag's
    /// existing addon storage so a bag and its nested containers use one uniform scheme.</summary>
    public const string ChildrenTree = "placed_addons";

    public override IAttachment GetAttached(string pointCode)
    {
        var tree = Stack.Attributes?.GetTreeAttribute(ChildrenTree);
        var s = tree?.GetItemstack(pointCode);
        if (s == null) return null;
        s.ResolveBlockOrItem(World);
        return AttachmentFactory.For(s, World);
    }
}
