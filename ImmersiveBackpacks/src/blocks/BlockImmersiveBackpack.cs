using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

namespace ImmersiveBackpacks.blocks;

public class BlockImmersiveBackpack : Block
{
    private static readonly Cuboidf BodyBox = new(0.25f, 0f, 0.25f, 0.75f, 0.75f, 0.75f);

    public override Cuboidf[] GetSelectionBoxes(IBlockAccessor blockAccessor, BlockPos pos)
    {
        var be = blockAccessor.GetBlockEntity(pos) as BlockEntityImmersiveBackpack;
        if (be == null || be.AttachmentPoints.Length == 0)
            return [BodyBox];

        var boxes = new Cuboidf[1 + be.AttachmentPoints.Length];
        boxes[0] = BodyBox;
        for (int i = 0; i < be.AttachmentPoints.Length; i++)
            boxes[i + 1] = be.AttachmentPoints[i].Hitbox;
        return boxes;
    }

    public override Cuboidf[] GetCollisionBoxes(IBlockAccessor blockAccessor, BlockPos pos)
        => [BodyBox];

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
