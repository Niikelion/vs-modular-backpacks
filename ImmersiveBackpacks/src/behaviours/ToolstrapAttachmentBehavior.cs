#nullable enable
using System.Collections.Generic;
using ImmersiveModularBackpacks.Attachments;
using ImmersiveBackpacks.points;
using Vintagestory.API.Common;

namespace ImmersiveBackpacks.behaviours;

/// <summary>
/// Marks a collectible as a toolstrap: builds a container node whose tool points are the <c>slot_tool_&lt;n&gt;</c>
/// markers in its strap shape and whose children are read through its <c>IHeldBag</c> state. Backpack hosts
/// hydrate that state on a transient stack before construction.
/// </summary>
public class ToolstrapAttachmentBehavior(CollectibleObject collObj) : CollectibleBehavior(collObj), IAttachmentBuilder
{
    public IAttachment Build(ItemStack stack, IWorldAccessor world)
        => new ToolstrapAttachment(stack, world);

    private sealed class ToolstrapAttachment(ItemStack stack, IWorldAccessor world)
        : AttachmentBase(stack), IAttachmentPointContextReceiver
    {
        private readonly IReadOnlyList<ItemStack?> tools = ReadTools(stack, world);
        private IReadOnlyList<string> parentPointTags = [];
        private IReadOnlyList<IAttachmentPoint>? toolPoints;

        public override IReadOnlyList<IAttachmentPoint> Points => toolPoints ??= BuildToolPoints();

        public void SetAttachmentPointContext(IAttachmentPoint point)
        {
            parentPointTags = point is ITaggedAttachmentPoint tagged ? tagged.Tags : [];
            toolPoints = null;
        }

        protected override void AppendOwnRenderState(ref AttachmentRenderKeyBuilder key)
        {
            base.AppendOwnRenderState(ref key);
            foreach (string tag in parentPointTags) key.Add(tag);
        }

        public override IAttachment? GetAttached(string pointCode)
        {
            int i = ParseIndex(pointCode);
            if (i < 0 || i >= tools.Count) return null;
            var s = tools[i];
            if (s == null) return null;
            s.ResolveBlockOrItem(world);
            return AttachmentFactory.For(s, world);
        }

        private static IReadOnlyList<ItemStack?> ReadTools(ItemStack source, IWorldAccessor sourceWorld)
            => source.Collectible?.GetCollectibleInterface<IHeldBag>()?.GetContents(source, sourceWorld) ?? [];

        // Points come from the strap's own immersiveBackpack.attachmentPoints, the same config a backpack
        // declares: each entry names a point and the categories it accepts. Geometry still comes from the
        // shape's slot_<code> marker, as it does for a bag - the JSON says what fits, the shape says where.
        private IReadOnlyList<IAttachmentPoint> BuildToolPoints()
        {
            var coll = Stack.Collectible;
            var declared = coll.Attributes?["immersiveBackpack"]["attachmentPoints"];
            if (declared is not { Exists: true }) return [];

            // Shared sizing applied to each tool slot in both render contexts.
            var toolTf = AttachmentTransform.FromModelTransform(
                coll.Attributes?["immersiveBackpackAttachment"]["toolTransform"]);
            var transformsByTag = coll.Attributes?["immersiveBackpackAttachment"]["toolTransformByPointTag"];
            foreach (string tag in parentPointTags)
                toolTf = toolTf.CombinedWith(
                    AttachmentTransform.FromModelTransform(transformsByTag?[tag]));

            var list = new List<IAttachmentPoint>();
            foreach (var slot in SlotDataLoader.Load(world.Api, coll, declared,
                         additionalTransform: toolTf))
                list.Add(new ToolStorageAttachmentPoint(slot));
            return list;
        }

        // "slot_tool_3" -> 3 (ReadSlots already stripped "slot_").
        private static int ParseIndex(string code)
        {
            int u = code.LastIndexOf('_');
            return u >= 0 && int.TryParse(code[(u + 1)..], out var n) ? n : -1;
        }
    }

}
