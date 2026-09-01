#nullable enable
using System;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace ImmersiveModularBackpacks.Attachments;

/// <summary>
/// A render transform for an attached addon: a uniform scale multiplier, an offset and an XYZ rotation
/// (degrees). Defined per attachment point and render context, and optionally overridden per attachable item.
/// The final transform an addon renders with is the point's transform composed with the item's local override.
///
/// Every configured transform uses Vintage Story's <see cref="ModelTransform"/> JSON shape: translation and
/// rotation objects plus scale. Per item, the override is split into a context-specific part
/// (<c>placed</c>/<c>worn</c>, nested under <c>immersiveBackpackAttachment</c>) and a shared transform at the top-level
/// <c>immersiveAttachedTransform</c> attribute that applies in every context (handy for an attached shape that
/// needs the same scale/rotation whether the bag is placed or worn). See <see cref="ForItem"/>.
///
/// Units: the offset is in block fractions [0,1] in every context - the composed worn/held path scales it to
/// 16-unit shape space itself (see AttachmentComposer.WrapAddon), so one shared transform positions an addon
/// identically whether the bag is placed, worn, or held. Tune it live with /tfedit (AttachmentTransformEditor).
/// </summary>
public class AttachmentTransform
{
    public float Scale = 1f;
    public float[] Offset = [0f, 0f, 0f];
    public float[] Rotation = [0f, 0f, 0f];

    public static readonly AttachmentTransform Identity = new();

    /// <summary>
    /// Bumped by the live transform editor (see <see cref="ImmersiveBackpacks.AttachmentTransformEditor"/>) whenever a transform is
    /// nudged. Folded into the composed-mesh cache keys, which are otherwise keyed only by addon placement and
    /// content - so without it a tuned transform would not show until the bag's contents changed.
    /// </summary>
    public static int TuningGeneration;

    /// <summary>A rotation-only transform (identity scale/offset), e.g., a slot rotation read from a shape.</summary>
    public static AttachmentTransform FromRotation(float[] rotation)
        => new() { Rotation = rotation is { Length: >= 3 } ? rotation : [0f, 0f, 0f] };

    /// <summary>Reads a per-item transform override from a collectible's immersiveBackpackAttachment.{key}.</summary>
    public static AttachmentTransform FromItem(CollectibleObject? collectible, string key)
        => FromModelTransform(collectible?.Attributes?["immersiveBackpackAttachment"][key]);

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
    /// The old nested key is still read when the top-level key is absent, but it uses the same ModelTransform
    /// format as every other transform.
    /// </summary>
    public static AttachmentTransform Attached(CollectibleObject? collectible)
    {
        var attributes = collectible?.Attributes;
        var declared = attributes?[AttachedTransformKey];

        return declared is { Exists: true }
            ? FromModelTransform(declared)
            : FromModelTransform(attributes?["immersiveBackpackAttachment"]["attachedTransform"]);
    }

