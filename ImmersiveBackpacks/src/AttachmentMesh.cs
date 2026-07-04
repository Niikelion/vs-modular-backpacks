using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace ImmersiveBackpacks;

/// <summary>
/// Shared addon-rendering helpers used by every place an attached addon is drawn: the placed block renderer
/// (per-frame draws) and the held/GUI item mesh (baked into one MultiTextureMeshRef). Keeps a single
/// definition of how an addon is tesselated, scaled and centred so placed and held bags look the same.
/// </summary>
public static class AttachmentMesh
{
    /// <summary>
    /// Tesselates an addon stack into its mesh. Blocks whose appearance depends on stack attributes (the
    /// lantern's metal/lining/glass) go through <see cref="IContainedMeshSource"/> so the attached variant
    /// renders, rather than <c>TesselateBlock</c> which always uses the block's default material.
    /// </summary>
    public static MeshData Tesselate(ICoreClientAPI capi, ItemStack stack)
    {
        if (stack?.Collectible == null) return null;
        if (stack.Item != null)
        {
            // An item may declare a separate, usually smaller, shape for when it's attached to a bag.
            var attached = ResolveAttachedShape(capi, stack.Item);
            if (attached != null)
            {
                capi.Tesselator.TesselateShape(stack.Item, attached, out var customMesh);
                return customMesh;
            }
            capi.Tesselator.TesselateItem(stack.Item, out var itemMesh);
            return itemMesh;
        }
        if (stack.Block is IContainedMeshSource cms)
            return cms.GenMesh(new DummySlot(stack), capi.BlockTextureAtlas, null);
        capi.Tesselator.TesselateBlock(stack.Block, out var blockMesh);
        return blockMesh;
    }

    /// <summary>
    /// The CompositeShape an addon renders with while attached to a bag, read from its
    /// <c>immersiveBackpackAttachment.attachedShape</c>, or null to fall back to the collectible's own
    /// display shape. Lets an addon look smaller/different when attached than on the ground or in the GUI.
    /// </summary>
    public static CompositeShape AttachedShapeComposite(CollectibleObject collectible)
    {
        var attr = collectible?.Attributes?["immersiveBackpackAttachment"]?["attachedShape"];
        if (attr == null || !attr.Exists) return null;
        var cs = attr.AsObject<CompositeShape>(null, collectible.Code.Domain);
        return string.IsNullOrEmpty(cs?.Base?.Path) ? null : cs;
    }

    private static Shape ResolveAttachedShape(ICoreClientAPI capi, CollectibleObject collectible)
    {
        var cs = AttachedShapeComposite(collectible);
        if (cs == null) return null;
        var loc = cs.Base.CopyWithPathPrefixAndAppendixOnce("shapes/", ".json");
        return Shape.TryGet(capi, loc.ToString());
    }

    /// <summary>
    /// A slot authored in a bag shape, resolved through its full ancestor transform chain.
    /// <see cref="Box"/> is the axis-aligned bounds of the (possibly rotated) slot, in raw 16-unit space;
    /// <see cref="Rotation"/> is the composed orientation as XYZ Euler degrees; <see cref="LocalSize"/> is
    /// the slot's own un-rotated box size (16-unit), used to fit an addon to the slot.
    /// </summary>
    public readonly struct SlotMarker
    {
        public readonly Cuboidf Box;
        public readonly float[] Rotation;
        public readonly float[] LocalSize;
        // The slot's placement anchor: its pivot (rotationOrigin) resolved through the ancestor chain, in raw
        // 16-unit space. This, not the box centre, is where an addon is anchored - so moving the pivot moves
        // the anchor, and it doesn't depend on the box's extent.
        public readonly Vec3f Origin;
        public SlotMarker(Cuboidf box, float[] rotation, float[] localSize, Vec3f origin)
        {
            Box = box; Rotation = rotation; LocalSize = localSize; Origin = origin;
        }
    }

