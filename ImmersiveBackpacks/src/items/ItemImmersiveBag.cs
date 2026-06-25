using System;
using System.Collections.Generic;
using ImmersiveBackpacks.inventory;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

namespace ImmersiveBackpacks.items;

/// <summary>
/// Item class applied (via JSON patch) to worn bags that support immersive attachment points.
///
/// It replaces the manual <c>BackpackWornAttachmentRenderer</c>: instead of drawing addon item
/// meshes on the player's back every frame, it folds the attached addons into the worn bag's
/// entity shape so vanilla's gear-tesselation pipeline renders them as part of the player.
///
/// Two interfaces cooperate here:
///   * <see cref="IAttachableToEntity"/> — vanilla's <c>EntityBehaviorPlayerInventory.OnTesselation</c>
///     resolves this via <c>IAttachableToEntity.FromCollectible</c>, which returns the *first* match:
///     a collectible/behavior interface before the attribute fallback. By implementing it on the
///     item class we make sure our implementation is used instead of the default attribute-based one
///     (which would render the plain bag without addons).
///   * <see cref="IWearableShapeSupplier"/> — only checked as a direct cast on the collectible (never
///     on behaviors), so it must live on the item class. It lets us return a shape composed at
///     runtime (base bag shape + every attached addon positioned at its attachment point).
///
/// It also implements <see cref="IHeldBag"/> so a worn bag exposes its base slots PLUS the slots
/// contributed by each attached addon. Vanilla resolves the bag via
/// <c>GetCollectibleInterface&lt;IHeldBag&gt;()</c> (class before behavior), so this overrides the
/// vanilla <c>HeldBag</c> behavior's fixed slot count. Storage uses the same vanilla
/// <c>backpack.slots</c> tree as the placed block entity, so cargo round-trips between placed and worn.
/// </summary>
public class ItemImmersiveBag : Item, IAttachableToEntity, IWearableShapeSupplier, IHeldBag
{
    public int RequiresBehindSlots { get; set; }

    // ---- IHeldBag -----------------------------------------------------------

    public int GetQuantitySlots(ItemStack bagstack)
    {
        if (bagstack?.Collectible?.Attributes == null) return 0;
        return BuildLayout(bagstack).Length;
    }

    public List<ItemSlotBagContent> GetOrCreateSlots(ItemStack bagstack, InventoryBase parentinv,
        int bagIndex, IWorldAccessor world)
    {
        var specs = BuildLayout(bagstack);
        var slots = SlotsTree(bagstack, create: true);

        var list = new List<ItemSlotBagContent>(specs.Length);
        for (int i = 0; i < specs.Length; i++)
        {
            var slot = new ItemSlotBagFiltered(parentinv, bagIndex, i, specs[i]);
            string key = "slot-" + i;
            if (slots[key] is ItemstackAttribute { value: { } stored })
            {
                slot.Itemstack = stored;
                slot.Itemstack.ResolveBlockOrItem(world);
            }
            else
            {
                slots[key] = new ItemstackAttribute(null);
            }
            list.Add(slot);
        }
        return list;
    }

    public ItemStack[] GetContents(ItemStack bagstack, IWorldAccessor world)
    {
        var slots = SlotsTree(bagstack, create: false);
        if (slots == null) return null;

        int n = BuildLayout(bagstack).Length;
        var contents = new ItemStack[n];
        for (int i = 0; i < n; i++)
        {
            var stack = (slots["slot-" + i] as ItemstackAttribute)?.value;
            stack?.ResolveBlockOrItem(world);
            contents[i] = stack;
        }
        return contents;
    }

    public void Store(ItemStack bagstack, ItemSlotBagContent slot)
        => SlotsTree(bagstack, create: true)["slot-" + slot.SlotIndex]
            = new ItemstackAttribute(slot.Itemstack);

    public void Clear(ItemStack bagstack)
    {
        var backpack = bagstack.Attributes.GetTreeAttribute("backpack");
        if (backpack != null) backpack["slots"] = new TreeAttribute();
    }

    public bool IsEmpty(ItemStack bagstack)
    {
        var slots = SlotsTree(bagstack, create: false);
        if (slots == null) return true;
        foreach (var kv in slots)
            if ((kv.Value as ItemstackAttribute)?.value?.StackSize > 0) return false;
        return true;
    }

    public string GetSlotBgColor(ItemStack bagstack) => null;            // per-slot colours, see layout

