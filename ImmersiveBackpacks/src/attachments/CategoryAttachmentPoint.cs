using System;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ImmersiveBackpacks.attachments;

/// <summary>
/// A point that accepts occupants by category match (the bag's <c>immersiveBackpack.attachmentPoints</c> config,
/// placed and worn). See <see cref="AttachmentPointBase"/> for the shared geometry.
/// </summary>
public sealed class CategoryAttachmentPoint : AttachmentPointBase
{
    public string[] Categories { get; }

    public CategoryAttachmentPoint(string code, string[] categories, Cuboidf box,
        AttachmentTransform transform = null, Vec3f origin = null)
        : base(code, box, transform, origin)
    {
        Categories = categories ?? Array.Empty<string>();
    }

    public override bool Accepts(IAttachment attachment)
        => AttachmentCategories.Accepts(Categories, attachment?.Stack?.Collectible);
}