    /// <summary>
    /// Reads attachment-slot markers authored directly in a bag shape: face-less elements named
    /// <c>slot_&lt;code&gt;</c> (invisible in-game), at any nesting depth. Each slot is resolved through the
    /// full transform chain of its ancestors (offsets, rotations and scales), exactly as VS composes
    /// element transforms, so a slot inherits a rotated parent group correctly. Returns the composed box,
    /// orientation and local size. Empty when the shape has no markers, so callers fall back.
    /// </summary>
    public static Dictionary<string, SlotMarker> ReadSlots(ICoreAPI api, string shapeBasePath, string domain)
    {
        var result = new Dictionary<string, SlotMarker>();
        if (string.IsNullOrEmpty(shapeBasePath)) return result;

        var loc = AssetLocation.Create(shapeBasePath, domain).CopyWithPathPrefixAndAppendixOnce("shapes/", ".json");
        var shape = Shape.TryGet(api, loc.ToString());
        if (shape?.Elements == null) return result;

        var identity = Mat4f.Create();
        Mat4f.Identity(identity);
        foreach (var el in shape.Elements) CollectSlots(el, identity, result);
        return result;
    }

    private static void CollectSlots(ShapeElement el, float[] parentMat, Dictionary<string, SlotMarker> result)
    {
        if (el == null) return;

        var world = Mat4f.Mul(Mat4f.Create(), parentMat, ElementMatrix(el));

        if (el.Name != null && el.Name.StartsWith("slot_", StringComparison.OrdinalIgnoreCase)
            && el.From is { Length: >= 3 } && el.To is { Length: >= 3 })
        {
            // The slot's box vertices are local 0..size; transform all 8 corners and take their AABB.
            float sx = (float)(el.To[0] - el.From[0]);
            float sy = (float)(el.To[1] - el.From[1]);
            float sz = (float)(el.To[2] - el.From[2]);
            float minX = float.MaxValue, minY = float.MaxValue, minZ = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue, maxZ = float.MinValue;
            for (int i = 0; i < 8; i++)
            {
                var v = Mat4f.MulWithVec4(world, (i & 1) == 0 ? 0 : sx, (i & 2) == 0 ? 0 : sy, (i & 4) == 0 ? 0 : sz, 1f);
                if (v[0] < minX) minX = v[0]; if (v[0] > maxX) maxX = v[0];
                if (v[1] < minY) minY = v[1]; if (v[1] > maxY) maxY = v[1];
                if (v[2] < minZ) minZ = v[2]; if (v[2] > maxZ) maxZ = v[2];
            }
            var box = new Cuboidf(minX, minY, minZ, maxX, maxY, maxZ);

            // Anchor = the slot's pivot (rotationOrigin) through the ancestor chain, independent of the box
            // extent. The element's own transform leaves its pivot fixed, so it's just parentMat * pivot.
            // Defaults to the box centre when no rotationOrigin is authored.
            float ox = el.RotationOrigin is { Length: >= 3 } ? (float)el.RotationOrigin[0] : (float)(el.From[0] + el.To[0]) / 2f;
            float oy = el.RotationOrigin is { Length: >= 3 } ? (float)el.RotationOrigin[1] : (float)(el.From[1] + el.To[1]) / 2f;
            float oz = el.RotationOrigin is { Length: >= 3 } ? (float)el.RotationOrigin[2] : (float)(el.From[2] + el.To[2]) / 2f;
            var ow = Mat4f.MulWithVec4(parentMat, ox, oy, oz, 1f);

            result[el.Name.Substring("slot_".Length)] =
                new SlotMarker(box, ExtractEuler(world), new[] { sx, sy, sz }, new Vec3f(ow[0], ow[1], ow[2]));
        }

        if (el.Children != null)
            foreach (var c in el.Children) CollectSlots(c, world, result);
    }

