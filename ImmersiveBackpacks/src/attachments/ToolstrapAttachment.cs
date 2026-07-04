using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ImmersiveBackpacks.attachments;

/// <summary>
/// A toolstrap: a container attachment whose points are the <c>slot_tool_&lt;n&gt;</c> markers authored in its
/// own strap shape, and whose children are the tools placed in the backpack's cargo slots that this toolstrap
/// contributes. Those tools are NOT stored in the toolstrap stack — they live in the container's unified cargo
/// (the placed block's inventory, or the worn/held bag's <c>backpack.slots</c>) — so the host resolves the
/// owning slot range and supplies the ordered tool stacks at construction. Cargo slot <c>i</c> in that range
/// renders at marker <c>slot_tool_i</c>. Composition into the strap mesh/shape is the same recursion the bag
/// uses for its addons (this is a bag one level down); the composer needs no toolstrap-specific code.
///
/// Points are read live from the shape, so the tool layout follows the markers authored in Blockbench.
/// See [[straps-tools-plan]].
/// </summary>
public sealed class ToolstrapAttachment : AttachmentBase
{
    private readonly IReadOnlyList<ItemStack> tools;
    private readonly IWorldAccessor world;
    private IReadOnlyList<IAttachmentPoint> points;

    public ToolstrapAttachment(ItemStack stack, IReadOnlyList<ItemStack> tools, IWorldAccessor world)
        : base(stack)
    {
        this.tools = tools ?? Array.Empty<ItemStack>();
        this.world = world;
    }

    public override IReadOnlyList<IAttachmentPoint> Points => points ??= BuildToolPoints();

    public override IAttachment GetAttached(string pointCode)
    {
        int i = ParseIndex(pointCode);
        if (i < 0 || i >= tools.Count) return null;
        var s = tools[i];
        if (s == null) return null;
        s.ResolveBlockOrItem(world);
        return AttachmentFactory.For(s, world);
    }

    private IReadOnlyList<IAttachmentPoint> BuildToolPoints()
    {
        var coll = Stack.Collectible;
        // Same base shape the composer tesselates for this node, so marker codes line up with the rendered mesh.
        var cs = AttachmentMesh.AttachedShapeComposite(coll)
            ?? (coll as Item)?.Shape ?? (coll as Block)?.Shape;
        var markers = AttachmentMesh.ReadSlots(world.Api, cs?.Base?.ToString(), coll.Code.Domain);

        var list = new List<IAttachmentPoint>(markers.Count);
        foreach (var kv in markers)
        {
            var b = kv.Value.Box;
            var box = new Cuboidf(b.X1 / 16f, b.Y1 / 16f, b.Z1 / 16f, b.X2 / 16f, b.Y2 / 16f, b.Z2 / 16f);
            // Tools carry no attachment category; a tool point accepts anything with a tool tier (matches the
            // "Tools" cargo filter). Only consulted by interaction, which for tools is the cargo dialog anyway.
            list.Add(new AttachmentPointSpec(kv.Key, null, box, rotation: kv.Value.Rotation,
                accepts: s => s?.Collectible?.ToolTier > 0));
        }
        return list;
    }

    // "tool_3" -> 3. Marker "slot_tool_3" becomes code "tool_3" after ReadSlots strips the "slot_" prefix.
    private static int ParseIndex(string code)
    {
        int u = code.LastIndexOf('_');
        return u >= 0 && int.TryParse(code[(u + 1)..], out var n) ? n : -1;
    }
}
