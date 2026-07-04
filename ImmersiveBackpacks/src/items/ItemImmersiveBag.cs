using System;
using System.Collections.Generic;
using ImmersiveBackpacks.attachments;
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
        var key = (HeldMeshKey(points, addons, itemstack), mirror: target == EnumItemRenderTarget.Gui);
        if (!heldMeshCache.TryGetValue(key, out var meshRef))
            heldMeshCache[key] = meshRef = BuildHeldMesh(capi, itemstack, key.mirror);

        if (meshRef != null) renderinfo.ModelRef = meshRef;
    }

    private MultiTextureMeshRef BuildHeldMesh(ICoreClientAPI capi, ItemStack itemstack, bool mirror)
    {
        // Base bag mesh + each addon composed at its marker, in item-model space ([0,1]). The shared composer
        // is the single source of this (the placed block and worn shape route through the same core).
        var body = AttachmentComposer.ComposeMesh(capi, BagNodeFor(itemstack));
        if (body == null) return null;

        // GUI target: the inventory projection is horizontally flipped vs the world, so mirror the whole
        // composed mesh across its own X centre. The GUI flip then cancels it and addons land on the same
        // side as the placed block (and the in-hand/ground renders, which use the unmirrored mesh).
        if (mirror)
        {
            var (c, _) = AttachmentMesh.Bounds(body);
            var mat = new Matrixf();
            mat.Identity().Translate(c.X, 0f, 0f).Scale(-1f, 1f, 1f).Translate(-c.X, 0f, 0f);
            body.MatrixTransform(mat.Values);
        }

        return capi.Render.UploadMultiTextureMesh(body);
    }

    // Builds the composer's node view of this bag stack: points from the immersiveBackpack.attachmentPoints
    // config, occupants read from placed_addons. A slot-bearing addon (a toolstrap) also gets the run of
    // unified cargo (backpack.slots) it owns, so its tools render — resolved here since only the bag knows the
    // layout. Point order matches BackpackSlotLayout, so the cargo ranges line up.
    private IAttachment BagNodeFor(ItemStack stack)
    {
        var pts = new List<IAttachmentPoint>();
        var orderedAddons = new List<ItemStack>();
        var addonsTree = stack.Attributes?.GetTreeAttribute("placed_addons");
        var points = Attributes?["immersiveBackpack"]?["attachmentPoints"];
        if (points != null && points.Exists)
            foreach (var pt in points.AsArray())
            {
                string code = pt["code"].AsString();
                if (code == null) continue;
                var cats = pt["categories"].AsArray<string>();
                Cuboidf box = null;
                var hb = pt["hitbox"].AsArray<float>();
                if (hb != null && hb.Length >= 6)
                    box = new Cuboidf(hb[0], hb[1], hb[2], hb[3], hb[4], hb[5]);
                pts.Add(new AttachmentPointSpec(code, cats, box, AttachmentTransform.FromJson(pt["placed"])));
                orderedAddons.Add(addonsTree?.GetItemstack(code));
            }

        int baseSlots = Attributes?["backpack"]?["quantitySlots"]?.AsInt(0) ?? 0;
        var ranges = BackpackSlotLayout.AddonRanges(baseSlots, orderedAddons);
        var cargo = SlotsTree(stack, create: false);
        var toolsByPoint = new Dictionary<string, IReadOnlyList<ItemStack>>();
        for (int i = 0; i < pts.Count; i++)
        {
            var (off, count) = ranges[i];
            if (count <= 0) continue;
            var owned = new List<ItemStack>(count);
            for (int k = 0; k < count; k++)
            {
                var s = (cargo?["slot-" + (off + k)] as ItemstackAttribute)?.value;
                s?.ResolveBlockOrItem(api.World);
                owned.Add(s);
            }
            toolsByPoint[pts[i].Code] = owned;
        }

        return new BagAttachment(stack, pts, toolsByPoint, api.World);
    }

    // Order- and position-sensitive cache key over the bag's attachment points: each point's code plus the
    // content hash of the addon stored there, mixed multiplicatively. TreeAttribute.GetHashCode() XORs its
    // entries, so it collides when two addons are swapped between points (same set, different placement) -
    // walking the points in order avoids that. Also folds the unified cargo (backpack.slots) hash, because a
    // slot-bearing addon (toolstrap) renders its cargo tools - so a tool change must rebuild the held mesh.
    // (Coarse: any cargo edit rebuilds; cargo edits are user-driven and infrequent.)
    private static int HeldMeshKey(JsonObject points, ITreeAttribute addons, ItemStack bagstack)
    {
        int key = 17;
        foreach (var pt in points.AsArray())
        {
            string code = pt["code"].AsString();
            if (code == null) continue;
            key = key * 31 + code.GetHashCode();
            key = key * 31 + (addons.GetItemstack(code)?.GetHashCode() ?? 0);
        }
        key = key * 31 + (SlotsTree(bagstack, create: false)?.GetHashCode() ?? 0);
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

        // The worn root loads its OWN base shape (attachableToEntity.attachedShape, which the composer's
        // per-node display-shape path doesn't know about), then the shared composer attaches every addon
        // under its slot marker - identical child-composition to the placed/held mesh path.
        string baseShapePath = Attributes?["attachableToEntity"]?["attachedShape"]?["base"]?.AsString();
        Shape combined = AttachmentComposer.LoadShape(capi, baseShapePath, Code.Domain);
        if (combined?.Elements == null || combined.Elements.Length == 0) return combined;

        AttachmentComposer.ComposeChildrenInto(capi, combined, BagNodeFor(stack));

        // IWearableShapeSupplier results are NOT step-parent-prepared by the caller, so do it here.
        combined.SubclassForStepParenting(texturePrefixCode, 0f);
        return combined;
    }
}
