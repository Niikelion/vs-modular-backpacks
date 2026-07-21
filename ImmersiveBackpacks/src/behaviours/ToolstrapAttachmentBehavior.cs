using System.Collections.Generic;
using ImmersiveBackpacks.attachments;
using ImmersiveBackpacks.inventory;
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

        private IReadOnlyList<IAttachmentPoint> BuildToolPoints()
        {
            var coll = Stack.Collectible;
            // Same base shape the composer tesselates, so marker codes line up with the rendered mesh.
            var cs = AttachmentMesh.AttachedShapeComposite(coll)
                ?? (coll as Item)?.Shape ?? (coll as Block)?.Shape;
            var markers = AttachmentMesh.ReadSlots(world.Api, cs?.Base?.ToString(), coll.Code.Domain);

            // Shared sizing applied to each tool slot in both render contexts.
            var toolTf = AttachmentTransform.FromJson(coll.Attributes?["immersiveBackpackAttachment"]["toolTransform"]);

            var list = new List<IAttachmentPoint>(markers.Count);
            foreach (var kv in markers)
            {
                var b = kv.Value.Box;
                var box = new Cuboidf(b.X1 / 16f, b.Y1 / 16f, b.Z1 / 16f, b.X2 / 16f, b.Y2 / 16f, b.Z2 / 16f);
                list.Add(new AttachmentPointSpec(kv.Key, null, box, placed: toolTf, worn: toolTf,
                    rotation: kv.Value.Rotation, accepts: s => BackpackSlotLayout.IsToolSlotItem(s?.Collectible)));
            }
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
