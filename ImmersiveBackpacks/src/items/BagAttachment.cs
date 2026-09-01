#nullable enable
using System.Collections.Generic;
using ImmersiveModularBackpacks.Attachments;
using ImmersiveBackpacks.inventory;
using Vintagestory.API.Common;

namespace ImmersiveBackpacks.items;

/// <summary>
/// Value-host node view of a bag stack for the attachment composer: its points come from the bag's
/// <c>immersiveBackpack.attachmentPoints</c> config, and children (addons) are read from the shared
/// <c>placed_addons</c> stack subtree. For a container addon whose contents live in the bag's
/// unified cargo, the host precomputes the owned slice per point and projects it through the addon's
/// <c>IHeldBag</c> contract. Reconstructed per render; holds no state beyond the stack and cargo slices.
/// </summary>
public sealed class BagAttachment : AttachmentBase
{
    private readonly IReadOnlyList<IAttachmentPoint> points;
    private readonly IReadOnlyDictionary<string, IReadOnlyList<ItemStack>> toolsByPoint;
    private readonly IWorldAccessor world;

    public BagAttachment(ItemStack stack, IReadOnlyList<IAttachmentPoint> points,
        IReadOnlyDictionary<string, IReadOnlyList<ItemStack>> toolsByPoint, IWorldAccessor world)
        : base(stack)
    {
        this.points = points;
        this.toolsByPoint = toolsByPoint;
        this.world = world;
    }

    public override IReadOnlyList<IAttachmentPoint> Points => points;

    public override IAttachment? GetAttached(string pointCode)
    {
        var s = BackpackSaveData.GetStack(BackpackSaveData.GetAddons(Stack.Attributes), pointCode);
        if (s == null) return null;
        s.ResolveBlockOrItem(world);

        toolsByPoint.TryGetValue(pointCode, out var owned);
        return BackpackAttachmentFactory.For(s, world, owned);
    }
}
