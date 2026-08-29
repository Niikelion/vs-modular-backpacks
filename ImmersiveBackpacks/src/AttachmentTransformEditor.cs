using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.Client.NoObf;

namespace ImmersiveBackpacks;

/// <summary>
/// Gives an addon's shared attachment transform a tab in vanilla's <c>/tfedit</c>, so it can be positioned live
/// in-game instead of edit-JSON-rebuild-relaunch.
///
/// Registering in <see cref="GuiDialogTransformEditor.extraTransforms"/> is the whole integration. Vanilla then
/// reads and writes a ModelTransform at the top-level collectible attribute of that name - which, since 1.8.0, is
/// exactly where <see cref="AttachmentTransform.Attached"/> looks. So the editor's own get/set path does the work
/// and this class intercepts nothing.
///
/// That matters beyond tidiness. Before 1.8.0 this stored the value itself and set <c>preventDefault</c>, which
/// meant the number the editor showed lived at a key only this mod knew about: a write-back tool watching
/// <c>onapplytransforms</c> would have saved a top-level <c>immersiveattachment</c> block that nothing reads. With
/// the default path in charge, Apply lands in the item's asset JSON at the same key the game loads from, and the
/// tuning loop actually closes.
///
/// What remains is a listener that does NOT claim the event - it only notices that OUR transform moved, so the
/// composed meshes can be rebuilt while the sliders are dragged.
///
/// Tuning workflow: attach the addon to a *placed* backpack, hold a second one of the same item, then open the
/// editor's <c>Immersive attachment</c> tab. tfedit edits the collectible in the active hotbar slot, and a
/// collectible is shared by every stack of that item - so the attached copy follows the held one. The placed
/// renderer composes its transform per frame, so edits land immediately; the worn bag is re-tesselated per edit.
/// </summary>
public static class AttachmentTransformEditor
{
    /// <summary>
    /// The editor tab name, the JSON attribute, and the <c>/tfedit</c> argument - one string, deliberately.
    /// Vanilla keys the tab's storage off this name, so anything else reintroduces the mismatch this class used
    /// to paper over.
    /// </summary>
    public const string Target = AttachmentTransform.AttachedTransformKey;

    public static void Register(ICoreClientAPI capi)
    {
        GuiDialogTransformEditor.extraTransforms.Add(new TransformConfig
        {
            AttributeName = Target,
            Title = "Immersive attachment"
        });

        capi.Event.RegisterEventBusListener(
            (string eventName, ref EnumHandling handling, IAttribute data) => OnSet(capi, data),
            0.5, "onsettransform");
    }

    // Fires on every slider nudge, for every tab - so check the target and otherwise keep out of the way. No
    // preventDefault: vanilla's setter runs right after this and stores the value where we read it from.
    private static void OnSet(ICoreClientAPI capi, IAttribute data)
    {
        if (data is not TreeAttribute tree || tree.GetString("target") != Target) return;

        // Composed worn/held meshes bake the transform in, so they must be rebuilt; the placed block composes
        // its transform each frame and needs nothing.
        AttachmentTransform.TuningGeneration++;
        capi.World.Player?.Entity?.MarkShapeModified();
    }
}
