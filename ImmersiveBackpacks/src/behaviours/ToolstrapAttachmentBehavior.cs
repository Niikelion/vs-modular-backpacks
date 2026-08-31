using System.Collections.Generic;
using ImmersiveModularBackpacks.Attachments;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ImmersiveBackpacks.behaviours;

/// <summary>
/// Marks a collectible as a toolstrap: builds a container node whose tool points are the <c>slot_tool_&lt;n&gt;</c>
/// markers in its strap shape and whose children are the host cargo it owns (the tools live in the bag's cargo,
/// not the strap stack). Registered via JSON <c>behaviors</c>, so the factory needs no toolstrap knowledge.
/// </summary>
public class ToolstrapAttachmentBehavior(CollectibleObject collObj) : CollectibleBehavior(collObj), IAttachmentBuilder
{
    public IAttachment Build(ItemStack stack, IWorldAccessor world, IReadOnlyList<ItemStack> ownedCargo = null)
        => new ToolstrapAttachment(stack, ownedCargo, world);

    private sealed class ToolstrapAttachment(ItemStack stack, IReadOnlyList<ItemStack> tools, IWorldAccessor world)
        : AttachmentBase(stack)
    {
        private readonly IReadOnlyList<ItemStack> tools = tools ?? [];

        public override IReadOnlyList<IAttachmentPoint> Points => field ??= BuildToolPoints();

        public override IAttachment GetAttached(string pointCode)
        {
            int i = ParseIndex(pointCode);
            if (i < 0 || i >= tools.Count) return null;
            var s = tools[i];
            if (s == null) return null;
            s.ResolveBlockOrItem(world);
            return AttachmentFactory.For(s, world);
        }

        // Points come from the strap's own immersiveBackpack.attachmentPoints, the same config a backpack
        // declares: each entry names a point and the categories it accepts. Geometry still comes from the
        // shape's slot_<code> marker, as it does for a bag - the JSON says what fits, the shape says where.
        private IReadOnlyList<IAttachmentPoint> BuildToolPoints()
        {
            var coll = Stack.Collectible;
            var declared = coll.Attributes?["immersiveBackpack"]["attachmentPoints"];
            if (declared is not { Exists: true }) return [];

            // Shared sizing applied to each tool slot in both render contexts.
            var toolTf = AttachmentTransform.FromJson(coll.Attributes?["immersiveBackpackAttachment"]["toolTransform"]);

            var list = new List<IAttachmentPoint>();
            foreach (var slot in SlotDataLoader.Load(world.Api, coll, declared,
                         additionalTransform: toolTf))
                list.Add(new CategoryAttachmentPoint(slot));
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
