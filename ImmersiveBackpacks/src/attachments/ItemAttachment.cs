using System;
using System.Collections.Generic;
using Vintagestory.API.Common;

namespace ImmersiveBackpacks.attachments;

/// <summary>
/// The simplest node: a bare stack that renders and hosts nothing — a tool on a strap, an item in a structure
/// slot, a sack on a bag. It's the default child type the composer wraps occupants in, so most attachments
/// need no bespoke class at all; a container only exists to declare tool/module points, and its occupants can
/// be these. Renders as its own (attached or display) shape via the base class.
/// </summary>
public sealed class ItemAttachment : AttachmentBase
{
    public ItemAttachment(ItemStack stack) : base(stack) { }

    public override IReadOnlyList<IAttachmentPoint> Points => Array.Empty<IAttachmentPoint>();

    public override IAttachment GetAttached(string pointCode) => null;
}