    // Replicates VS ShapeElement.GetLocalTransformMatrix (non-anim) in raw 16-unit units (no /16):
    // T(rotationOrigin) * RotateByXYZ * Scale * T(From - rotationOrigin).
    private static float[] ElementMatrix(ShapeElement el)
    {
        var m = Mat4f.Create();
        Mat4f.Identity(m);
        float ox = el.RotationOrigin is { Length: >= 3 } ? (float)el.RotationOrigin[0] : 0f;
        float oy = el.RotationOrigin is { Length: >= 3 } ? (float)el.RotationOrigin[1] : 0f;
        float oz = el.RotationOrigin is { Length: >= 3 } ? (float)el.RotationOrigin[2] : 0f;
        Mat4f.Translate(m, ox, oy, oz);
        Mat4f.RotateByXYZ(m, (float)(el.RotationX * Math.PI / 180.0), (float)(el.RotationY * Math.PI / 180.0), (float)(el.RotationZ * Math.PI / 180.0));
        Mat4f.Scale(m, (float)el.ScaleX, (float)el.ScaleY, (float)el.ScaleZ);
        float fx = (el.From is { Length: >= 3 } ? (float)el.From[0] : 0f) - ox;
        float fy = (el.From is { Length: >= 3 } ? (float)el.From[1] : 0f) - oy;
        float fz = (el.From is { Length: >= 3 } ? (float)el.From[2] : 0f) - oz;
        Mat4f.Translate(m, fx, fy, fz);
        return m;
    }

    // Extracts XYZ Euler degrees from a (column-major) matrix whose rotation part was built by RotateByXYZ.
    private static float[] ExtractEuler(float[] m)
    {
        float r2d = 180f / (float)Math.PI;
        float sy = m[8] < -1f ? -1f : (m[8] > 1f ? 1f : m[8]);
        float ay = (float)Math.Asin(sy);
        float ax, az;
        if (Math.Abs(Math.Cos(ay)) > 1e-4)
        {
            ax = (float)Math.Atan2(-m[9], m[10]);
            az = (float)Math.Atan2(-m[4], m[0]);
        }
        else
        {
            ax = (float)Math.Atan2(m[6], m[5]);
            az = 0f;
        }
        return new[] { ax * r2d, ay * r2d, az * r2d };
    }

    /// <summary>Local-space centre and size of a tesselated addon mesh.</summary>
    public static (Vec3f center, Vec3f size) Bounds(MeshData mesh)
    {
        float minX = float.MaxValue, minY = float.MaxValue, minZ = float.MaxValue;
        float maxX = float.MinValue, maxY = float.MinValue, maxZ = float.MinValue;

        var xyz = mesh.xyz;
        int n = mesh.VerticesCount * 3;
        for (int i = 0; i + 2 < n; i += 3)
        {
            float x = xyz[i], y = xyz[i + 1], z = xyz[i + 2];
            if (x < minX) minX = x; if (y < minY) minY = y; if (z < minZ) minZ = z;
            if (x > maxX) maxX = x; if (y > maxY) maxY = y; if (z > maxZ) maxZ = z;
        }

        if (minX > maxX) return (new Vec3f(0.5f, 0.5f, 0.5f), new Vec3f(1f, 1f, 1f));
        return (
            new Vec3f((minX + maxX) / 2f, (minY + maxY) / 2f, (minZ + maxZ) / 2f),
            new Vec3f(maxX - minX, maxY - minY, maxZ - minZ));
    }

    /// <summary>
    /// The addon's model origin in block space — the fixed point on the addon that is placed AT the attachment
    /// point's anchor, INSTEAD of the addon's geometry bounding-box centre. Being fixed (not derived from the
    /// mesh) it is content-independent: adding tools to a toolstrap does not move the toolstrap, and asymmetric
    /// models don't need the offset re-tuned per model. Defaults to the floor centre of the cube — (0.5,0,0.5)
    /// = (8,0,8) in 16-unit space — so an addon sits horizontally centred with its base on the point. An addon
    /// authored around a different origin overrides it via <c>immersiveBackpackAttachment.origin</c> (block-space).
    /// </summary>
    public static Vec3f ModelOrigin(CollectibleObject collectible)
    {
        var arr = collectible?.Attributes?["immersiveBackpackAttachment"]?["origin"]?.AsArray<float>(null);
        return arr is { Length: >= 3 } ? new Vec3f(arr[0], arr[1], arr[2]) : new Vec3f(0.5f, 0f, 0.5f);
    }
}
