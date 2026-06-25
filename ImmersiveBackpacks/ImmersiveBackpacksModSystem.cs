using ImmersiveBackpacks.behaviours;
using ImmersiveBackpacks.blocks;
using ImmersiveBackpacks.items;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace ImmersiveBackpacks;

public class ImmersiveBackpacksModSystem : ModSystem
{
    public override void Start(ICoreAPI api)
    {
        api.RegisterItemClass("ImmersiveBag", typeof(ItemImmersiveBag));
        api.RegisterBlockClass("ImmersiveBackpack", typeof(BlockImmersiveBackpack));
        api.RegisterBlockEntityClass("ImmersiveBackpackBE", typeof(BlockEntityImmersiveBackpack));
        api.RegisterCollectibleBehaviorClass("ImmersiveBackpackPlacement", typeof(BackpackPlacementBehavior));
        api.RegisterEntityBehaviorClass("immersivebackpacklight", typeof(EntityBehaviorBackpackLight));
    }

    public override void StartServerSide(ICoreServerAPI api) { }

    public override void StartClientSide(ICoreClientAPI api) { }
}
