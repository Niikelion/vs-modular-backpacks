using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ImmersiveModularBackpacks.Attachments;

/// <summary>
/// A point that accepts occupants by category match (the bag's <c>immersiveBackpack.attachmentPoints</c> config,
/// placed and worn). See <see cref="AttachmentPointBase"/> for the shared geometry.
/// </summary>
public sealed class CategoryAttachmentPoint : AttachmentPointBase
{
    public string[] Categories { get; }

    public CategoryAttachmentPoint(string code, string[] categories, Cuboidf box,
        AttachmentTransform transform = null, Vec3f origin = null,
        AttachmentMirror mirror = AttachmentMirror.None, bool isVirtual = false,
        IReadOnlyList<string> memberCodes = null)
        : base(code, box, transform, origin, mirror, isVirtual, memberCodes)
    {
        Categories = categories ?? Array.Empty<string>();
    }

    public CategoryAttachmentPoint(in SlotData slot)
        : this(slot.Code, slot.Categories, slot.Box, slot.Transform, slot.Origin,
            slot.Mirror, slot.Virtual, slot.Slots) { }

    public override bool Accepts(IAttachment attachment)
        => AttachmentCategories.Accepts(Categories, attachment?.Stack?.Collectible);
}
