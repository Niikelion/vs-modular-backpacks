#nullable enable
using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace ImmersiveModularBackpacks.Attachments;

/// <summary>
/// The single, host-agnostic composition core for the attachment tree. Every render path routes through here:
///   * <see cref="ComposeShape"/> — recursive SHAPE composition (worn/entity): a node's own base shape with
///     each occupied child's <see cref="IAttachment.GetShape"/> wrapped at its slot marker, textures merged
///     and prefixed per level so nesting (bag → strap → tool) never collides. Fed straight to vanilla's gear
///     pipeline by the host adapter (which does the step-parent prep on the root).
///   * <see cref="ComposeMesh"/> / <see cref="MeshFor"/> — recursive MESH composition (placed/held): a node's
///     base mesh with each child's mesh matrix-placed at its marker. <see cref="MeshFor"/> is where the
///     escape hatch lives: a child implementing <see cref="IAttachmentMeshSource"/> (the lantern) supplies its
///     authoritative mesh; everything else bakes/composes from its shape.
///
/// A faithful generalization of the bag's original inline composition, lifted to run over any
/// <see cref="IAttachment"/> node instead of a fixed point list. Every host routes through here —
/// <c>ItemImmersiveBag</c> (worn and held/GUI) and the placed block renderer alike. Point geometry is baked before
/// composition, so both paths consume the same anchor and transform. See [[attachment-system-design]].
/// </summary>
public static class AttachmentComposer
{
    private const float D2R = (float)(Math.PI / 180.0);

    // ---- worn / entity: shape composition -----------------------------------

    /// <summary>
    /// The node's render shape in its own local space, its occupied children composed in recursively. Callers
    /// that render worn (the host adapter) run step-parent prep on the returned root; child composition itself
    /// must not, so this only wraps + attaches. Returns null-ish (empty) when the node has no usable shape,
    /// letting a node opt out of worn rendering.
    /// </summary>
    public static Shape? ComposeShape(ICoreAPI api, IAttachment node)
    {
        var shape = StackShape(api, node.Stack);
        if (shape == null)
        {
            var coll = node.Stack.Collectible;
            if (coll == null) return null;
            var baseComposite = AttachmentMesh.AttachedShapeComposite(coll) ?? GetDisplayShape(coll);
            shape = LoadShape(api, baseComposite?.Base?.ToString(), coll.Code.Domain);
            if (shape?.Elements == null || shape.Elements.Length == 0) return shape;

            // The node's own textures (shape-file textures overridden by the collectible's, or a variant addon's
            // stack-driven textures via IAttachableToEntity).
            ApplyAddonTextures(node.Stack, shape);
        }

        ComposeChildrenInto(api, shape, node);
        return shape;
    }

    /// <summary>
    /// Shape providers for collectibles whose geometry lives in stack attributes rather than in a static
    /// display shape (a Toolsmith tinkered tool). The worn counterpart of the
    /// <see cref="IAttachmentMeshSource"/> escape hatch, which serves the placed/held mesh path only.
    /// Tried before the node's display shape; a provider returns null to defer. Textures are the provider's
    /// job, since it is the only one that knows what its shape references.
    /// </summary>
    public static readonly List<System.Func<ICoreAPI, ItemStack, Shape?>> StackShapeSources = [];

    private static Shape? StackShape(ICoreAPI api, ItemStack stack)
    {
        foreach (var source in StackShapeSources)
            if (source(api, stack) is { Elements.Length: > 0 } shape) return shape;
        return null;
    }

