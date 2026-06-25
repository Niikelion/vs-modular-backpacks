using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace ImmersiveBackpacks;

/// <summary>
/// A render transform for an attached addon: a uniform scale multiplier, an offset and an XYZ rotation
/// (degrees). Defined per attachment point, separately for the placed block and the worn bag, and
/// optionally overridden per attachable item. The final transform an addon renders with is the point's
/// transform combined with the item's override (scale multiplied, offset and rotation added).
///
/// Units: the placed offset is in block fractions [0,1]; the worn offset is in 16-unit shape-model space.
/// </summary>
public class AttachmentTransform
{
    public float Scale = 1f;
    public float[] Offset = { 0f, 0f, 0f };
    public float[] Rotation = { 0f, 0f, 0f };

    public static readonly AttachmentTransform Identity = new();

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

    /// <summary>Point transform combined with an item override (this = point, other = item).</summary>
    public AttachmentTransform CombinedWith(AttachmentTransform other) => new()
    {
        Scale = Scale * other.Scale,
        Offset = new[] { Offset[0] + other.Offset[0], Offset[1] + other.Offset[1], Offset[2] + other.Offset[2] },
        Rotation = new[] { Rotation[0] + other.Rotation[0], Rotation[1] + other.Rotation[1], Rotation[2] + other.Rotation[2] }
    };
}
