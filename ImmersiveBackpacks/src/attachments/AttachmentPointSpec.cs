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
    private readonly System.Func<ItemStack, bool> accepts;

    public AttachmentPointSpec(string code, string[] categories, Cuboidf box,
        AttachmentTransform placed = null, AttachmentTransform worn = null, float[] rotation = null,
        System.Func<ItemStack, bool> accepts = null)
    {
        Code = code;
        this.categories = categories ?? Array.Empty<string>();
        this.accepts = accepts;
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
        // A custom predicate (e.g. tool points accepting anything with a tool tier) overrides the default
        // category match, since not every attachable declares an immersiveBackpackAttachment.category.
        if (accepts != null) return accepts(stack);
        var cat = stack?.Collectible?.Attributes?["immersiveBackpackAttachment"]?["category"]?.AsString();
        return cat != null && Array.IndexOf(categories, cat) >= 0;
    }
}
