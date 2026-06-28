using System;
using System.Collections.Generic;
using ImmersiveBackpacks.inventory;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
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

    // Composed held/GUI meshes (base bag + attached addons), keyed by (addon-placement hash, mirrored).
    // The mirrored variant is for the GUI target only: the inventory renders with a horizontally-flipped
    // projection vs the world, so without it a left-slot addon shows on the right in the inventory while
    // being correct on the placed block / in hand. Tesselated and uploaded once per key; disposed in OnUnloaded.
    private readonly Dictionary<(int hash, bool mirror), MultiTextureMeshRef> heldMeshCache = new();

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

    /// <summary>
    /// Brightest light emitted by this bag's attached addons. Vanilla queries this on the active
    /// hand slots in <c>EntityPlayer.LightHsv</c>, so overriding it makes a held bag with a light addon
    /// (e.g. an attached torch) light the player — the same source the worn behavior and placed block use.
    /// </summary>
    public override byte[] GetLightHsv(IBlockAccessor blockAccessor, BlockPos pos, ItemStack stack = null)
    {
        if (stack == null) return base.GetLightHsv(blockAccessor, pos, stack);
        var light = BackpackLight.Brightest(ReadAddons(stack), blockAccessor);
        return light ?? base.GetLightHsv(blockAccessor, pos, stack);
    }

    /// <summary>Brightest addon light, for the worn-light behavior (no block position).</summary>
    public byte[] GetWornLight(ItemStack bagstack, IBlockAccessor blockAccessor)
        => GetLightHsv(blockAccessor, null, bagstack);

    // ---- held / GUI rendering ----------------------------------------------

    /// <summary>
    /// Renders attached addons on the bag item itself (inventory, ground, hand) by swapping in a composed
    /// mesh: the base bag plus every addon, each tesselated into its own atlas and merged into a single
    /// <see cref="MultiTextureMeshRef"/> (so item-atlas pouches and the block-atlas lantern both render
    /// correctly). The worn bag is composed separately at the shape level via IWearableShapeSupplier; the
    /// placed block draws the same addons per-frame in its renderer — this is the third, item-space path.
    /// Composed meshes are cached per attachment configuration.
    /// </summary>
    public override void OnBeforeRender(ICoreClientAPI capi, ItemStack itemstack,
        EnumItemRenderTarget target, ref ItemRenderInfo renderinfo)
    {
        base.OnBeforeRender(capi, itemstack, target, ref renderinfo);

        var addons = itemstack.Attributes?.GetTreeAttribute("placed_addons");
        var points = Attributes?["immersiveBackpack"]?["attachmentPoints"];
        if (addons == null || addons.Count == 0 || points == null || !points.Exists) return;

        // Keyed by addon placement (point code + addon content hash, in point order) plus whether this is the
        // mirrored GUI variant, so per-frame cost is a single cheap hash + dictionary lookup; the actual
        // tesselation/upload happens once per configuration.
        var key = (HeldMeshKey(points, addons), mirror: target == EnumItemRenderTarget.Gui);
        if (!heldMeshCache.TryGetValue(key, out var meshRef))
            heldMeshCache[key] = meshRef = BuildHeldMesh(capi, points, addons, key.mirror);

        if (meshRef != null) renderinfo.ModelRef = meshRef;
    }

    private MultiTextureMeshRef BuildHeldMesh(ICoreClientAPI capi, JsonObject points, ITreeAttribute addons,
        bool mirror)
    {
        capi.Tesselator.TesselateItem(this, out MeshData body);
        if (body == null) return null;
        // TesselateItem can hand back the shared static placeholder (unknownItemModelData) when the bag's
        // own shape/texture is missing. We accumulate addon faces into this mesh below, so clone first to
        // avoid corrupting that shared instance for every other unknown item in the game.
        body = body.Clone();

        const float d2r = (float)(Math.PI / 180.0);
        var mat = new Matrixf();

        // Slot markers from the bag's own item shape position addons here too (matching the placed block):
        // box centre (16-unit -> [0,1]) is the anchor and the marker rotation seeds the placed transform.
        var shapeSlots = AttachmentMesh.ReadSlots(capi, Shape?.Base?.ToString(), Code.Domain);

        foreach (var pt in points.AsArray())
        {
            string code = pt["code"].AsString();
            if (code == null) continue;

            var addonStack = addons.GetItemstack(code);
            if (addonStack == null) continue;
            addonStack.ResolveBlockOrItem(capi.World);
            if (addonStack.Collectible == null) continue;

            // Items tesselate into the item atlas, blocks (lantern) into the block atlas, each via the variant
            // that honours stack appearance (the lantern's metal). The tesselator tags every face with its
            // real atlas-page texture id, so AddMeshData folds both atlases (and any multi-page texture) into
            // one multi-texture mesh with no manual atlas bookkeeping.
            MeshData addonMesh = AttachmentMesh.Tesselate(capi, addonStack);
            if (addonMesh == null) continue;
            // Same shared-placeholder concern as the body, plus we MatrixTransform in place below.
            addonMesh = addonMesh.Clone();

            var (center, _) = AttachmentMesh.Bounds(addonMesh);
            var tf = AttachmentTransform.FromJson(pt["placed"])
                .CombinedWith(AttachmentTransform.ForItem(addonStack.Collectible, "placed"));

            float cx, cy, cz;
            if (shapeSlots.TryGetValue(code, out var marker))
            {
                var b = marker.Box;
                cx = (b.X1 + b.X2) / 32f; cy = (b.Y1 + b.Y2) / 32f; cz = (b.Z1 + b.Z2) / 32f;
                tf = AttachmentTransform.FromRotation(marker.Rotation).CombinedWith(tf);
            }
            else
            {
                var hbArr = pt["hitbox"].AsArray<float>();
                if (hbArr == null || hbArr.Length < 6) continue;
                cx = (hbArr[0] + hbArr[3]) / 2f; cy = (hbArr[1] + hbArr[4]) / 2f; cz = (hbArr[2] + hbArr[5]) / 2f;
            }
            float scale = tf.Scale;

            // Item-model space (the bag mesh is in [0,1], same as the block hitbox): position the addon at
            // its hitbox centre, then the point's placed transform + item override, then fit-scale, then
            // centre the addon mesh on its own origin. Mirrors the placed renderer minus the world/block
            // transform. Vanilla then applies the item's gui/fp/tp/ground ModelTransform on top.
            // Addon rotation comes from the composed shape slot (X,Y,Z order, as authored). The offset is
            // applied after the rotation so it follows the addon's local axes (matches placed/worn).
            mat.Identity()
                .Translate(cx, cy, cz)
                .RotateX(tf.Rotation[0] * d2r)
                .RotateY(tf.Rotation[1] * d2r)
                .RotateZ(tf.Rotation[2] * d2r)
                .Scale(scale, scale, scale)
                .Translate(tf.Offset[0] - center.X, tf.Offset[1] - center.Y, tf.Offset[2] - center.Z);
            addonMesh.MatrixTransform(mat.Values);

            body.AddMeshData(addonMesh);
        }

        // GUI target: the inventory projection is horizontally flipped vs the world, so mirror the whole
        // composed mesh across its own X centre. The GUI flip then cancels it and addons land on the same
        // side as the placed block (and the in-hand/ground renders, which use the unmirrored mesh).
        if (mirror)
        {
            var (c, _) = AttachmentMesh.Bounds(body);
            mat.Identity().Translate(c.X, 0f, 0f).Scale(-1f, 1f, 1f).Translate(-c.X, 0f, 0f);
            body.MatrixTransform(mat.Values);
        }

        return capi.Render.UploadMultiTextureMesh(body);
    }

    // Order- and position-sensitive cache key over the bag's attachment points: each point's code plus the
    // content hash of the addon stored there, mixed multiplicatively. TreeAttribute.GetHashCode() XORs its
    // entries, so it collides when two addons are swapped between points (same set, different placement) -
    // walking the points in order avoids that.
    private static int HeldMeshKey(JsonObject points, ITreeAttribute addons)
    {
        int key = 17;
        foreach (var pt in points.AsArray())
        {
            string code = pt["code"].AsString();
            if (code == null) continue;
            key = key * 31 + code.GetHashCode();
            key = key * 31 + (addons.GetItemstack(code)?.GetHashCode() ?? 0);
        }
        return key;
    }

    public override void OnUnloaded(ICoreAPI api)
    {
        foreach (var meshRef in heldMeshCache.Values) meshRef?.Dispose();
        heldMeshCache.Clear();
        base.OnUnloaded(api);
    }

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
        int addonIndex = 0;
        var points = stack.Collectible.Attributes?["immersiveBackpack"]?["attachmentPoints"];
        if (points == null || !points.Exists) return;

        // Each addon attaches under its slot_<code> marker's own parent in the composed shape, inheriting
        // the full ancestor transform - including the worn bag's scale-up - so it rides the same transform
        // as the bag. The slot supplies position + orientation; the addon's own attachedTransform supplies
        // its size (there is no per-slot scale and no auto-fit). Slots without a marker render nothing.
        var slotElems = FindSlotElements(combined.Elements);

        foreach (var pt in points.AsArray())
        {
            string code = pt["code"].AsString();
            if (code == null) continue;
            if (!slotElems.TryGetValue(code, out var s) || s.parent == null) continue;

            ItemStack addonStack = addons.GetItemstack(code);
            if (addonStack == null) continue;
            addonStack.ResolveBlockOrItem(capi.World);
            if (addonStack.Collectible == null) continue;

            // Prefer the addon's attached-specific shape (usually smaller than its on-ground shape).
            CompositeShape addonComposite = AttachmentMesh.AttachedShapeComposite(addonStack.Collectible)
                ?? GetDisplayShape(addonStack.Collectible);
            Shape addonShape = LoadShape(capi, addonComposite?.Base?.ToString(),
                addonStack.Collectible.Code.Domain);
            if (addonShape?.Elements == null || addonShape.Elements.Length == 0) continue;

            string sub = "ib" + addonIndex++ + "_";
            ApplyAddonTextures(addonStack, addonShape);
            PrefixShape(addonShape, sub);
            MergeInto(combined.Textures ??= new(), addonShape.Textures);
            MergeInto(combined.TextureSizes ??= new(), addonShape.TextureSizes);

            // Centre the addon in the slot box (local to the slot's parent), apply the slot's own rotation
            // and the addon's attachedTransform (scale/offset/rotation); the parent inherits the rest.
            var slot = s.slot;
            double[] slotCenter =
            {
                (slot.From[0] + slot.To[0]) / 2.0,
                (slot.From[1] + slot.To[1]) / 2.0,
                (slot.From[2] + slot.To[2]) / 2.0
            };
            var slotRot = new[] { (float)slot.RotationX, (float)slot.RotationY, (float)slot.RotationZ };
            var tf = AttachmentTransform.FromRotation(slotRot)
                .CombinedWith(AttachmentTransform.FromItem(addonStack.Collectible, "attachedTransform"));
            var wrapper = WrapAddon(addonShape.Elements, slotCenter, tf);
            AttachUnder(s.parent, new[] { wrapper });
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

    // Populate the addon shape's texture codes for the composed worn shape. Addons that texture themselves
    // from stack attributes (the lantern's metal/lining/glass) do it through IAttachableToEntity.CollectTextures
    // so the worn render shows the attached variant, not the block's default material. Plain addons (the
    // sacks) just take their collectible textures.
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
                // Some addons (the lantern) read variant attributes (material/lining/glass) off the stack
                // and throw when they're missing. Don't let that fail the whole worn tesselation (which
                // would make the player invisible) - fall back to the collectible's own textures.
            }
        }
        MergeAddonTextures(addonStack.Collectible, addonShape);
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

    /// <summary>
    /// Wraps an addon's elements under one synthetic, face-less element placed at the slot, carrying the
    /// addon's position, rotation and scale. VS applies a parent element's transform to all descendants, so
    /// the addon renders as one coherent shape - its parent-relative child elements must NOT be transformed
    /// individually. The wrapper is attached under the slot's parent, inheriting the rest of the chain, and
    /// applies its rotation in VS's element order (X, Y, Z) - matching how the slot was authored.
    /// </summary>
    private static ShapeElement WrapAddon(ShapeElement[] addonElements, double[] slotCenter,
        AttachmentTransform tf)
    {
        // Centre the addon on its own origin, then displace it by the offset so the wrapper's rotation
        // carries it. tf.Offset is authored in [0,1] block fractions (same basis as placed/held); ×16 for
        // 16-unit worn space. Applying it here (inside the wrapper) makes it a LOCAL-space offset that
        // follows the addon's orientation, rather than moving the wrapper along the bag's axes.
        var (center, _) = AbsoluteBounds(addonElements);
        double[] shift =
        {
            center[0] - tf.Offset[0] * 16.0,
            center[1] - tf.Offset[1] * 16.0,
            center[2] - tf.Offset[2] * 16.0
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
            From = (double[])slotCenter.Clone(),
            To = (double[])slotCenter.Clone(),
            RotationOrigin = (double[])slotCenter.Clone(),
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

    // Maps slot code -> (slot element, its parent) across a composed shape tree, so an addon can be
    // attached under the slot's parent and inherit the full ancestor transform chain.
    private static Dictionary<string, (ShapeElement slot, ShapeElement parent)> FindSlotElements(ShapeElement[] roots)
    {
        var map = new Dictionary<string, (ShapeElement, ShapeElement)>();
        if (roots == null) return map;

        void Walk(ShapeElement el, ShapeElement parent)
        {
            if (el.Name != null && el.Name.StartsWith("slot_", System.StringComparison.OrdinalIgnoreCase))
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
