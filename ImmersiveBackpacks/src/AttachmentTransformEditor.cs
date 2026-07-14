using Newtonsoft.Json.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;

namespace ImmersiveBackpacks;

/// <summary>
/// Bridges vanilla's <c>/tfedit</c> onto an addon's <c>immersiveBackpackAttachment.attachedTransform</c>, so an
/// attachment can be positioned live in-game instead of edit-JSON-rebuild-relaunch.
///
/// Registering in <see cref="GuiDialogTransformEditor.extraTransforms"/> is what adds the
/// <c>/tfedit immersiveattachment</c> argument and its editor tab. Vanilla would then read and write a
/// ModelTransform at a *top-level* collectible attribute of that name - which is not where our system looks - so
/// the editor's get/set events are intercepted (preventDefault) and mapped onto our nested key instead. The
/// numbers the editor shows are therefore exactly what belongs in the item's asset JSON.
///
/// Tuning workflow: attach the addon to a *placed* backpack, hold a second one of the same item, then
/// <c>/tfedit immersiveattachment</c>. tfedit edits the collectible in the active hotbar slot, and a collectible
/// is shared by every stack of that item - so the attached copy follows the held one. The placed renderer
/// composes its transform per frame, so edits land immediately; the worn bag is re-tesselated per edit.
///
/// Client-only, and a dev tool: it mutates in-memory collectible attributes, so copy the values into the asset
/// when done. Nothing is persisted.
/// </summary>
public static class AttachmentTransformEditor
{
    /// <summary>The /tfedit argument and editor tab name.</summary>
    public const string Target = "immersiveattachment";

    public static void Register(ICoreClientAPI capi)
    {
        GuiDialogTransformEditor.extraTransforms.Add(new TransformConfig
        {
            AttributeName = Target,
            Title = "Immersive attachment"
        });

        capi.Event.RegisterEventBusListener(
            (string eventName, ref EnumHandling handling, IAttribute data) => OnGet(capi, data),
            0.5, "ongettransform");
        capi.Event.RegisterEventBusListener(
            (string eventName, ref EnumHandling handling, IAttribute data) => OnSet(capi, data),
            0.5, "onsettransform");
    }

    // The editor is opening our tab: hand it the addon's current attachedTransform, so tuning starts from
    // whatever the asset declares rather than from an identity transform.
    private static void OnGet(ICoreClientAPI capi, IAttribute data)
    {
        if (data is not TreeAttribute tree || tree.GetString("target") != Target) return;

        var coll = HeldCollectible(capi);
        if (coll == null) return;

        ToModelTransform(AttachmentTransform.FromItem(coll, "attachedTransform")).ToTreeAttribute(tree);
        tree.SetBool("preventDefault", true);
    }

    // Fires on every slider nudge. Write our own nested key (vanilla would write a top-level one nothing reads),
    // then invalidate so the change is visible on the bag right away.
    private static void OnSet(ICoreClientAPI capi, IAttribute data)
    {
        if (data is not TreeAttribute tree || tree.GetString("target") != Target) return;

        var coll = HeldCollectible(capi);
        if (coll == null) return;

        WriteAttachedTransform(coll, FromModelTransform(ModelTransform.CreateFromTreeAttribute(tree)));
        tree.SetBool("preventDefault", true);

        // Composed worn/held meshes bake the transform in, so they must be rebuilt; the placed block composes
        // its transform each frame and needs nothing.
        AttachmentTransform.TuningGeneration++;
        capi.World.Player?.Entity?.MarkShapeModified();
    }

    /// <summary>The collectible tfedit is editing: the one in the active hotbar slot.</summary>
    private static CollectibleObject HeldCollectible(ICoreClientAPI capi)
        => capi.World?.Player?.InventoryManager?.ActiveHotbarSlot?.Itemstack?.Collectible;

    private static void WriteAttachedTransform(CollectibleObject coll, AttachmentTransform t)
    {
        // A plain vanilla tool carries no attributes at all until something gives it some - and a tool on a
        // toolstrap is exactly what one tunes here, so create the node rather than silently dropping the edit.
        coll.Attributes ??= new JsonObject(new JObject());
        if (coll.Attributes.Token is not JObject root) return;

        if (root["immersiveBackpackAttachment"] is not JObject attachment)
        {
            attachment = new JObject();
            root["immersiveBackpackAttachment"] = attachment;
        }

        attachment["attachedTransform"] = new JObject
        {
            ["scale"] = t.Scale,
            ["offset"] = new JArray(t.Offset[0], t.Offset[1], t.Offset[2]),
            ["rotation"] = new JArray(t.Rotation[0], t.Rotation[1], t.Rotation[2])
        };
    }

    // Our transform is a uniform scale, an offset and an XYZ rotation; a ModelTransform additionally carries a
    // per-axis scale and an origin, which our system has no use for (an addon is anchored at its point's pivot).
    // Only the X scale is read back, so keep the three axes locked together while tuning.
    private static ModelTransform ToModelTransform(AttachmentTransform t) => new()
    {
        Translation = new Vec3f(t.Offset[0], t.Offset[1], t.Offset[2]),
        Rotation = new Vec3f(t.Rotation[0], t.Rotation[1], t.Rotation[2]),
        Origin = new Vec3f(0.5f, 0.5f, 0.5f),
        ScaleXYZ = new Vec3f(t.Scale, t.Scale, t.Scale)
    };

    private static AttachmentTransform FromModelTransform(ModelTransform m) => new()
    {
        Scale = m.ScaleXYZ.X,
        Offset = [m.Translation.X, m.Translation.Y, m.Translation.Z],
        Rotation = [m.Rotation.X, m.Rotation.Y, m.Rotation.Z]
    };
}
