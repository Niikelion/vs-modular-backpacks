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
        api.RegisterCollectibleBehaviorClass("ImmersiveToolstrapAttachment", typeof(ToolstrapAttachmentBehavior));
        api.RegisterEntityBehaviorClass("immersivebackpacklight", typeof(EntityBehaviorBackpackLight));
    }

    public override void StartServerSide(ICoreServerAPI api) { }

    public override void StartClientSide(ICoreClientAPI api)
    {
        AttachmentTransformEditor.Register(api);
#if VSMCP_BRIDGE
        // Client-side only on purpose: single-player runs this ModSystem twice, and the bridge's action
        // registry is static - registering from one side keeps it a single registration.
        compat.BridgeActions.TryRegister(api);
#endif
    }
}