    /// <summary>
    /// Attaches a node's occupied children into an ALREADY-LOADED parent shape using each point's baked geometry,
    /// with textures merged and per-child prefixed. Separated from <see cref="ComposeShape"/> so a host root that must build its base
    /// shape specially (the worn bag: its own <c>attachableToEntity.attachedShape</c> + step-parent prep) can
    /// reuse the exact same child-attaching logic without going through the node's own display shape.
    /// </summary>
    public static void ComposeChildrenInto(ICoreAPI api, Shape parentShape, IAttachment node)
    {
        var points = node.Points;
        if (points.Count == 0 || parentShape?.Elements == null) return;

        string? stepParent = RootStepParent(parentShape.Elements);
        int idx = 0;
        foreach (var pt in points)
        {
            var child = AttachmentFactory.WithPointContext(node.GetAttached(pt.Code), pt);
            if (child == null) continue;

            // Through the node's own GetShape (not ComposeShape directly) so a child can override how it renders;
            // the default delegates back here, bringing its own children.
            var childShape = child.GetShape(api);
            if (childShape?.Elements == null || childShape.Elements.Length == 0) continue;

            // Prefix the whole child subtree so its (already-composed) element/texture codes never collide with
            // ours or a sibling's. Nested prefixes stack (ibN_ibK_...) which stays unique.
            string sub = "ib" + idx++ + "_";
            PrefixShape(childShape, sub);
            MergeInto(parentShape.Textures ??= new(), childShape.Textures);
            MergeInto(parentShape.TextureSizes ??= new(), childShape.TextureSizes);

            var itemTransform = AttachmentTransform.ForItem(child.Stack.Collectible, "worn").Mirrored(pt.Mirror);
            var tf = pt.Transform.CombinedWith(itemTransform);
            // Anchor by the child's fixed model origin (16-unit), not its geometry bounds - content-stable.
            var childOrigin = AttachmentMesh.ModelOrigin(child.Stack.Collectible);
            var wrapper = WrapAddon(childShape.Elements,
                [pt.Origin.X * 16.0, pt.Origin.Y * 16.0, pt.Origin.Z * 16.0], tf,
                [childOrigin.X * 16.0, childOrigin.Y * 16.0, childOrigin.Z * 16.0]);
            wrapper.StepParentName = stepParent;
            parentShape.Elements = Append(parentShape.Elements, wrapper);
        }
    }

    // ---- placed / held: mesh composition ------------------------------------

    /// <summary>
    /// The mesh for a node in placed/held (item/block-atlas) space. Prefers the node's own authoritative mesh
    /// if it implements <see cref="IAttachmentMeshSource"/> (the lantern's variant/glass/glow), otherwise
    /// composes its shape-derived base mesh with its children. Returns an independently owned quad-layout mesh.
    /// </summary>
    public static MeshData? MeshFor(ICoreClientAPI capi, IAttachment node)
    {
        if (node is not IAttachmentMeshSource ms) return ComposeMesh(capi, node);
        var m = ms.GetMesh(capi);
        return m == null ? null : AttachmentMeshNormalizer.CloneForComposition(m);
    }

    /// <summary>
    /// A node's base mesh (its own shape/stack, honoring an attached-specific shape) with each occupied
    /// child's <see cref="MeshFor"/> matrix-placed at its slot marker. Local item-model space ([0,1]); the
    /// host adapter applies the world/block or item ModelTransform on top. Mirror of <c>BuildHeldMesh</c>
    /// minus the GUI mirror, generalized over child nodes.
    /// </summary>
    public static MeshData? ComposeMesh(ICoreClientAPI capi, IAttachment node)
    {
        var baseMesh = AttachmentMesh.Tessellate(capi, node.Stack);
        if (baseMesh == null) return null;
        baseMesh = AttachmentMeshNormalizer.CloneForComposition(baseMesh);

        var points = node.Points;
        if (points.Count == 0) return baseMesh;

        var mat = new Matrixf();
        foreach (var pt in points)
        {
            var child = AttachmentFactory.WithPointContext(node.GetAttached(pt.Code), pt);
            if (child == null) continue;

            var childMesh = MeshFor(capi, child);
            if (childMesh == null) continue;

            ChildMatrix(mat, pt, child);
            childMesh.MatrixTransform(mat.Values);

            baseMesh.AddMeshData(childMesh);
        }
        return baseMesh;
    }

