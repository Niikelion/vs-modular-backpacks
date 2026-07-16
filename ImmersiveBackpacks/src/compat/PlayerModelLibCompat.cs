using ImmersiveBackpacks.attachments;
using ImmersiveBackpacks.items;
using PlayerModelLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace ImmersiveBackpacks.compat;

/// <summary>
/// Integrates our runtime-composed worn bag (base + attached addons) with PlayerModelLib's custom-model gear
/// pipeline.
///
/// On a custom player model PML does NOT render our <see cref="IWearableShapeSupplier.GetShape"/> result: its
/// <c>WearablesTesselatorBehavior.BeforeWearableShapeAttached</c> handler
/// (<c>ShapeReplacementUtil.ReplaceWearableShape</c>) rebuilds the worn gear from the base attachedShape (via
/// <c>GetAttachedShape</c>) and overwrites the shape we returned - so addons vanish. On the vanilla seraph it
/// early-returns, which is why GetShape is honoured there.
///
/// We subscribe AFTER PML (ExecuteOrder 0.22 &gt; PML's 0.21, so our += lands later and the multicast invokes us
/// last) and, on custom models only, re-graft our addons onto the model-adjusted base shape PML just produced.
/// PML's subsequent StepParentShape + texture registration then pick them up. The markers we compose onto
/// (slot_*) survive PML's rescale, so addons land correctly proportioned to the model.
///
/// Soft dependency: <see cref="ShouldLoad"/> gates on <c>playermodellib</c> being enabled, so when it is absent
/// this ModSystem is never instantiated and the JIT never resolves any PlayerModelLib type - the mod runs fine
/// standalone despite the compile-time reference (which is not shipped).
/// </summary>
public class PlayerModelLibCompat : ModSystem
{
    public override bool ShouldLoad(ICoreAPI api)
        => api.Side == EnumAppSide.Client && api.ModLoader.IsModEnabled("playermodellib");

    // PML's CustomModelsSystem is 0.21 and subscribes its shape-replacer in StartClientSide; a higher order
    // makes our subscription run after it, so we compose onto the shape it produced rather than the reverse.
    public override double ExecuteOrder() => 0.22;

    public override void StartClientSide(ICoreClientAPI api)
        => WearablesTesselatorBehavior.BeforeWearableShapeAttached += OnBeforeWearableShapeAttached;

    public override void Dispose()
        => WearablesTesselatorBehavior.BeforeWearableShapeAttached -= OnBeforeWearableShapeAttached;

    private void OnBeforeWearableShapeAttached(WearablesTesselatorBehavior beh, IInventory inventory, ItemSlot slot,
        ref Shape entityShape, ref string[] willDeleteElements, ref Shape attachableShape,
        ref CompositeShape attachableCompositeShape)
    {
        if (attachableShape == null) return;
        if (slot.Itemstack?.Collectible is not ItemImmersiveBag bag) return;

        // Seraph path: GetShape already composed the addons into attachableShape; composing again would
        // duplicate them. PML only replaces our shape (dropping addons) on custom models.
        var skin = beh.PlayerEntity.GetBehavior<PlayerSkinBehavior>();
        if (skin == null || skin.CurrentModelCode == CustomModelsSystem.SeraphModelCode) return;

        AttachmentComposer.ComposeChildrenInto(beh.PlayerEntity.World.Api, attachableShape, bag.BagNodeFor(slot.Itemstack));
    }
}
