using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ImmersiveModularBackpacks.Attachments;

/// <summary>
/// Base <see cref="IAttachmentPoint"/> carrying the shared geometry (code, box, occupant transform, anchor);
/// subclasses decide acceptance. Geometry that comes from the owner shape's <c>slot_&lt;code&gt;</c> marker is
/// read live by the composer; <see cref="Box"/> here is the fallback for owners without a marker.
/// </summary>
public abstract class AttachmentPointBase : IAttachmentPoint
{
    protected AttachmentPointBase(string code, Cuboidf box,
        AttachmentTransform transform = null, Vec3f origin = null,
        AttachmentMirror mirror = AttachmentMirror.None, bool isVirtual = false,
        IReadOnlyList<string> memberCodes = null)
    {
        Code = code;
        Box = box;
        Transform = transform ?? AttachmentTransform.Identity;
        Origin = origin ?? BoxCentre(box);
        Mirror = mirror;
        IsVirtual = isVirtual;
        MemberCodes = memberCodes ?? Array.Empty<string>();
    }

    public string Code { get; }
    public bool IsVirtual { get; }
    public IReadOnlyList<string> MemberCodes { get; }
    public Cuboidf Box { get; }
    public AttachmentTransform Transform { get; }
    public Vec3f Origin { get; }
    public AttachmentMirror Mirror { get; }

    public abstract bool Accepts(IAttachment attachment);

    public virtual void OnAttached(IAttachment child, IAttachmentHost host) { }

    public virtual void OnDetached() { }

    private static Vec3f BoxCentre(Cuboidf b)
        => b == null ? new Vec3f() : new Vec3f((b.X1 + b.X2) / 2f, (b.Y1 + b.Y2) / 2f, (b.Z1 + b.Z2) / 2f);
}
