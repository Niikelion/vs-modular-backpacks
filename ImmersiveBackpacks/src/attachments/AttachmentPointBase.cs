#nullable enable
using System.Collections.Generic;
using Vintagestory.API.MathTools;

namespace ImmersiveModularBackpacks.Attachments;

/// <summary>
/// Base <see cref="IAttachmentPoint"/> carrying the shared geometry (code, box, occupant transform, anchor);
/// subclasses decide acceptance. Geometry that comes from the owner shape's <c>slot_&lt;code&gt;</c> marker is
/// read live by the composer; <see cref="Box"/> here is the fallback for owners without a marker.
/// </summary>
public abstract class AttachmentPointBase(
    string code,
    Cuboidf box,
    AttachmentTransform? transform = null,
    Vec3f? origin = null,
    AttachmentMirror mirror = AttachmentMirror.None,
    bool isVirtual = false,
    IReadOnlyList<string>? memberCodes = null)
    : IAttachmentPoint
{
    public string Code { get; } = code;
    public bool IsVirtual { get; } = isVirtual;
    public IReadOnlyList<string> MemberCodes { get; } = memberCodes ?? [];
    public Cuboidf Box { get; } = box;
    public AttachmentTransform Transform { get; } = transform ?? AttachmentTransform.Identity;
    public Vec3f Origin { get; } = origin ?? box.Center.ToVec3f();
    public AttachmentMirror Mirror { get; } = mirror;

    public abstract bool Accepts(IAttachment attachment);

    public virtual void OnAttached(IAttachment child, IAttachmentHost host) { }

    public virtual void OnDetached() { }

}
