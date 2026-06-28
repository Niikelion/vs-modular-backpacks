using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

namespace ImmersiveBackpacks.blocks;

public class BlockImmersiveBackpack : Block
{
    // The body box comes from the standard blocktype selectionBoxes/collisionBoxes (backpack-placed.json);
    // we add the per-slot attachment boxes on top of it for the selection (interaction) boxes.
    public override Cuboidf[] GetSelectionBoxes(IBlockAccessor blockAccessor, BlockPos pos)
    {
        var be = blockAccessor.GetBlockEntity(pos) as BlockEntityImmersiveBackpack;
        if (be == null) return base.GetSelectionBoxes(blockAccessor, pos);

        float angle = be.MeshAngleRad;
        var body = SelectionBoxes ?? [];
        var boxes = new Cuboidf[body.Length + be.AttachmentPoints.Length];
        for (int i = 0; i < body.Length; i++)
            boxes[i] = RotateBoxY(body[i], angle);
        for (int i = 0; i < be.AttachmentPoints.Length; i++)
            boxes[body.Length + i] = RotateBoxY(be.AttachmentPoints[i].Hitbox, angle);
        return boxes;
    }

    // Rotates a hitbox about the block's vertical centre axis to follow the placed orientation, then
    // re-axis-aligns it. Placement snaps to 90deg, so the result is always a proper axis-aligned box.
    // Uses the same pivot (0.5, 0.5) and handedness as the renderer's RotateY so the boxes track the
    // drawn addons.
    private static Cuboidf RotateBoxY(Cuboidf box, float angleRad)
    {
        float cos = GameMath.Cos(angleRad);
        float sin = GameMath.Sin(angleRad);
        float[] cornerX = { box.X1, box.X2, box.X2, box.X1 };
        float[] cornerZ = { box.Z1, box.Z1, box.Z2, box.Z2 };
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

    public override byte[] GetLightHsv(IBlockAccessor blockAccessor, BlockPos pos = null, ItemStack stack = null)
    {
        if (pos != null && blockAccessor.GetBlockEntity(pos) is BlockEntityImmersiveBackpack be)
        {
            var light = be.ComputeLightHsv();
            if (light != null) return light;
        }
        return base.GetLightHsv(blockAccessor, pos, stack);
    }

    public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
    {
        var be = world.BlockAccessor.GetBlockEntity(blockSel.Position) as BlockEntityImmersiveBackpack;
        return be?.OnPlayerRightClick(byPlayer, blockSel) ?? false;
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
        if (be == null) return [];

        int idx = blockSel.SelectionBoxIndex;
        if (idx == 0)
        {
            return
            [
                new WorldInteraction
                {
                    ActionLangCode = "immersivebackpacks:open-cargo",
                    MouseButton = EnumMouseButton.Right
                }
            ];
        }

        int pointIndex = idx - 1;
        if (pointIndex >= be.AttachmentPoints.Length) return [];

        var point = be.AttachmentPoints[pointIndex];
        bool occupied = be.AttachedItems[pointIndex] != null;

        return
        [
            new WorldInteraction
            {
                ActionLangCode = occupied
                    ? "immersivebackpacks:remove-attachment"
                    : "immersivebackpacks:attach-item",
                MouseButton = EnumMouseButton.Right
            }
        ];
    }
}
