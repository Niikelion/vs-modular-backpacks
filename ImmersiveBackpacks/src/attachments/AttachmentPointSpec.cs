using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ImmersiveBackpacks.attachments;

/// <summary>
/// Plain data implementation of <see cref="IAttachmentPoint"/> — a point built from an owner's
/// <c>immersiveBackpack.attachmentPoints</c> config (code, accepted categories, fallback [0,1] hitbox, placed
/// transform). Geometry that comes from the owner shape's <c>slot_&lt;code&gt;</c> marker is read live by the
/// composer; <see cref="Box"/>/<see cref="Rotation"/> here are the fallback for owners without a marker.
/// </summary>
public sealed class AttachmentPointSpec : IAttachmentPoint
{
    private readonly string[] categories;

    public AttachmentPointSpec(string code, string[] categories, Cuboidf box,
        AttachmentTransform placed = null, AttachmentTransform worn = null, float[] rotation = null)
    {
        Code = code;
        this.categories = categories ?? Array.Empty<string>();
        Box = box;
        Placed = placed ?? AttachmentTransform.Identity;
        Worn = worn ?? AttachmentTransform.Identity;
        Rotation = rotation ?? new[] { 0f, 0f, 0f };
    }

    public string Code { get; }
    public IReadOnlyList<string> Categories => categories;
    public Cuboidf Box { get; }
    public float[] Rotation { get; }
    public AttachmentTransform Placed { get; }
    public AttachmentTransform Worn { get; }

    public bool Accepts(ItemStack stack)
    {
        var cat = stack?.Collectible?.Attributes?["immersiveBackpackAttachment"]?["category"]?.AsString();
        return cat != null && Array.IndexOf(categories, cat) >= 0;
    }
}
