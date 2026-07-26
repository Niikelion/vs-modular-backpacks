using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace ImmersiveBackpacks;

/// <summary>
/// A render transform for an attached addon: a uniform scale multiplier, an offset and an XYZ rotation
/// (degrees). Defined per attachment point, separately for the placed block and the worn bag, and
/// optionally overridden per attachable item. The final transform an addon renders with is the point's
/// transform combined with the item's override (scale multiplied, offset and rotation added).
///
/// Per item, the override is split into a context-specific part (<c>placed</c>/<c>worn</c>) and a shared
/// <c>attachedTransform</c> that applies in every context (handy for an attached shape that needs the same
/// scale/rotation whether the bag is placed or worn). See <see cref="ForItem"/>.
///
/// Units: the offset is in block fractions [0,1] in every context - the composed worn/held path scales it to
/// 16-unit shape space itself (see AttachmentComposer.WrapAddon), so one attachedTransform positions an addon
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

    /// <summary>
    /// The full per-item transform for a render context: the context-specific override
    /// (<paramref name="contextKey"/> = "placed" or "worn") combined with the shared
    /// <c>attachedTransform</c> that applies in every context.
    /// </summary>
    public static AttachmentTransform ForItem(CollectibleObject collectible, string contextKey)
        => FromItem(collectible, contextKey).CombinedWith(FromItem(collectible, "attachedTransform"));

    /// <summary>Point transform combined with an item override (this = point, other = item).</summary>
    public AttachmentTransform CombinedWith(AttachmentTransform other) => new()
    {
        Scale = Scale * other.Scale,
        Offset = new[] { Offset[0] + other.Offset[0], Offset[1] + other.Offset[1], Offset[2] + other.Offset[2] },
        Rotation = new[] { Rotation[0] + other.Rotation[0], Rotation[1] + other.Rotation[1], Rotation[2] + other.Rotation[2] }
    };
}
