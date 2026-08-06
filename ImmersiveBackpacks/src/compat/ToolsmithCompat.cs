using System.Collections.Generic;
using ImmersiveBackpacks.attachments;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace ImmersiveBackpacks.compat;

/// <summary>
/// Rebuilds a Toolsmith tool's look as a Shape, so a tinkered tool on a worn toolstrap shows its real head,
/// handle and binding instead of the plain vanilla tool.
///
/// The placed/held paths get this for free from Toolsmith's own <c>IContainedMeshSource</c>, but the worn path
/// cannot: <c>IWearableShapeSupplier</c> returns a Shape, because worn geometry has to step-parent into the
/// entity's animated skeleton, and a mesh carries no such binding. Toolsmith's part tree references shape
/// assets rather than baked geometry, so the same parts can be composed as shape elements instead.
///
/// The tree is read straight off the stack, so there is no compile-time or runtime dependency on Toolsmith;
/// a stack without it (or a part whose shape has moved) yields null and the node falls back to its display
/// shape - the behaviour before this existed.
/// </summary>
public class ToolsmithCompat : ModSystem
{
    // Toolsmith's attribute keys (Toolsmith.Client.MultiPartRenderingHelpers). Baked into saved stacks, so
    // they cannot change without breaking Toolsmith's own saves.
    private const string MultiPartTree = "modularMultiPartRenderData";
    private const string PartTree = "modularPartRenderData";
    private const string ShapePathKey = "partShapeIndex";
    private const string ShapeOverrideKey = "shapeOverrideAppendTag";
    private const string TexturesKey = "partTextures";

    public override bool ShouldLoad(ICoreAPI api)
        => api.Side == EnumAppSide.Client && api.ModLoader.IsModEnabled("toolsmith");

    public override void StartClientSide(ICoreClientAPI api)
        => AttachmentComposer.StackShapeSources.Add(Compose);

    public override void Dispose()
        => AttachmentComposer.StackShapeSources.Remove(Compose);

    private static Shape Compose(ICoreAPI api, ItemStack stack)
    {
        var attributes = stack?.Attributes;
        if (attributes == null) return null;

        var elements = new List<ShapeElement>();
        var textures = new Dictionary<string, AssetLocation>();
        var sizes = new Dictionary<string, int[]>();

        if (attributes.GetTreeAttribute(MultiPartTree) is { Count: > 0 } multi)
        {
            int idx = 0;
            foreach (var entry in multi)
            {
                var part = multi.GetTreeAttribute(entry.Key);
                if (part == null) continue;
                // One missing part would render a headless tool, so bail out and let the vanilla shape stand.
                if (!AddPart(api, stack, part.GetTreeAttribute(PartTree), Offset(part), Rotation(part),
                        "ts" + idx++ + "_", elements, textures, sizes))
                    return null;
            }
        }
        else if (attributes.GetTreeAttribute(PartTree) is { } single)
        {
            if (!AddPart(api, stack, single, null, null, "ts_", elements, textures, sizes)) return null;
        }

        if (elements.Count == 0) return null;
        return new Shape { Elements = elements.ToArray(), Textures = textures, TextureSizes = sizes };
    }

    private static bool AddPart(ICoreAPI api, ItemStack stack, ITreeAttribute part, float[] offset,
        float[] rotation, string prefix, List<ShapeElement> elements,
        Dictionary<string, AssetLocation> textures, Dictionary<string, int[]> sizes)
    {
        string path = part?.GetString(ShapePathKey);
        if (string.IsNullOrEmpty(path)) return false;

        var shape = Shape.TryGet(api, path + part.GetString(ShapeOverrideKey) + ".json");
        if (shape?.Elements is not { Length: > 0 }) return false;

        ResolveTextures(stack, part, shape);
        if (offset != null)
            foreach (var el in shape.Elements) ShiftBy(el, offset);

        AttachmentComposer.PrefixShape(shape, prefix);
        foreach (var kv in shape.Textures) textures[kv.Key] = kv.Value;
        foreach (var kv in shape.TextureSizes) sizes[kv.Key] = kv.Value;

        if (rotation == null) elements.AddRange(shape.Elements);
        else elements.Add(RotateAboutOrigin(shape.Elements, rotation, prefix));
        return true;
    }

    /// <summary>
    /// The part's texture per code: the shape's own default, overridden by the tool item's (Toolsmith carries
    /// a head's metal that way), then by whatever the stack records for this part.
    /// </summary>
    private static void ResolveTextures(ItemStack stack, ITreeAttribute part, Shape shape)
    {
        var own = stack.Item?.Textures;
        var resolved = new Dictionary<string, AssetLocation>();
        foreach (var kv in shape.Textures ?? [])
            resolved[kv.Key] = own != null && own.TryGetValue(kv.Key, out var texture) ? texture.Base : kv.Value;

        if (part.GetTreeAttribute(TexturesKey) is { } partTextures)
            foreach (var entry in partTextures)
            {
                // A "-overlay" code blends onto its base; Shape.Textures holds one asset per code, so the
                // blend is dropped and the base texture shows unblended.
                if (entry.Key.Contains("-overlay")) continue;
                resolved[entry.Key] = new AssetLocation(partTextures.GetString(entry.Key) + ".png");
            }

        // Parts are separate shape files and may disagree on texture resolution, so pin each code's size
        // rather than leaning on the merged shape's single TextureWidth/Height.
        var sizes = new Dictionary<string, int[]>();
        foreach (var key in resolved.Keys)
            sizes[key] = shape.TextureSizes != null && shape.TextureSizes.TryGetValue(key, out var size)
                ? size
                : [shape.TextureWidth, shape.TextureHeight];

        shape.Textures = resolved;
        shape.TextureSizes = sizes;
    }

    // Toolsmith rotates a part's whole mesh about the model origin; an element wrapper anchored there is the
    // shape-space equivalent. Every code path in Toolsmith writes zeroes today, so this rarely fires.
    private static ShapeElement RotateAboutOrigin(ShapeElement[] children, float[] rotation, string prefix)
    {
        var wrapper = new ShapeElement
        {
            Name = prefix + "part",
            From = [0, 0, 0],
            To = [0, 0, 0],
            RotationOrigin = [0, 0, 0],
            RotationX = rotation[0],
            RotationY = rotation[1],
            RotationZ = rotation[2],
            Children = children,
            FacesResolved = new ShapeElementFace[6]
        };
        foreach (var el in children) el.ParentElement = wrapper;
        return wrapper;
    }

    // A child element's coordinates sit in its parent's frame, so shifting the roots moves each subtree whole.
    private static void ShiftBy(ShapeElement el, float[] offset)
    {
        Add(el.From, offset);
        Add(el.To, offset);
        Add(el.RotationOrigin, offset);
    }

    private static void Add(double[] point, float[] delta)
    {
        if (point == null) return;
        point[0] += delta[0]; point[1] += delta[1]; point[2] += delta[2];
    }

    // Toolsmith's offsets are in mesh units; shape elements are 16 to the block.
    private static float[] Offset(ITreeAttribute part)
    {
        float x = part.GetFloat("partOffsetX"), y = part.GetFloat("partOffsetY"), z = part.GetFloat("partOffsetZ");
        return x == 0 && y == 0 && z == 0 ? null : [x * 16f, y * 16f, z * 16f];
    }

    private static float[] Rotation(ITreeAttribute part)
    {
        float x = part.GetFloat("partRotationX"), y = part.GetFloat("partRotationY"), z = part.GetFloat("partRotationZ");
        return x == 0 && y == 0 && z == 0 ? null : [x, y, z];
    }
}