    /// <summary>
    /// Reads the ModelTransform form: <c>translation</c> and <c>rotation</c> as xyz objects plus a uniform
    /// <c>scale</c>.
    ///
    /// Bound with <c>AsObject</c> rather than read key by key, because there are two spellings of this in play:
    /// an asset writes lowercase JSON, while the Transform Editors Apply leaves a serialised ModelTransform in
    /// the collectible's attributes with the C# property names. The same binding vanilla uses accepts both, and
    /// resolves <c>scale</c> against <c>scaleXyz</c> as a bonus; reading the keys by hand silently returned zeros
    /// for the in-memory form, which would collapse an addon the moment it was tuned.
    ///
    /// <c>origin</c> is read and discarded: an addon is anchored at its attachment point's pivot, so an origin
    /// here would have nothing to mean. Only one scale axis is kept because the point and item transforms are
    /// combined by multiplication, and a non-uniform addon scale has never been supported.
    /// </summary>
    public static AttachmentTransform FromModelTransform(JsonObject? obj)
    {
        if (obj is not { Exists: true }) return new();

        // EnsureDefaultValues fills in whatever a partial block - a patch that sets only `rotation`, say - left
        // unset, so an omitted scale stays 1 rather than collapsing the addon to nothing.
        var m = obj.AsObject<ModelTransform>()?.EnsureDefaultValues();
        if (m == null) return new();

        return new()
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
    public static AttachmentTransform ForItem(CollectibleObject? collectible, string contextKey)
        => FromItem(collectible, contextKey).CombinedWith(Attached(collectible));

    /// <summary>Affine composition of this parent transform with <paramref name="other"/> in local space.
    /// The other transform is applied first, followed by this one.</summary>
    public AttachmentTransform CombinedWith(AttachmentTransform other)
    {
        float[] matrix = Mat4f.Mul(Mat4f.Create(), ToMatrix(this), ToMatrix(other));

        // Uniform scales stay uniform under composition. Normalize the rotation basis before extracting Euler
        // angles, then move the composed translation back into our local-offset representation (R * S * T).
        float scale = MathF.Sqrt(matrix[0] * matrix[0] + matrix[1] * matrix[1] + matrix[2] * matrix[2]);
        if (scale <= 1e-8f) return new() { Scale = 0f };

        float[] rotationMatrix = (float[])matrix.Clone();
        for (int column = 0; column < 3; column++)
            for (int row = 0; row < 3; row++)
                rotationMatrix[column * 4 + row] /= scale;

        float tx = matrix[12], ty = matrix[13], tz = matrix[14];
        float invScale = 1f / scale;
        return new()
        {
            Scale = scale,
            Offset =
            [
                (rotationMatrix[0] * tx + rotationMatrix[1] * ty + rotationMatrix[2] * tz) * invScale,
                (rotationMatrix[4] * tx + rotationMatrix[5] * ty + rotationMatrix[6] * tz) * invScale,
                (rotationMatrix[8] * tx + rotationMatrix[9] * ty + rotationMatrix[10] * tz) * invScale
            ],
            Rotation = ExtractEuler(rotationMatrix)
        };
    }

    private static float[] ToMatrix(AttachmentTransform transform)
    {
        const float d2R = MathF.PI / 180f;
        float[] matrix = Mat4f.Create();
        Mat4f.Identity(matrix);
        Mat4f.RotateByXYZ(matrix,
            transform.Rotation[0] * d2R,
            transform.Rotation[1] * d2R,
            transform.Rotation[2] * d2R);
        Mat4f.Scale(matrix, transform.Scale, transform.Scale, transform.Scale);
        Mat4f.Translate(matrix, transform.Offset[0], transform.Offset[1], transform.Offset[2]);
        return matrix;
    }

    private static float[] ExtractEuler(float[] matrix)
    {
        const float r2D = 180f / MathF.PI;
        float y = MathF.Asin(GameMath.Clamp(matrix[8], -1f, 1f));

        float x;
        float z;
        if (MathF.Abs(MathF.Cos(y)) > 1e-4f)
        {
            x = MathF.Atan2(-matrix[9], matrix[10]);
            z = MathF.Atan2(-matrix[4], matrix[0]);
        }
        else
        {
            x = MathF.Atan2(matrix[6], matrix[5]);
            z = 0f;
        }

        return [x * r2D, y * r2D, z * r2D];
    }

    public AttachmentTransform Mirrored(AttachmentMirror mirror)
    {
        float[] offset = (float[])Offset.Clone();
        float[] rotation = (float[])Rotation.Clone();

        if ((mirror & AttachmentMirror.X) != 0)
        {
            offset[0] = -offset[0];
            rotation[1] = -rotation[1];
            rotation[2] = -rotation[2];
        }
        if ((mirror & AttachmentMirror.Y) != 0)
        {
            offset[1] = -offset[1];
            rotation[0] = -rotation[0];
            rotation[2] = -rotation[2];
        }
        if ((mirror & AttachmentMirror.Z) != 0)
        {
            offset[2] = -offset[2];
            rotation[0] = -rotation[0];
            rotation[1] = -rotation[1];
        }

        return new() { Scale = Scale, Offset = offset, Rotation = rotation };
    }
}