    // Explicit: IHeldBag.GetStorageFlags (what the bag may contain) shares a signature with
    // CollectibleObject.GetStorageFlags (where this item itself may be stored - must stay Backpack so
    // it can be worn). Explicit implementation keeps the inherited virtual intact.
    EnumItemStorageFlags IHeldBag.GetStorageFlags(ItemStack bagstack)
        => (EnumItemStorageFlags)BackpackSlotLayout.DefaultStorageFlags;

    public TagSet GetStorageTags(ItemStack bagStack) => TagSet.Empty;

    /// <summary>Brightest light emitted by this bag's attached addons (for the worn-light behavior).</summary>
    public byte[] GetWornLight(ItemStack bagstack, IBlockAccessor blockAccessor)
        => BackpackLight.Brightest(ReadAddons(bagstack), blockAccessor);

    private BackpackSlotLayout.SlotSpec[] BuildLayout(ItemStack bagstack)
    {
        int baseSlots = Attributes?["backpack"]?["quantitySlots"]?.AsInt(0) ?? 0;
        return BackpackSlotLayout.Build(baseSlots, ReadAddons(bagstack));
    }

    private List<ItemStack> ReadAddons(ItemStack bagstack)
    {
        var result = new List<ItemStack>();
        var tree = bagstack.Attributes?.GetTreeAttribute("placed_addons");
        var points = Attributes?["immersiveBackpack"]?["attachmentPoints"];
        if (tree == null || points == null || !points.Exists) return result;

        foreach (var pt in points.AsArray())
        {
            string code = pt["code"].AsString();
            if (code == null) continue;
            var stack = tree.GetItemstack(code);
            if (stack == null) continue;
            stack.ResolveBlockOrItem(api.World);
            if (stack.Collectible != null) result.Add(stack);
        }
        return result;
    }

    private static ITreeAttribute SlotsTree(ItemStack bagstack, bool create)
    {
        var backpack = bagstack.Attributes.GetTreeAttribute("backpack");
        if (backpack == null)
        {
            if (!create) return null;
            backpack = new TreeAttribute();
            bagstack.Attributes["backpack"] = backpack;
        }
        var slots = backpack.GetTreeAttribute("slots");
        if (slots == null)
        {
            if (!create) return null;
            slots = new TreeAttribute();
            backpack["slots"] = slots;
        }
        return slots;
    }

    // ---- IAttachableToEntity ------------------------------------------------

    public bool IsAttachable(Entity toEntity, ItemStack itemStack) => toEntity is EntityPlayer;

    public string GetCategoryCode(ItemStack stack)
        => Attributes?["attachableToEntity"]?["categoryCode"]?.AsString("backpack") ?? "backpack";

    // Shape is supplied dynamically through IWearableShapeSupplier.GetShape, so no composite shape here.
    public CompositeShape GetAttachedShape(ItemStack stack, string slotCode) => null;

    public string[] GetDisableElements(ItemStack stack)
        => Attributes?["attachableToEntity"]?["disableElements"]?.AsArray<string>(null);

    public string[] GetKeepElements(ItemStack stack)
        => Attributes?["attachableToEntity"]?["keepElements"]?.AsArray<string>(null);

    public string GetTexturePrefixCode(ItemStack stack)
        => Attributes?["attachableToEntity"]?["texturePrefixCode"]?.AsString(null);

    public void CollectTextures(ItemStack stack, Shape shape, string texturePrefixCode,
        Dictionary<string, CompositeTexture> intoDict)
    {
        // The composed shape already carries every texture it references (base bag + addons). Register
        // them all so they land in the entity atlas. Texture codes in Textures are left unprefixed by
        // SubclassForStepParenting, while face codes get texturePrefixCode prepended - mirror that here.
        if (shape?.Textures == null) return;
        foreach (var kv in shape.Textures)
            intoDict[texturePrefixCode + kv.Key] = new CompositeTexture(kv.Value);
    }

    // ---- IWearableShapeSupplier --------------------------------------------

