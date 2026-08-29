using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace ImmersiveBackpacks;

/// <summary>
/// A render transform for an attached addon: a uniform scale multiplier, an offset and an XYZ rotation
/// (degrees). Defined per attachment point, separately for the placed block and the worn bag, and
/// optionally overridden per attachable item. The final transform an addon renders with is the point's
/// transform combined with the item's override (scale multiplied, offset and rotation added).
///
/// Per item, the override is split into a context-specific part (<c>placed</c>/<c>worn</c>, nested under
/// <c>immersiveBackpackAttachment</c>) and a shared transform at the top-level
/// <c>immersiveAttachedTransform</c> attribute that applies in every context (handy for an attached shape that
/// needs the same scale/rotation whether the bag is placed or worn). See <see cref="ForItem"/>.
///
/// Units: the offset is in block fractions [0,1] in every context - the composed worn/held path scales it to
/// 16-unit shape space itself (see AttachmentComposer.WrapAddon), so one shared transform positions an addon
/// identically whether the bag is placed, worn or held. Tune it live with /tfedit (AttachmentTransformEditor).
/// </summary>
public class AttachmentTransform
{
    public float Scale = 1f;
    public float[] Offset = [0f, 0f, 0f];
    public float[] Rotation = [0f, 0f, 0f];

    public static readonly AttachmentTransform Identity = new();

    /// <summary>
    /// Bumped by the live transform editor (see <see cref="AttachmentTransformEditor"/>) whenever a transform is
    /// nudged. Folded into the composed-mesh cache keys, which are otherwise keyed only by addon placement and
    /// content - so without it a tuned transform would not show until the bag's contents changed.
    /// </summary>
    public static int TuningGeneration;

    /// <summary>A rotation-only transform (identity scale/offset), e.g., a slot rotation read from a shape.</summary>
    public static AttachmentTransform FromRotation(float[] rotation)
        => new() { Rotation = rotation is { Length: >= 3 } ? rotation : new[] { 0f, 0f, 0f } };

    public static AttachmentTransform FromJson(JsonObject obj)
    {
        var t = new AttachmentTransform();
        if (obj == null || !obj.Exists) return t;

        t.Scale = obj["scale"].AsFloat(1f);
        var offset = obj["offset"].AsArray<float>(null);
        if (offset is { Length: >= 3 }) t.Offset = offset;
        var rotation = obj["rotation"].AsArray<float>(null);
        if (rotation is { Length: >= 3 }) t.Rotation = rotation;
        return t;
    }

    /// <summary>Reads a per-item transform override from a collectible's immersiveBackpackAttachment.{key}.</summary>
    public static AttachmentTransform FromItem(CollectibleObject collectible, string key)
        => FromJson(collectible?.Attributes?["immersiveBackpackAttachment"]?[key]);

    /// <summary>The top-level attribute holding the shared per-item transform. See <see cref="Attached"/>.</summary>
    public const string AttachedTransformKey = "immersiveAttachedTransform";

    /// <summary>
    /// The shared per-item transform - the one that applies in every context.
    ///
    /// It lives at a *top-level* attribute in *ModelTransform* form, and both halves of that are the point.
    /// Vanilla's Transform Editor stores an extraTransforms entry at <c>collectible.Attributes[name]</c> as a
    /// ModelTransform, so matching that shape is what lets <c>/tfedit</c> read and write this transform with no
    /// interception at all - and therefore what lets a write-back tool find it and save it into the asset.
    /// Nesting it under <c>immersiveBackpackAttachment</c>, as before 1.8.0, put it somewhere the editor could
    /// only reach by hijacking its events, and somewhere no write-back could resolve.
    ///
    /// Pre-1.8.0 assets and third-party patches still work: the old nested key is read when the new one is absent.
    /// </summary>
    public static AttachmentTransform Attached(CollectibleObject collectible)
    {
        var attributes = collectible?.Attributes;
        var declared = attributes?[AttachedTransformKey];

        return declared is { Exists: true }
            ? FromModelTransform(declared)
            : FromJson(attributes?["immersiveBackpackAttachment"]?["attachedTransform"]);
    }

    /// <summary>
    /// Reads the ModelTransform form: <c>translation</c> and <c>rotation</c> as xyz objects plus a uniform
    /// <c>scale</c>.
    ///
    /// Bound with <c>AsObject</c> rather than read key by key, because there are two spellings of this in play:
    /// an asset writes lowercase JSON, while the Transform Editor's Apply leaves a serialised ModelTransform in
    /// the collectible's attributes with the C# property names. The same binding vanilla uses accepts both, and
    /// resolves <c>scale</c> against <c>scaleXyz</c> as a bonus; reading the keys by hand silently returned zeros
    /// for the in-memory form, which would collapse an addon the moment it was tuned.
    ///
    /// <c>origin</c> is read and discarded: an addon is anchored at its attachment point's pivot, so an origin
    /// here would have nothing to mean. Only one scale axis is kept, because the point and item transforms are
    /// combined by multiplication and a non-uniform addon scale has never been supported.
    /// </summary>
    public static AttachmentTransform FromModelTransform(JsonObject obj)
    {
        if (obj == null || !obj.Exists) return new AttachmentTransform();

        // EnsureDefaultValues fills in whatever a partial block - a patch that sets only `rotation`, say - left
        // unset, so an omitted scale stays 1 rather than collapsing the addon to nothing.
        var m = obj.AsObject<ModelTransform>()?.EnsureDefaultValues();
        if (m == null) return new AttachmentTransform();

        return new AttachmentTransform
        {
            Scale = m.ScaleXYZ.X,
            Offset = [m.Translation.X, m.Translation.Y, m.Translation.Z],
            Rotation = [m.Rotation.X, m.Rotation.Y, m.Rotation.Z]
        };
    }

    /// <summary>
    /// The full per-item transform for a render context: the context-specific override
    /// (<paramref name="contextKey"/> = "placed" or "worn") combined with the shared transform
    /// that applies in every context.
    /// </summary>
    public static AttachmentTransform ForItem(CollectibleObject collectible, string contextKey)
        => FromItem(collectible, contextKey).CombinedWith(Attached(collectible));

    /// <summary>Point transform combined with an item override (this = point, other = item).</summary>
    public AttachmentTransform CombinedWith(AttachmentTransform other) => new()
    {
        Scale = Scale * other.Scale,
        Offset = new[] { Offset[0] + other.Offset[0], Offset[1] + other.Offset[1], Offset[2] + other.Offset[2] },
        Rotation = new[] { Rotation[0] + other.Rotation[0], Rotation[1] + other.Rotation[1], Rotation[2] + other.Rotation[2] }
    };
}