    /// <summary>
    /// Transforms a box from a child attachment's local model space into its parent's model space using the
    /// exact placed-render transform. Hosts use this for nested interaction-point hitboxes.
    /// </summary>
    public static Cuboidf TransformChildBox(IAttachmentPoint parentPoint, IAttachment child, Cuboidf childBox)
    {
        var matrix = ChildMatrix(new Matrixf(), parentPoint, child).Values;
        float minX = float.MaxValue, minY = float.MaxValue, minZ = float.MaxValue;
        float maxX = float.MinValue, maxY = float.MinValue, maxZ = float.MinValue;

        float[] xs = [childBox.X1, childBox.X2];
        float[] ys = [childBox.Y1, childBox.Y2];
        float[] zs = [childBox.Z1, childBox.Z2];
        foreach (float x in xs)
        foreach (float y in ys)
        foreach (float z in zs)
        {
            float tx = matrix[0] * x + matrix[4] * y + matrix[8] * z + matrix[12];
            float ty = matrix[1] * x + matrix[5] * y + matrix[9] * z + matrix[13];
            float tz = matrix[2] * x + matrix[6] * y + matrix[10] * z + matrix[14];
            minX = MathF.Min(minX, tx); maxX = MathF.Max(maxX, tx);
            minY = MathF.Min(minY, ty); maxY = MathF.Max(maxY, ty);
            minZ = MathF.Min(minZ, tz); maxZ = MathF.Max(maxZ, tz);
        }

        return new(minX, minY, minZ, maxX, maxY, maxZ);
    }

    private static Matrixf ChildMatrix(Matrixf matrix, IAttachmentPoint point, IAttachment child)
    {
        // Anchor by the fixed model origin so container contents never move the container itself.
        var origin = AttachmentMesh.ModelOrigin(child.Stack.Collectible);
        var itemTransform = AttachmentTransform.ForItem(child.Stack.Collectible, "placed").Mirrored(point.Mirror);
        var tf = point.Transform.CombinedWith(itemTransform);
        float s = tf.Scale;
        return matrix.Identity()
            .Translate(point.Origin.X, point.Origin.Y, point.Origin.Z)
            .RotateX(tf.Rotation[0] * D2R)
            .RotateY(tf.Rotation[1] * D2R)
            .RotateZ(tf.Rotation[2] * D2R)
            .Scale(s, s, s)
            .Translate(tf.Offset[0] - origin.X, tf.Offset[1] - origin.Y, tf.Offset[2] - origin.Z);
    }

    // ---- shape helpers (lifted from ItemImmersiveBag; kept behaviour-identical) ----

    /// <summary>Loads a fresh, independent shape from a composite base path (host adapters use it to build a
    /// root base shape before composing children in).</summary>
    public static Shape? LoadShape(ICoreAPI api, string? basePath, string defaultDomain)
    {
        if (string.IsNullOrEmpty(basePath)) return null;
        var loc = AssetLocation.Create(basePath, defaultDomain)
            .CopyWithPathPrefixAndAppendixOnce("shapes/", ".json");
        return Shape.TryGet(api, loc.ToString());
    }

    private static CompositeShape? GetDisplayShape(CollectibleObject collectible)
        => collectible switch
        {
            Item it => it.Shape,
            Block bl => bl.Shape,
            _ => null
        };

    /// <summary>Namespaces a whole shape - element names, face texture codes and texture keys - so it can be
    /// merged with a sibling or parent shape without colliding.</summary>
    public static void PrefixShape(Shape shape, string prefix)
    {
        foreach (var el in shape.Elements)
            el.WalkRecursive(e =>
            {
                e.Name = prefix + e.Name;
                if (e.FacesResolved == null) return;
                foreach (var face in e.FacesResolved)
                    if (face != null && face.Enabled)
                        face.Texture = prefix + face.Texture;
            });

        shape.Textures = RekeyAssets(shape.Textures, prefix);
        shape.TextureSizes = RekeySizes(shape.TextureSizes, prefix);
    }

