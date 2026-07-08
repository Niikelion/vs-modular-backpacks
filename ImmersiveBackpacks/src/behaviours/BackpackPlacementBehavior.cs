using ImmersiveBackpacks.blocks;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ImmersiveBackpacks.behaviours;

public class BackpackPlacementBehavior(CollectibleObject collObj) : CollectibleBehavior(collObj)
{
    public override void OnHeldInteractStart(
        ItemSlot itemslot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel,
        bool firstEvent, ref EnumHandHandling handHandling, ref EnumHandling handling)
    {
        if (blockSel == null || !byEntity.Controls.ShiftKey) return;
        if (blockSel.Face != BlockFacing.UP) return;

        // Take over placement fully: PreventSubsequent also stops the vanilla GroundStorable behavior
        // from running (PreventDefault would only block the default action, letting GroundStorable still
        // fire and the client briefly predict a vanilla ground backpack - the placement "flash").
        handling = EnumHandling.PreventSubsequent;
        handHandling = EnumHandHandling.PreventDefault;

        IWorldAccessor world = byEntity.World;
        IPlayer byPlayer = (byEntity as EntityPlayer)?.Player;
        if (byPlayer == null) return;

        if (!world.Claims.TryAccess(byPlayer, blockSel.Position, EnumBlockAccessFlags.BuildOrBreak))
        {
            itemslot.MarkDirty();
            return;
        }

        Block onBlock = world.BlockAccessor.GetBlock(blockSel.Position);
        // The placed block is a variant block matching the bag's type, so each variant carries its own
        // vanilla selection/collision box.
        var variant = itemslot.Itemstack.Collectible.Variant;
        string type = variant != null && variant.TryGetValue("type", out var t) ? t : "normal";
        Block placedBlock = world.GetBlock(new AssetLocation("immersivemodularbackpacks:backpack-placed-" + type));
        if (placedBlock == null) return;
        if (!onBlock.CanAttachBlockAt(world.BlockAccessor, placedBlock, blockSel.Position, BlockFacing.UP)) return;

        BlockPos placePos = blockSel.Position.UpCopy();
        if (world.BlockAccessor.GetBlock(placePos).Replaceable < 6000) return;

        if (world.Side != EnumAppSide.Server) return;

        world.BlockAccessor.SetBlock(placedBlock.BlockId, placePos);

        if (world.BlockAccessor.GetBlockEntity(placePos) is BlockEntityImmersiveBackpack be)
        {
            // Set the placement orientation before InitFromItemStack so the first sync carries it.
            // Flip 180° to face the player and snap to 90° increments (so attachment hitboxes stay
            // axis-aligned when rotated to match the placement).
            const float step = GameMath.PI / 2f;
            float yaw = byEntity.Pos.Yaw + GameMath.PI;
            be.MeshAngleRad = (float)System.Math.Round(yaw / step) * step;
            be.InitFromItemStack(itemslot.Itemstack);
        }

        world.BlockAccessor.TriggerNeighbourBlockUpdate(placePos);

        if (!byPlayer.WorldData.CurrentGameMode.HasFlag(EnumGameMode.Creative))
        {
            itemslot.TakeOut(1);
            itemslot.MarkDirty();
        }

        world.PlaySoundAt(
            new AssetLocation("sounds/player/buildhigh"),
            placePos.X + 0.5, placePos.Y, placePos.Z + 0.5,
            byPlayer, true, 16f);
    }
}
