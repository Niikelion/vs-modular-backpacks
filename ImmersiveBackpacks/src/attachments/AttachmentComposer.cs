using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace ImmersiveBackpacks.attachments;

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
/// A faithful generalisation of the bag's original inline composition, lifted to run over any
/// <see cref="IAttachment"/> node instead of a fixed point list. <c>ItemImmersiveBag</c> (worn + held/GUI) now
/// routes through here; the placed block renderer still has its own inline copy (migrated next). Geometry
/// still comes from the owner shape's <c>slot_&lt;code&gt;</c> markers (mesh path via
/// <see cref="AttachmentMesh.ReadSlots"/>, worn path via the slot element found in the tree), so authored bag
/// shapes keep working unchanged. See [[attachment-system-design]].
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
    public static Shape ComposeShape(ICoreAPI api, IAttachment node)
    {
        var coll = node.Stack.Collectible;
        var baseComposite = AttachmentMesh.AttachedShapeComposite(coll) ?? GetDisplayShape(coll);
        Shape shape = LoadShape(api, baseComposite?.Base?.ToString(), coll.Code.Domain);
        if (shape?.Elements == null || shape.Elements.Length == 0) return shape;

        // The node's own textures (shape-file textures overridden by the collectible's, or a variant addon's
        // stack-driven textures via IAttachableToEntity).
        ApplyAddonTextures(node.Stack, shape);

        ComposeChildrenInto(api, shape, node);
        return shape;
    }

    /// <summary>
    /// Attaches a node's occupied children into an ALREADY-LOADED parent shape, under each child's
    /// <c>slot_&lt;code&gt;</c> element (inheriting the full ancestor transform chain), textures merged and
    /// per-child prefixed. Separated from <see cref="ComposeShape"/> so a host root that must build its base
    /// shape specially (the worn bag: its own <c>attachableToEntity.attachedShape</c> + step-parent prep) can
    /// reuse the exact same child-attaching logic without going through the node's own display shape.
    /// </summary>
    public static void ComposeChildrenInto(ICoreAPI api, Shape parentShape, IAttachment node)
    {
        var points = node.Points;
        if (points == null || points.Count == 0 || parentShape?.Elements == null) return;

        var slotElems = FindSlotElements(parentShape.Elements);
        int idx = 0;
        foreach (var pt in points)
        {
            var child = node.GetAttached(pt.Code);
            if (child == null) continue;
            if (!slotElems.TryGetValue(pt.Code, out var s) || s.parent == null) continue;

            // Through the node's own GetShape (not ComposeShape directly) so a child can override how it renders;
            // the default delegates back here, bringing its own children.
            Shape childShape = child.GetShape(api);
            if (childShape?.Elements == null || childShape.Elements.Length == 0) continue;

            // Prefix the whole child subtree so its (already-composed) element/texture codes never collide with
            // ours or a sibling's. Nested prefixes stack (ibN_ibK_...) which stays unique.
            string sub = "ib" + idx++ + "_";
            PrefixShape(childShape, sub);
            MergeInto(parentShape.Textures ??= new(), childShape.Textures);
            MergeInto(parentShape.TextureSizes ??= new(), childShape.TextureSizes);

            var slot = s.slot;
            // Anchor at the slot's pivot (rotationOrigin), not its box centre - matches the mesh path and
            // is independent of the box extent. Defaults to box centre when no pivot is authored.
            double[] slotOrigin = slot.RotationOrigin is { Length: >= 3 }
                ? slot.RotationOrigin
                : new[] { (slot.From[0] + slot.To[0]) / 2.0, (slot.From[1] + slot.To[1]) / 2.0, (slot.From[2] + slot.To[2]) / 2.0 };
            var slotRot = new[] { (float)slot.RotationX, (float)slot.RotationY, (float)slot.RotationZ };
            // Worn placement: the slot marker's own rotation, the point's own transform (identity for a bag's
            // addon points; a toolstrap's tool scale for its tool points), then the child's shared
            // attachedTransform. The parent chain supplies the rest.
            var tf = AttachmentTransform.FromRotation(slotRot)
                .CombinedWith(pt.Transform)
                .CombinedWith(AttachmentTransform.FromItem(child.Stack.Collectible, "attachedTransform"));
            // Anchor by the child's fixed model origin (16-unit), not its geometry bounds - content-stable.
            var childOrigin = AttachmentMesh.ModelOrigin(child.Stack.Collectible);
            var wrapper = WrapAddon(childShape.Elements, slotOrigin, tf,
                new[] { childOrigin.X * 16.0, childOrigin.Y * 16.0, childOrigin.Z * 16.0 });
            AttachUnder(s.parent, new[] { wrapper });
        }
    }

    // ---- placed / held: mesh composition ------------------------------------

    /// <summary>
    /// The mesh for a node in placed/held (item/block-atlas) space. Prefers the node's own authoritative mesh
    /// if it implements <see cref="IAttachmentMeshSource"/> (the lantern's variant/glass/glow), otherwise
    /// composes its shape-derived base mesh with its children.
    /// </summary>
    public static MeshData MeshFor(ICoreClientAPI capi, IAttachment node)
    {
        if (node is IAttachmentMeshSource ms)
        {
            var m = ms.GetMesh(capi);
            if (m != null) return m;
        }
        return ComposeMesh(capi, node);
    }

    /// <summary>
    /// A node's base mesh (its own shape/stack, honouring an attached-specific shape) with each occupied
    /// child's <see cref="MeshFor"/> matrix-placed at its slot marker. Local item-model space ([0,1]); the
    /// host adapter applies the world/block or item ModelTransform on top. Mirror of <c>BuildHeldMesh</c>
    /// minus the GUI mirror, generalised over child nodes.
    /// </summary>
    public static MeshData ComposeMesh(ICoreClientAPI capi, IAttachment node)
    {
        var baseMesh = AttachmentMesh.Tesselate(capi, node.Stack);
        if (baseMesh == null) return null;
        baseMesh = baseMesh.Clone();

        var points = node.Points;
        if (points == null || points.Count == 0) return baseMesh;

        var coll = node.Stack.Collectible;
        var baseComposite = AttachmentMesh.AttachedShapeComposite(coll) ?? GetDisplayShape(coll);
        var markers = AttachmentMesh.ReadSlots(capi, baseComposite?.Base?.ToString(), coll.Code.Domain);

        var mat = new Matrixf();
        foreach (var pt in points)
        {
            var child = node.GetAttached(pt.Code);
            if (child == null) continue;

            var childMesh = MeshFor(capi, child);
            if (childMesh == null) continue;
            childMesh = childMesh.Clone();

            // Anchor the child by its fixed model origin (not its bounds centre) so a container child
            // (a toolstrap) doesn't shift when its own children change, and asymmetric addons don't drift.
            var origin = AttachmentMesh.ModelOrigin(child.Stack.Collectible);
            var tf = pt.Transform.CombinedWith(AttachmentTransform.ForItem(child.Stack.Collectible, "placed"));

            float cx, cy, cz;
            if (markers.TryGetValue(pt.Code, out var marker))
            {
                // Anchor at the marker's pivot (origin), 16-unit -> [0,1].
                cx = marker.Origin.X / 16f; cy = marker.Origin.Y / 16f; cz = marker.Origin.Z / 16f;
                tf = AttachmentTransform.FromRotation(marker.Rotation).CombinedWith(tf);
            }
            else if (pt.Box != null)
            {
                // No shape marker: fall back to the point's own anchor (box centre unless it set one).
                cx = pt.Origin.X; cy = pt.Origin.Y; cz = pt.Origin.Z;
            }
            else continue;   // no marker and no box: nowhere to place

            float s = tf.Scale;
            mat.Identity()
                .Translate(cx, cy, cz)
                .RotateX(tf.Rotation[0] * D2R)
                .RotateY(tf.Rotation[1] * D2R)
                .RotateZ(tf.Rotation[2] * D2R)
                .Scale(s, s, s)
                .Translate(tf.Offset[0] - origin.X, tf.Offset[1] - origin.Y, tf.Offset[2] - origin.Z);
            childMesh.MatrixTransform(mat.Values);

            baseMesh.AddMeshData(childMesh);
        }
        return baseMesh;
    }

    // ---- shape helpers (lifted from ItemImmersiveBag; kept behaviour-identical) ----

    /// <summary>Loads a fresh, independent shape from a composite base path (host adapters use it to build a
    /// root base shape before composing children in).</summary>
    public static Shape LoadShape(ICoreAPI api, string basePath, string defaultDomain)
    {
        if (string.IsNullOrEmpty(basePath)) return null;
        var loc = AssetLocation.Create(basePath, defaultDomain)
            .CopyWithPathPrefixAndAppendixOnce("shapes/", ".json");
        return Shape.TryGet(api, loc.ToString());
    }

    private static CompositeShape GetDisplayShape(CollectibleObject collectible)
        => collectible switch
        {
            Item it => it.Shape,
            Block bl => bl.Shape,
            _ => null
        };

    private static void PrefixShape(Shape shape, string prefix)
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
                atta.CollectTextures(addonStack, addonShape, "", new Dictionary<string, CompositeTexture>());
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
        IDictionary<string, CompositeTexture> src = collectible switch
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
        Dictionary<string, AssetLocation> src, string prefix)
    {
        var dst = new Dictionary<string, AssetLocation>();
        if (src != null)
            foreach (var kv in src) dst[prefix + kv.Key] = kv.Value;
        return dst;
    }

    private static Dictionary<string, int[]> RekeySizes(Dictionary<string, int[]> src, string prefix)
    {
        var dst = new Dictionary<string, int[]>();
        if (src != null)
            foreach (var kv in src) dst[prefix + kv.Key] = kv.Value;
        return dst;
    }

    private static void MergeInto<T>(Dictionary<string, T> target, Dictionary<string, T> src)
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

    private static void Shift(double[] p, double[] delta)
    {
        if (p == null) return;
        p[0] -= delta[0]; p[1] -= delta[1]; p[2] -= delta[2];
    }

    private static Dictionary<string, (ShapeElement slot, ShapeElement parent)> FindSlotElements(ShapeElement[] roots)
    {
        var map = new Dictionary<string, (ShapeElement, ShapeElement)>();
        if (roots == null) return map;

        void Walk(ShapeElement el, ShapeElement parent)
        {
            if (el.Name != null && el.Name.StartsWith("slot_", StringComparison.OrdinalIgnoreCase))
                map[el.Name.Substring("slot_".Length)] = (el, parent);
            if (el.Children != null)
                foreach (var c in el.Children) Walk(c, el);
        }

        foreach (var r in roots) Walk(r, null);
        return map;
    }

    private static void AttachUnder(ShapeElement root, ShapeElement[] addonElements)
    {
        foreach (var el in addonElements)
        {
            el.StepParentName = null;
            el.ParentElement = root;
        }

        if (root.Children == null || root.Children.Length == 0)
        {
            root.Children = addonElements;
            return;
        }

        var merged = new ShapeElement[root.Children.Length + addonElements.Length];
        root.Children.CopyTo(merged, 0);
        addonElements.CopyTo(merged, root.Children.Length);
        root.Children = merged;
    }
}
