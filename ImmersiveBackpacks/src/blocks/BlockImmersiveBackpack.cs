using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace ImmersiveBackpacks.blocks;

public class BlockImmersiveBackpack : Block, ICustomSelectionBoxRender
{
    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);
        // Make sneak-interact beat held-block placement, so attaching an addon (e.g. a lantern) wins over
        // placing it as a block. Without this the engine places first and only attaches on placement failure.
        PlacedPriorityInteract = true;
    }

    // The body box comes from the standard block type selectionBoxes/collisionBoxes (backpack-placed.json);
    // we add the per-slot attachment boxes on top of it for the selection (interaction) boxes.
    public override Cuboidf[] GetSelectionBoxes(IBlockAccessor blockAccessor, BlockPos pos)
    {
        if (blockAccessor.GetBlockEntity(pos) is not BlockEntityImmersiveBackpack be) return base.GetSelectionBoxes(blockAccessor, pos);

        float angle = be.MeshAngleRad;
        var body = SelectionBoxes ?? [];
        var boxes = new Cuboidf[body.Length + be.AttachmentPoints.Length];
        for (int i = 0; i < body.Length; i++)
            boxes[i] = RotateBoxY(body[i], angle);
        for (int i = 0; i < be.AttachmentPoints.Length; i++)
            boxes[body.Length + i] = RotateBoxY(be.AttachmentPoints[i].Hitbox, angle);
        return boxes;
    }

    // Rotates a hitbox about the block's vertical center axis to follow the placed orientation, then
    // re-axis-aligns it. Placement snaps to 90deg, so the result is always a proper axis-aligned box.
    // Uses the same pivot (0.5, 0.5) and handedness as the renderer's RotateY so the boxes track the
    // drawn addons.
    private static Cuboidf RotateBoxY(Cuboidf box, float angleRad)
    {
        float cos = GameMath.Cos(angleRad);
        float sin = GameMath.Sin(angleRad);
        float[] cornerX = [box.X1, box.X2, box.X2, box.X1];
        float[] cornerZ = [box.Z1, box.Z1, box.Z2, box.Z2];
        float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;
        for (int i = 0; i < 4; i++)
        {
            float dx = cornerX[i] - 0.5f, dz = cornerZ[i] - 0.5f;
            float rx = dx * cos + dz * sin + 0.5f;
            float rz = -dx * sin + dz * cos + 0.5f;
            minX = GameMath.Min(minX, rx); maxX = GameMath.Max(maxX, rx);
            minZ = GameMath.Min(minZ, rz); maxZ = GameMath.Max(maxZ, rz);
        }
        return new Cuboidf(minX, box.Y1, minZ, maxX, box.Y2, maxZ);
    }

    public override Cuboidf[] GetCollisionBoxes(IBlockAccessor blockAccessor, BlockPos pos)
    {
        var be = blockAccessor.GetBlockEntity(pos) as BlockEntityImmersiveBackpack;
        float angle = be?.MeshAngleRad ?? 0f;
        var src = CollisionBoxes ?? [];
        var result = new Cuboidf[src.Length];
        for (int i = 0; i < src.Length; i++)
            result[i] = RotateBoxY(src[i], angle);
        return result;
    }

    // Vanilla's SystemSelectedBlockOutline draws EVERY selection box the block returns, so all attachment
    // slot boxes light up at once. Take over the render: always outline the backpack body box(es), plus only
    // the single attachment-slot box the player is currently pointing at. Selection/raycasting is unchanged
    // (still driven by GetSelectionBoxes); this only governs what's drawn.
    public void RenderSelectionBoxes(BlockSelection blockSel, RenderBoxDelegate renderBoxHandler)
    {
        var boxes = GetSelectionBoxes(api.World.BlockAccessor, blockSel.Position);
        if (boxes == null || boxes.Length == 0) return;

        var capi = api as ICoreClientAPI;
        float thickness = capi != null && capi.Settings.Float.Exists("wireframethickness")
            ? capi.Settings.Float["wireframethickness"] : 1.6f;
        float width = 1.6f * thickness;
        var color = GetSelectionColor(capi, blockSel.Position);

        // Body boxes come first (SelectionBoxes), then one box per attachment point (see GetSelectionBoxes).
        int bodyCount = SelectionBoxes?.Length ?? 0;
        for (int i = 0; i < bodyCount && i < boxes.Length; i++)
            renderBoxHandler(boxes[i], width, color);

        int idx = blockSel.SelectionBoxIndex;
        if (idx >= bodyCount && idx < boxes.Length)
            renderBoxHandler(boxes[idx], width, SlotColor(capi, blockSel.Position, idx - bodyCount) ?? color);
    }

    // Muted, and close to the default outline's alpha: a tint on the wireframe, not a highlight.
    private static readonly Vec4f acceptsColor = new(0.3f, 0.55f, 0.3f, 0.55f);
    private static readonly Vec4f rejectsColor = new(0.6f, 0.28f, 0.28f, 0.55f);

    // Answers "would shift+right-click attach what I'm holding here?" for the slot box under the crosshair:
    // green yes, red no (wrong category, or the point is taken - that click detaches instead). Null with an
    // empty hand, where there is nothing to judge, leaving the box its default outline.
    private static Vec4f SlotColor(ICoreClientAPI capi, BlockPos pos, int pointIndex)
    {
        var held = capi?.World?.Player?.InventoryManager?.ActiveHotbarSlot?.Itemstack;
        if (held == null) return null;
        if (capi.World.BlockAccessor.GetBlockEntity(pos) is not BlockEntityImmersiveBackpack be) return null;
        return be.CanAccept(pointIndex, held) ? acceptsColor : rejectsColor;
    }

    public override byte[] GetLightHsv(IBlockAccessor blockAccessor, BlockPos pos, ItemStack stack = null)
    {
        if (pos == null || blockAccessor.GetBlockEntity(pos) is not BlockEntityImmersiveBackpack be)
            return base.GetLightHsv(blockAccessor, pos, stack);
        byte[] light = be.ComputeLightHsv();
        return light ?? base.GetLightHsv(blockAccessor, pos, stack);
    }

    public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
    {
        var be = world.BlockAccessor.GetBlockEntity(blockSel.Position) as BlockEntityImmersiveBackpack;
        return be?.OnPlayerRightClick(byPlayer, blockSel) ?? false;
    }

    // Middle-click pick must return the worn bag (with its addons + cargo), not a bare placed-block stack.
    public override ItemStack OnPickBlock(IWorldAccessor world, BlockPos pos)
    {
        var be = world.BlockAccessor.GetBlockEntity(pos) as BlockEntityImmersiveBackpack;
        return be?.CreateDropItemStack(world) ?? base.OnPickBlock(world, pos);
    }

    public override ItemStack[] GetDrops(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1f)
    {
        var be = world.BlockAccessor.GetBlockEntity(pos) as BlockEntityImmersiveBackpack;
        var drop = be?.CreateDropItemStack(world);
        return drop != null ? [drop] : [];
    }

    public override void OnBlockBroken(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1f)
    {
        if (world.Side == EnumAppSide.Server)
        {
            var drops = GetDrops(world, pos, byPlayer, dropQuantityMultiplier);
            foreach (var drop in drops)
                world.SpawnItemEntity(drop, pos.ToVec3d().Add(0.5, 0.5, 0.5));
        }

        world.BlockAccessor.SetBlock(0, pos);
    }

    public override WorldInteraction[] GetPlacedBlockInteractionHelp(IWorldAccessor world, BlockSelection blockSel, IPlayer forPlayer)
    {
        var be = world.BlockAccessor.GetBlockEntity(blockSel.Position) as BlockEntityImmersiveBackpack;

        // Plain right-click picks the pack up; ctrl+right-click opens the cargo dialog.
        var interactions = new List<WorldInteraction>
        {
            new() { ActionLangCode = "immersivemodularbackpacks:pick-up", MouseButton = EnumMouseButton.Right },
            new() { ActionLangCode = "immersivemodularbackpacks:open-cargo", MouseButton = EnumMouseButton.Right, HotKeyCode = "ctrl" }
        };

        // On an attachment-point box (indices 1+; box 0 is the bag body), shift+right-click attaches/detaches.
        int pointIndex = blockSel.SelectionBoxIndex - 1;
        if (be != null && pointIndex >= 0 && pointIndex < be.AttachmentPoints.Length)
        {
            var point = be.AttachmentPoints[pointIndex];
            bool occupied = be.AttachedItems[pointIndex] != null;
            interactions.Add(new()
            {
                ActionLangCode = occupied
                    ? "immersivemodularbackpacks:remove-attachment"
                    : "immersivemodularbackpacks:attach-item",
                MouseButton = EnumMouseButton.Right,
                HotKeyCode = "shift",
                // Empty point: cycle through every addon that can attach here so the options are discoverable.
                Itemstacks = occupied ? null : AttachableStacks(point.Categories)
            });
        }

        return interactions.ToArray().Append(base.GetPlacedBlockInteractionHelp(world, blockSel, forPlayer));
    }

    // Every addon stack whose declared category is accepted by the point, for the interaction-help cycle.
    // Built once from the collectible registry and cached (the attachable set is fixed after load).
    private ItemStack[] AttachableStacks(string[] categories)
    {
        if (categories == null || categories.Length == 0) return null;

        var byCategory = ObjectCacheUtil.GetOrCreate(api, "immersivemodularbackpacks:attachablesByCategory", () =>
        {
            var map = new Dictionary<string, List<ItemStack>>();
            foreach (var obj in api.World.Collectibles)
            {
                var cat = obj.Attributes?["immersiveBackpackAttachment"]?["category"]?.AsString();
                if (cat == null) continue;
                var stacks = RepresentativeStacks(obj);
                if (stacks == null) continue;
                if (!map.TryGetValue(cat, out var list)) map[cat] = list = new();
                list.AddRange(stacks);
            }
            return map;
        });

        var stacks = new List<ItemStack>();
        foreach (var cat in categories)
            if (byCategory.TryGetValue(cat, out var list)) stacks.AddRange(list);
        return stacks.Count > 0 ? stacks.ToArray() : null;
    }

    // Displayable ghost stacks for the interaction-help cycle. Both creative declaration forms have to be
    // handled: "creativeinventoryStacks" spells out the stacks (the lantern, whose stacks carry the metal
    // attribute its shape needs - without it the shape asks for a "#deco-" texture that doesn't exist and
    // tesselation errors), while the plain "creativeinventory" tab list (everything else) declares no stacks
    // at all and just means "the collectible itself". Null for collectibles in neither form drops
    // placement-only block orientations (wall/ceiling lanterns) a player never holds.
    private static List<ItemStack> RepresentativeStacks(CollectibleObject obj)
    {
        if (obj.CreativeInventoryStacks != null)
        {
            var stacks = new List<ItemStack>();
            foreach (var entry in obj.CreativeInventoryStacks)
                foreach (var js in entry.Stacks)
                    if (js.ResolvedItemstack != null)
                    {
                        var stack = js.ResolvedItemstack.Clone();
                        // A declared stack may carry a quantity; the help icon would draw a badge for it.
                        stack.StackSize = 1;
                        stacks.Add(stack);
                    }
            return stacks.Count > 0 ? stacks : null;
        }

        return obj.CreativeInventoryTabs?.Length > 0 ? [new ItemStack(obj)] : null;
    }
}
