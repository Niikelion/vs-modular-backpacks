#nullable enable
using System.Collections.Generic;
using ImmersiveModularBackpacks.Attachments;
using Vintagestory.API.MathTools;

namespace ImmersiveBackpacks.points;

/// <summary>A backpack attachment point that accepts occupants by the mod's custom category attribute.</summary>
public class CategoryAttachmentPoint(
    string code,
    string[]? categories,
    Cuboidf box,
    AttachmentTransform? transform = null,
    Vec3f? origin = null,
    AttachmentMirror mirror = AttachmentMirror.None,
    bool isVirtual = false,
    IReadOnlyList<string>? memberCodes = null)
    : AttachmentPointBase(code, box, transform, origin, mirror, isVirtual, memberCodes)
{
    public string[] Categories { get; } = categories ?? [];

    public CategoryAttachmentPoint(in SlotData slot)
        : this(slot.Code, AttachmentCategories.Read(slot.Config["categories"]), slot.Box, slot.Transform,
            slot.Origin, slot.Mirror, slot.Virtual, slot.Slots) { }

    public override bool Accepts(IAttachment attachment)
        => AttachmentCategories.Accepts(Categories, attachment.Stack.Collectible);
}
