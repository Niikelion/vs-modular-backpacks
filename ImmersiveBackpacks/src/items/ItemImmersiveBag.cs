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

    // Vanilla's attribute-driven IAttachableToEntity, built here purely to reuse its worn-shape lookup
    // (see GetShape). FromCollectible prefers our interface implementation over this one, so vanilla never
    // constructs it for our bag - we must, or we lose the attachedShape resolution it does for free.
    private AttributeAttachableToEntity attributeAttachable;

    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);
        attributeAttachable =
            Attributes?["attachableToEntity"].AsObject<AttributeAttachableToEntity>(null, Code.Domain);
    }

    /// <summary>
    /// Cross-mod contract with Deven's "Immersive Backpacks": when the player selects a worn bag to hold it, that
    /// mod sets this attribute on the stack and expects the worn shape hidden (so it isn't drawn on the back AND
    /// in hand). It hides bags via the vanilla attribute/behaviour shape providers, which never see our
    /// interface-based backpack - so we honour the flag ourselves, in both shape sources. Absent that mod the
    /// flag is never set, so this is a no-op and we render like vanilla.
    /// </summary>
    private static bool HiddenWhileSelected(ItemStack stack)
        => stack?.Attributes?.GetInt("immersiveBackpacksHideAttachmentWhileSelected") == 1;

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
            var slot = BackpackSlotLayout.CreateBagSlot(parentinv, bagIndex, i, specs[i]);
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
    /// (e.g., an attached torch) light the player — the same source the worn behavior and placed block use.
    /// </summary>
    public override byte[] GetLightHsv(IBlockAccessor blockAccessor, BlockPos pos, ItemStack stack = null)
    {
        if (stack == null) return base.GetLightHsv(blockAccessor, pos);
        byte[] light = BackpackLight.Brightest(ReadAddons(stack), blockAccessor);
        return light ?? base.GetLightHsv(blockAccessor, pos, stack);
    }

    /// <summary>Brightest addon light, for the worn-light behavior (no block position).</summary>
    public byte[] GetWornLight(ItemStack bagStack, IBlockAccessor blockAccessor)
        => GetLightHsv(blockAccessor, null, bagStack);

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
        var points = Attributes?["immersiveBackpack"]["attachmentPoints"];
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
    internal IAttachment BagNodeFor(ItemStack stack)
    {
        var pts = new List<IAttachmentPoint>();
        var orderedAddons = new List<ItemStack>();
        var addonsTree = stack.Attributes?.GetTreeAttribute("placed_addons");
        var points = Attributes?["immersiveBackpack"]["attachmentPoints"];
        if (points is { Exists: true })
            foreach (var pt in points.AsArray() ?? [])
            {
                string code = pt["code"].AsString();
                if (code == null) continue;
                string[] cats = pt["categories"].AsArray<string>();
                Cuboidf box = null;
                float[] hb = pt["hitbox"].AsArray<float>();
                if (hb is { Length: >= 6 })
                    box = new(hb[0], hb[1], hb[2], hb[3], hb[4], hb[5]);
                pts.Add(new AttachmentPointSpec(code, cats, box, AttachmentTransform.FromJson(pt["placed"])));
                // Resolve before it feeds AddonRanges: an unresolved stack has a null Collectible, so
                // AddonSlotCount reports 0 slots and a slot-bearing addon (toolstrap) would own no cargo
                // range - its tools would silently drop out of the held/GUI mesh.
                var addonStack = addonsTree?.GetItemstack(code);
                addonStack?.ResolveBlockOrItem(api.World);
                orderedAddons.Add(addonStack);
            }

        int baseSlots = Attributes?["backpack"]["quantitySlots"].AsInt() ?? 0;
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
    private static int HeldMeshKey(JsonObject points, ITreeAttribute addons, ItemStack bagStack)
    {
        int key = 17;
        foreach (var pt in points.AsArray() ?? [])
        {
            string code = pt["code"].AsString();
            if (code == null) continue;
            key = key * 31 + code.GetHashCode();
            key = key * 31 + (addons.GetItemstack(code)?.GetHashCode() ?? 0);
        }
        // Position-sensitive cargo hash: a slot-bearing addon (toolstrap) renders its cargo tools, and a tool
        // moving between slots must rebuild the mesh - the raw tree hash XORs entries and misses that.
        key = key * 31 + BackpackSlotLayout.CargoHash(SlotsTree(bagStack, create: false));
        // Live /tfedit tuning changes a transform without touching placement or contents, so fold it in too.
        key = key * 31 + AttachmentTransform.TuningGeneration;
        return key;
    }

    public override void OnUnloaded(ICoreAPI api)
    {
        foreach (var meshRef in heldMeshCache.Values) meshRef?.Dispose();
        heldMeshCache.Clear();
        base.OnUnloaded(api);
    }

    private BackpackSlotLayout.SlotSpec[] BuildLayout(ItemStack bagStack)
    {
        int baseSlots = Attributes?["backpack"]["quantitySlots"].AsInt() ?? 0;
        return BackpackSlotLayout.Build(baseSlots, ReadAddons(bagStack));
    }

    private List<ItemStack> ReadAddons(ItemStack bagStack)
    {
        var result = new List<ItemStack>();
        var tree = bagStack.Attributes?.GetTreeAttribute("placed_addons");
        var points = Attributes?["immersiveBackpack"]["attachmentPoints"];
        if (tree == null || points is not { Exists: true }) return result;

        foreach (var pt in points.AsArray() ?? [])
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

    private static ITreeAttribute SlotsTree(ItemStack bagStack, bool create)
    {
        var backpack = bagStack.Attributes.GetTreeAttribute("backpack");
        if (backpack == null)
        {
            if (!create) return null;
            backpack = new TreeAttribute();
            bagStack.Attributes["backpack"] = backpack;
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

    // Unconditional, like vanilla's AttributeAttachableToEntity: a host with a bag slot (an Equus horse) may
    // carry the bag too, and GetShape poses it for whichever slot it lands in.
    public bool IsAttachable(Entity toEntity, ItemStack itemStack) => true;

    public string GetCategoryCode(ItemStack stack)
        => Attributes?["attachableToEntity"]["categoryCode"].AsString("backpack") ?? "backpack";

    // Vanilla's per-slot shape lookup (attachedShape, else a wildcard match in attachedShapeBySlotCode, else the
    // held shape). GetShape calls it to pick the base it composes addons onto, so this stays the single place a
    // slot code turns into a model. Vanilla itself only calls it when GetShape returns null - i.e. when we could
    // not identify the host's slot - and then renders it uncomposed.
    public CompositeShape GetAttachedShape(ItemStack stack, string slotCode)
        => HiddenWhileSelected(stack) ? null : attributeAttachable?.GetAttachedShape(stack, slotCode);

    public string[] GetDisableElements(ItemStack stack)
        => Attributes?["attachableToEntity"]["disableElements"].AsArray<string>();

    public string[] GetKeepElements(ItemStack stack)
        => Attributes?["attachableToEntity"]["keepElements"].AsArray<string>();

    public string GetTexturePrefixCode(ItemStack stack)
        => Attributes?["attachableToEntity"]["texturePrefixCode"].AsString();

    public void CollectTextures(ItemStack stack, Shape shape, string texturePrefixCode,
        Dictionary<string, CompositeTexture> intoDict)
    {
        // The composed shape already carries every texture it references (base bag + addons). Register
        // them all so they land in the entity atlas. Texture codes in Textures are left unprefixed by
        // SubclassForStepParenting, while face codes get texturePrefixCode prepended - mirror that here.
        if (shape.Textures == null) return;
        foreach (var kv in shape.Textures)
            intoDict[texturePrefixCode + kv.Key] = new(kv.Value);
    }

    // ---- IWearableShapeSupplier --------------------------------------------

    Shape IWearableShapeSupplier.GetShape(ItemStack stack, Entity forEntity, string texturePrefixCode)
    {
        if (stack == null || HiddenWhileSelected(stack)) return null;

        // Which entry of attachedShapeBySlotCode this host wants. The player's worn bag has no slot code of its
        // own - vanilla never passes one to IWearableShapeSupplier - and wants the map's generic "*". Any other
        // host (an Equus horse) has one, and it selects a shape posed for that animal ("*-ferus", which our Equus
        // compat mod points at our own geometry). If we can't identify the host's slot, we bail out, letting
        // vanilla fall back to GetAttachedShape: the same shape, only without addons composed into it.
        string slotCode = "*";
        if (forEntity is not EntityPlayer)
        {
            slotCode = HostSlotLookup.SlotCodeFor(forEntity, stack);
            if (slotCode == null) return null;
        }

        ICoreAPI capi = forEntity.World.Api;

        // The attached root loads its OWN base shape (the composer's per-node display-shape path only knows the
        // held shape), then the shared composer attaches every addon under its slot-marker - identical
        // child-composition to the placed/held mesh path, and the reason a mounted bag carries its pouches too.
        //
        // Resolve that base through vanilla's own resolution rather than reading attachedShape.base ourselves:
        // mods relocate the shape into the per-slot map attachedShapeBySlotCode (Equus, to vary the bag on
        // horseback), which is wildcard-matched against the slot code, falling back to the held shape when
        // neither node is set. So which model a host gets stays a JSON decision - a compat mod only has to add
        // its slot to that map.
        var attached = GetAttachedShape(stack, slotCode);
        var combined = AttachmentComposer.LoadShape(capi, attached?.Base?.ToString(), Code.Domain);
        if (combined?.Elements == null || combined.Elements.Length == 0) return combined;

        AttachmentComposer.ComposeChildrenInto(capi, combined, BagNodeFor(stack));

        // The caller does NOT step-parent-prepare IWearableShapeSupplier results, so do it here.
        combined.SubclassForStepParenting(texturePrefixCode);
        return combined;
    }
}