    private static void ApplyAddonTextures(ItemStack addonStack, Shape addonShape)
    {
        if (addonStack.Collectible is IAttachableToEntity atta)
        {
            addonShape.Textures ??= new();
            try
            {
                atta.CollectTextures(addonStack, addonShape, "", new());
                return;
            }
            catch (Exception)
            {
                // Variant addons (the lantern) throw when their material attributes are absent; fall back to
                // the collectible's own textures rather than failing the whole tesselation.
            }
        }
        MergeAddonTextures(addonStack.Collectible, addonShape);
    }

    private static void MergeAddonTextures(CollectibleObject collectible, Shape addonShape)
    {
        IDictionary<string, CompositeTexture>? src = collectible switch
        {
            Item it => it.Textures,
            Block bl => bl.Textures,
            _ => null
        };
        if (src == null) return;

        addonShape.Textures ??= new();
        foreach (var kv in src)
            addonShape.Textures[kv.Key] = kv.Value.Base;
    }

    private static Dictionary<string, AssetLocation> RekeyAssets(
        Dictionary<string, AssetLocation>? src, string prefix)
    {
        var dst = new Dictionary<string, AssetLocation>();
        if (src != null)
            foreach (var kv in src) dst[prefix + kv.Key] = kv.Value;
        return dst;
    }

    private static Dictionary<string, int[]> RekeySizes(Dictionary<string, int[]>? src, string prefix)
    {
        var dst = new Dictionary<string, int[]>();
        if (src != null)
            foreach (var kv in src) dst[prefix + kv.Key] = kv.Value;
        return dst;
    }

    private static void MergeInto<T>(Dictionary<string, T> target, Dictionary<string, T>? src)
    {
        if (src == null) return;
        foreach (var kv in src) target[kv.Key] = kv.Value;
    }

    private static ShapeElement WrapAddon(ShapeElement[] addonElements, double[] slotOrigin,
        AttachmentTransform tf, double[] addonOrigin)
    {
        // Shift so the addon's fixed model origin lands on the wrapper, displaced by the authored offset -
        // so the addon's origin (not its geometry centre) sits at the slot, matching the mesh path.
        double[] shift =
        {
            addonOrigin[0] - tf.Offset[0] * 16.0,
            addonOrigin[1] - tf.Offset[1] * 16.0,
            addonOrigin[2] - tf.Offset[2] * 16.0
        };
        foreach (var el in addonElements)
        {
            Shift(el.From, shift);
            Shift(el.To, shift);
            Shift(el.RotationOrigin, shift);
            el.StepParentName = null;
        }

        double scale = tf.Scale;
        var wrapper = new ShapeElement
        {
            Name = "addon",
            From = (double[])slotOrigin.Clone(),
            To = (double[])slotOrigin.Clone(),
            RotationOrigin = (double[])slotOrigin.Clone(),
            RotationX = tf.Rotation[0],
            RotationY = tf.Rotation[1],
            RotationZ = tf.Rotation[2],
            ScaleX = scale,
            ScaleY = scale,
            ScaleZ = scale,
            Children = addonElements,
            FacesResolved = new ShapeElementFace[6]
        };
        foreach (var el in addonElements) el.ParentElement = wrapper;
        return wrapper;
    }

    private static void Shift(double[]? p, double[] delta)
    {
        if (p == null) return;
        p[0] -= delta[0]; p[1] -= delta[1]; p[2] -= delta[2];
    }

    private static ShapeElement[] Append(ShapeElement[] elements, ShapeElement added)
    {
        var result = new ShapeElement[elements.Length + 1];
        elements.CopyTo(result, 0);
        result[^1] = added;
        return result;
    }

    private static string? RootStepParent(ShapeElement[] elements)
    {
        foreach (var element in elements)
            if (!string.IsNullOrEmpty(element.StepParentName)) return element.StepParentName;
        return null;
    }
}