    Shape IWearableShapeSupplier.GetShape(ItemStack stack, Entity forEntity, string texturePrefixCode)
    {
        ICoreAPI capi = forEntity.World.Api;

        string baseShapePath = Attributes?["attachableToEntity"]?["attachedShape"]?["base"]?.AsString();
        Shape combined = LoadShape(capi, baseShapePath, Code.Domain);
        if (combined?.Elements == null || combined.Elements.Length == 0) return combined;

        // Addons hang off whichever root element is step-parented onto the body (e.g. "UpperTorso").
        ShapeElement root = FindStepParentRoot(combined.Elements) ?? combined.Elements[0];

        var addons = stack.Attributes?.GetTreeAttribute("placed_addons");
        if (addons != null)
            ComposeAddons(capi, stack, combined, root, addons);

        // IWearableShapeSupplier results are NOT step-parent-prepared by the caller, so do it here.
        combined.SubclassForStepParenting(texturePrefixCode, 0f);
        return combined;
    }

    private void ComposeAddons(ICoreAPI capi, ItemStack stack, Shape combined, ShapeElement root,
        ITreeAttribute addons)
    {
        // Reference box used to map an attachment point's [0,1] hitbox into the bag's local model space.
        double[] from = root.From ?? new double[3];
        double[] to = root.To ?? new double[3];

        int addonIndex = 0;
        var points = stack.Collectible.Attributes?["immersiveBackpack"]?["attachmentPoints"];
        if (points == null || !points.Exists) return;

        foreach (var pt in points.AsArray())
        {
            string code = pt["code"].AsString();
            if (code == null) continue;

            ItemStack addonStack = addons.GetItemstack(code);
            if (addonStack == null) continue;
            addonStack.ResolveBlockOrItem(capi.World);
            if (addonStack.Collectible == null) continue;

            var hb = pt["hitbox"].AsArray<float>();
            if (hb == null || hb.Length < 6) continue;

            CompositeShape addonComposite = GetDisplayShape(addonStack.Collectible);
            Shape addonShape = LoadShape(capi, addonComposite?.Base?.ToString(),
                addonStack.Collectible.Code.Domain);
            if (addonShape?.Elements == null || addonShape.Elements.Length == 0) continue;

            string sub = "ib" + addonIndex++ + "_";
            MergeAddonTextures(addonStack.Collectible, addonShape);
            PrefixShape(addonShape, sub);
            MergeInto(combined.Textures ??= new(), addonShape.Textures);
            MergeInto(combined.TextureSizes ??= new(), addonShape.TextureSizes);

            var wornTf = AttachmentTransform.FromJson(pt["worn"])
                .CombinedWith(AttachmentTransform.FromItem(addonStack.Collectible, "worn"));
            var wrapper = WrapAddon(addonShape.Elements, hb, from, to, wornTf);
            AttachUnder(root, new[] { wrapper });
        }
    }

    // ---- shape helpers ------------------------------------------------------

    private static Shape LoadShape(ICoreAPI capi, string basePath, string defaultDomain)
    {
        if (string.IsNullOrEmpty(basePath)) return null;

        var loc = AssetLocation.Create(basePath, defaultDomain)
            .CopyWithPathPrefixAndAppendixOnce("shapes/", ".json");

        // TryGet deserializes a fresh, independent shape each call, so the composition below can mutate
        // it freely. (ShapeElement.Clone only shallow-copies FacesResolved, so cloning + caching would
        // leak texture-code mutations back into a shared instance - hence no cache here.)
        // Item.Shape (a CompositeShape field) shadows the Shape type, so qualify the static call.
        return Vintagestory.API.Common.Shape.TryGet(capi, loc.ToString());
    }

    private static CompositeShape GetDisplayShape(CollectibleObject collectible)
        => collectible switch
        {
            Item it => it.Shape,
            Block bl => bl.Shape,
            _ => null
        };

    private static ShapeElement FindStepParentRoot(ShapeElement[] elements)
    {
        foreach (var el in elements)
            if (el.StepParentName != null) return el;
        return null;
    }

    /// <summary>Prefixes element names and face texture codes so merged addons never collide.</summary>
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

    private static void MergeAddonTextures(CollectibleObject collectible, Shape addonShape)
    {
        // The collectible's own "textures" override the shape file's textures for matching keys (this is
        // how a miningbag reuses the linensack shape but its own textures). So overwrite, not gap-fill.
        // Done before prefixing so PrefixShape re-keys shape and override textures together.
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

    // Worn-appearance tuning. AddonScale = addon size as a fraction of its own model size. WornSpread
    // pushes addon positions outward from the bag-body centre so they span the larger worn backpack
    // (1.0 = exactly the body box). AddonMargin = how far points may sit outside the body (model units).
    private const double AddonScale = 0.47;
    private const double WornSpread = 1.95;
    private const double AddonMargin = 1.5;
    // Global vertical lift for worn addons (body-box local Y, model units). Raises the whole cluster.
    private const double WornLift = 1.0;

    /// <summary>
    /// Wraps an addon's elements under a synthetic, face-less element placed at the attachment point and
    /// scaled down. VS applies a parent element's scale and translation to all descendants, so the addon
    /// renders as a single coherent shape — its parent-relative child elements must NOT be transformed
    /// individually (doing so scatters multi-element shapes). The wrapper becomes a child of the bag
    /// root, inheriting the bag's orientation on the player's back.
    /// </summary>
    private static ShapeElement WrapAddon(ShapeElement[] addonElements, float[] hb, double[] from,
        double[] to, AttachmentTransform tf)
    {
        // Centre the addon on its own origin. Only top-level elements move; children are relative and
        // follow their parent automatically.
        var (center, _) = AbsoluteBounds(addonElements);
        foreach (var el in addonElements)
        {
            Shift(el.From, center);
            Shift(el.To, center);
            Shift(el.RotationOrigin, center);
            el.StepParentName = null;
        }

        double[] baseOffset = AttachOffset(hb, from, to);
        double[] pos =
        {
            baseOffset[0] + tf.Offset[0],
            baseOffset[1] + tf.Offset[1],
            baseOffset[2] + tf.Offset[2]
        };
        double scale = AddonScale * tf.Scale;

        var wrapper = new ShapeElement
        {
            Name = "addon",
            From = (double[])pos.Clone(),
            To = (double[])pos.Clone(),
            RotationOrigin = (double[])pos.Clone(),
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

    /// <summary>
    /// Attachment point as an offset (in the bag root's local model box) for the wrapper. The hitbox
    /// [0,1] centre maps into the body box, then is expanded around the box centre by WornSpread so
    /// addons spread out to match the larger worn backpack.
    /// </summary>
    private static double[] AttachOffset(float[] hb, double[] from, double[] to)
    {
        float cx = (hb[0] + hb[3]) / 2f;
        float cy = (hb[1] + hb[4]) / 2f;
        float cz = (hb[2] + hb[5]) / 2f;
        double bw = to[0] - from[0], bh = to[1] - from[1], bd = to[2] - from[2];

        double x = Lerp(-AddonMargin, bw + AddonMargin, cx);
        double y = Lerp(-AddonMargin, bh + AddonMargin, cy);
        double z = Lerp(-AddonMargin, bd + AddonMargin, cz);
        return new[]
        {
            bw / 2 + (x - bw / 2) * WornSpread,
            bh / 2 + (y - bh / 2) * WornSpread + WornLift,
            bd / 2 + (z - bd / 2) * WornSpread
        };
    }

    private static void Shift(double[] p, double[] delta)
    {
        if (p == null) return;
        p[0] -= delta[0]; p[1] -= delta[1]; p[2] -= delta[2];
    }

    /// <summary>
    /// Absolute bounding box of an addon, accumulating parent-relative child offsets (child From/To are
    /// interpreted in the parent's local space, additive on the parent's From).
    /// </summary>
    private static (double[] center, double[] size) AbsoluteBounds(ShapeElement[] elements)
    {
        double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;

        void Walk(ShapeElement el, double bx, double by, double bz)
        {
            double fx = bx + (el.From?[0] ?? 0), fy = by + (el.From?[1] ?? 0), fz = bz + (el.From?[2] ?? 0);
            double tx = bx + (el.To?[0] ?? 0), ty = by + (el.To?[1] ?? 0), tz = bz + (el.To?[2] ?? 0);
            minX = Math.Min(minX, Math.Min(fx, tx)); minY = Math.Min(minY, Math.Min(fy, ty)); minZ = Math.Min(minZ, Math.Min(fz, tz));
            maxX = Math.Max(maxX, Math.Max(fx, tx)); maxY = Math.Max(maxY, Math.Max(fy, ty)); maxZ = Math.Max(maxZ, Math.Max(fz, tz));
            if (el.Children != null)
                foreach (var c in el.Children) Walk(c, fx, fy, fz);
        }

        foreach (var el in elements) Walk(el, 0, 0, 0);

        if (minX > maxX) return (new double[3], new double[3]);
        return (
            new[] { (minX + maxX) / 2, (minY + maxY) / 2, (minZ + maxZ) / 2 },
            new[] { maxX - minX, maxY - minY, maxZ - minZ });
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

    private static double Lerp(double a, double b, double t) => a + (b - a) * t;
}
