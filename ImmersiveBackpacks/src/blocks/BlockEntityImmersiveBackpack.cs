using System;
using System.Collections.Generic;
using System.Text;
using ImmersiveBackpacks.inventory;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace ImmersiveBackpacks.blocks;

public class BlockEntityImmersiveBackpack : BlockEntityOpenableContainer
{
    public record AttachmentPoint(string Code, Cuboidf Hitbox, string[] Categories,
        AttachmentTransform Placed, AttachmentTransform Worn);

    private InventoryGeneric cargoInv;
    private BlockEntityImmersiveBackpackRenderer renderer;
    private byte[] lastEmittedLight;

    public AssetLocation BackpackItemCode { get; private set; }
    public AttachmentPoint[] AttachmentPoints { get; private set; } = [];
    public ItemStack[] AttachedItems { get; private set; } = [];

    // Horizontal placement orientation (radians), applied by the renderer.
    public float MeshAngleRad { get; set; }

    public override InventoryBase Inventory => cargoInv;
    public override string InventoryClassName => "immersivebackpack";

    public BlockEntityImmersiveBackpack()
    {
        cargoInv = new InventoryGeneric(1, null, null);
    }

    public override void Initialize(ICoreAPI api)
    {
        base.Initialize(api);

        if (api is ICoreClientAPI capi)
        {
            renderer = new BlockEntityImmersiveBackpackRenderer(Pos, capi, this);
            capi.Event.RegisterRenderer(renderer, EnumRenderStage.Opaque, "immersivebackpack");
        }

        // The chunk lighting engine reads GetLightHsv on load/placement; just record what it lit so a
        // later addon change can diff against it. Tracked per-side: client and server each run their own
        // lighting engine, so each must diff against what it last emitted.
        lastEmittedLight = ComputeLightHsv();
    }

    /// <summary>Brightest light emitted by the attached addons, or null. Read by Block.GetLightHsv.</summary>
    public byte[] ComputeLightHsv()
        => Api == null ? null : BackpackLight.Brightest(AttachedItems, Api.World.BlockAccessor);

    // Re-trigger chunk lighting when the emitted light changes: remove the old contribution, then
    // exchange the block with itself so the engine re-reads GetLightHsv (vanilla dynamic-light pattern,
    // see BlockEntityGroundStorage.LightUpdate). Runs on both sides: the server spreads light to
    // neighbours/other players, while the client owns the light actually rendered for the local player.
    private void UpdateEmittedLight()
    {
        if (Api == null) return;

        var old = lastEmittedLight;
        lastEmittedLight = ComputeLightHsv();
        if (LightEquals(old, lastEmittedLight)) return;

        if (old != null && old[2] > 0)
            Api.World.BlockAccessor.RemoveBlockLight(old, Pos);
        Api.World.BlockAccessor.ExchangeBlock(Block.Id, Pos);
    }

    private static bool LightEquals(byte[] a, byte[] b)
    {
        if (a == null || b == null) return a == b;
        return a[0] == b[0] && a[1] == b[1] && a[2] == b[2];
    }

    // Called by BackpackPlacementBehavior when the block is freshly placed.
    public void InitFromItemStack(ItemStack stack)
    {
        BackpackItemCode = stack.Collectible.Code;
        LoadAttachmentConfig(Api, stack.Collectible);
        AttachedItems = new ItemStack[AttachmentPoints.Length];

        var addonsTree = stack.Attributes?.GetTreeAttribute("placed_addons");
        if (addonsTree != null)
        {
            for (int i = 0; i < AttachmentPoints.Length; i++)
            {
                var s = addonsTree.GetItemstack(AttachmentPoints[i].Code);
                if (s == null) continue;
                s.ResolveBlockOrItem(Api.World);
                AttachedItems[i] = s;
            }
        }

        cargoInv = NewCargoInv(Layout());

        var slotsTree = stack.Attributes?.GetTreeAttribute("backpack")?.GetTreeAttribute("slots");
        if (slotsTree != null)
        {
            for (int i = 0; i < cargoInv.Count; i++)
            {
                var s = (slotsTree["slot-" + i] as ItemstackAttribute)?.value;
                if (s == null) continue;
                s.ResolveBlockOrItem(Api.World);
                cargoInv[i].Itemstack = s;
            }
        }

        UpdateEmittedLight();
        MarkDirty(true);
    }

    public override bool OnPlayerRightClick(IPlayer byPlayer, BlockSelection blockSel)
    {
        if (blockSel.SelectionBoxIndex == 0)
        {
            if (Api.Side == EnumAppSide.Client)
                OpenCargoDialog(byPlayer);
            return true;
        }

        int pointIndex = blockSel.SelectionBoxIndex - 1;
        if (pointIndex >= AttachmentPoints.Length) return false;

        if (Api.Side == EnumAppSide.Server)
            OnPlayerInteractWithPoint(pointIndex, byPlayer);

        return true;
    }

    private void OnPlayerInteractWithPoint(int pointIndex, IPlayer byPlayer)
    {
        var point = AttachmentPoints[pointIndex];
        var activeSlot = byPlayer.InventoryManager.ActiveHotbarSlot;
        var oldAttached = (ItemStack[])AttachedItems.Clone();

        if (AttachedItems[pointIndex] == null)
        {
            if (activeSlot.Empty) return;
            if (!CanAcceptInPoint(point, activeSlot.Itemstack)) return;

            var addon = activeSlot.Itemstack.Clone();
            addon.StackSize = 1;
            AttachedItems[pointIndex] = addon;
            activeSlot.TakeOut(1);
            activeSlot.MarkDirty();

            // Base + other addons keep their cargo (RebuildCargo), then this bag's own contents flow into
            // the slots it just added.
            RebuildCargo(oldAttached, byPlayer);
            LoadAddonIntoCargo(addon, point.Code);
        }
        else
        {
            var addon = AttachedItems[pointIndex];

            // A bag addon carries its cargo back inside itself. Non-bag addons (the lantern) contribute no
            // slots, so this is a no-op for them; the guard just avoids writing an empty bag tree onto them.
            if (addon.Collectible?.GetCollectibleInterface<IHeldBag>() != null)
                StoreCargoIntoAddon(addon, point.Code);

            Expel(addon, byPlayer);
            AttachedItems[pointIndex] = null;

            RebuildCargo(oldAttached, byPlayer);
        }

        UpdateEmittedLight();
        MarkDirty(true);
    }

    // Hand a stack back to the interacting player, dropping it at the block if their inventory is full.
    private void Expel(ItemStack stack, IPlayer byPlayer)
    {
        if (stack == null) return;
        if (byPlayer != null && byPlayer.InventoryManager.TryGiveItemstack(stack)) return;
        Api?.World.SpawnItemEntity(stack, Pos.ToVec3d().Add(0.5, 0.5, 0.5));
    }

    // Cargo slot indices (into the current cargoInv) contributed by the addon at the given point.
    private List<int> CargoSlotIndices(string pointCode, ItemStack[] attached)
    {
        var owners = SlotOwners(attached);
        var indices = new List<int>();
        int count = cargoInv?.Count ?? 0;
        for (int i = 0; i < owners.Count && i < count; i++)
            if (owners[i] == pointCode) indices.Add(i);
        return indices;
    }

    // Move a freshly-attached bag's stored contents into the cargo slots it now owns, then strip them from
    // the bag stack. While attached, the unified cargo inventory is the single home for those items (so the
    // bag is not persisted with a duplicate copy and the items are not re-imported on reload).
    private void LoadAddonIntoCargo(ItemStack addon, string pointCode)
    {
        var slots = AddonSlotsTree(addon, create: false);
        if (slots == null) return;

        var indices = CargoSlotIndices(pointCode, AttachedItems);
        for (int k = 0; k < indices.Count; k++)
        {
            var s = (slots["slot-" + k] as ItemstackAttribute)?.value;
            if (s == null) continue;
            s.ResolveBlockOrItem(Api.World);
            cargoInv[indices[k]].Itemstack = s;
        }
        addon.Attributes.GetTreeAttribute("backpack")?.RemoveAttribute("slots");
    }

    // Move the cargo from a bag's owned slots back inside the bag stack (vanilla IHeldBag layout) and clear
    // those cargo slots, so the detached bag carries its contents and RebuildCargo won't also expel them.
    private void StoreCargoIntoAddon(ItemStack addon, string pointCode)
    {
        var indices = CargoSlotIndices(pointCode, AttachedItems);
        var slots = new TreeAttribute();
        for (int k = 0; k < indices.Count; k++)
        {
            slots["slot-" + k] = new ItemstackAttribute(cargoInv[indices[k]].Itemstack);
            cargoInv[indices[k]].Itemstack = null;
        }

        var backpack = addon.Attributes.GetTreeAttribute("backpack");
        if (backpack == null)
        {
            backpack = new TreeAttribute();
            addon.Attributes["backpack"] = backpack;
        }
        backpack["slots"] = slots;
    }

    // The vanilla IHeldBag content tree on a bag stack: backpack -> slots -> slot-{i}.
    private static ITreeAttribute AddonSlotsTree(ItemStack addon, bool create)
    {
        var backpack = addon.Attributes.GetTreeAttribute("backpack");
        if (backpack == null)
        {
            if (!create) return null;
            backpack = new TreeAttribute();
            addon.Attributes["backpack"] = backpack;
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

    private bool CanAcceptInPoint(AttachmentPoint point, ItemStack stack)
    {
        var category = stack.Collectible.Attributes?["immersiveBackpackAttachment"]?["category"]?.AsString();
        if (category == null) return false;
        return Array.IndexOf(point.Categories, category) >= 0;
    }

    private void OpenCargoDialog(IPlayer byPlayer)
    {
        int cols = Math.Max(1, Math.Min(4, cargoInv.Count));
        string title = Lang.Get("immersivebackpacks:cargo-dialog-title");
        toggleInventoryDialogClient(byPlayer, () =>
            new GuiDialogBlockEntityInventory(title, Inventory, Pos, cols, Api as ICoreClientAPI));
    }

    public ItemStack CreateDropItemStack(IWorldAccessor world)
    {
        var item = world.GetItem(BackpackItemCode);
        if (item == null) return null;
        var stack = new ItemStack(item);

        if (AttachedItems.Length > 0)
        {
            var addonsTree = new TreeAttribute();
            for (int i = 0; i < AttachmentPoints.Length; i++)
            {
                if (AttachedItems[i] == null) continue;
                addonsTree.SetItemstack(AttachmentPoints[i].Code, AttachedItems[i]);
            }
            stack.Attributes["placed_addons"] = addonsTree;
        }

        // Vanilla IHeldBag layout (backpack -> slots -> slot-{i}) so the worn bag reads the same cargo.
        var slotsTree = new TreeAttribute();
        for (int i = 0; i < cargoInv.Count; i++)
            slotsTree["slot-" + i] = new ItemstackAttribute(cargoInv[i].Itemstack);
        var backpackTree = new TreeAttribute();
        backpackTree["slots"] = slotsTree;
        stack.Attributes["backpack"] = backpackTree;

        return stack;
    }

    // Slot layout (base slots + addon slots, each with its type/colour) shared with the worn-bag
    // IHeldBag implementation so the placed dialog and the worn bag look and store identically.
    private BackpackSlotLayout.SlotSpec[] Layout()
        => BackpackSlotLayout.Build(GetBaseSlots(), AttachedItems);

    private InventoryGeneric NewCargoInv(BackpackSlotLayout.SlotSpec[] layout)
    {
        int size = Math.Max(1, layout.Length);
        var inv = new InventoryGeneric(size, null, null,
            (slotId, slotInv) => slotId < layout.Length
                ? new ItemSlotFiltered(slotInv, layout[slotId])
                : new ItemSlotSurvival(slotInv));

        if (Api != null)
            inv.LateInitialize(InventoryClassName + "-" + Pos.X + "/" + Pos.Y + "/" + Pos.Z, Api);
        return inv;
    }

    // Owner key per cargo slot for a given attachment set, aligned 1:1 with BackpackSlotLayout.Build:
    // "" for a base slot, otherwise the attachment-point code that contributed the slot. Lets RebuildCargo
    // keep each item with the addon (or base) it belongs to even though an addon's slots sit mid-layout.
    private List<string> SlotOwners(ItemStack[] attached)
    {
        var owners = new List<string>();
        for (int i = 0; i < GetBaseSlots(); i++) owners.Add("");
        for (int i = 0; i < AttachmentPoints.Length; i++)
        {
            int qty = BackpackSlotLayout.AddonSlotCount(attached[i]);
            for (int j = 0; j < qty; j++) owners.Add(AttachmentPoints[i].Code);
        }
        return owners;
    }

    // Resize cargo for the current AttachedItems while moving each item to the slot run owned by the same
    // base/addon it was in. Items whose owning addon was just detached are expelled (to the player, else
    // dropped) instead of silently sliding into a neighbouring addon's differently-filtered slots.
    private void RebuildCargo(ItemStack[] oldAttached, IPlayer byPlayer)
    {
        var newInv = NewCargoInv(Layout());
        var oldOwners = SlotOwners(oldAttached);
        var newOwners = SlotOwners(AttachedItems);

        // New slot indices grouped by owner, in order.
        var newSlotsByOwner = new Dictionary<string, List<int>>();
        for (int i = 0; i < newOwners.Count; i++)
        {
            if (!newSlotsByOwner.TryGetValue(newOwners[i], out var slots))
                newSlotsByOwner[newOwners[i]] = slots = new List<int>();
            slots.Add(i);
        }

        var filled = new Dictionary<string, int>();   // owner -> how many of its new slots are taken
        int oldCount = cargoInv?.Count ?? 0;
        for (int oldIdx = 0; oldIdx < oldCount; oldIdx++)
        {
            var stack = cargoInv[oldIdx].Itemstack;
            if (stack == null) continue;

            string owner = oldIdx < oldOwners.Count ? oldOwners[oldIdx] : "";
            int pos = filled.TryGetValue(owner, out var c) ? c : 0;
            filled[owner] = pos + 1;

            if (newSlotsByOwner.TryGetValue(owner, out var slots) && pos < slots.Count)
                newInv[slots[pos]].Itemstack = stack;
            else
                Expel(stack, byPlayer);
        }

        cargoInv = newInv;
    }

    private int GetBaseSlots()
    {
        if (BackpackItemCode == null) return 0;
        var item = Api?.World.GetItem(BackpackItemCode);
        return item?.Attributes?["backpack"]?["quantitySlots"]?.AsInt(0) ?? 0;
    }

    private void LoadAttachmentConfig(ICoreAPI api, CollectibleObject coll)
    {
        var ibAttr = coll.Attributes?["immersiveBackpack"];
        var pointsJson = ibAttr?["attachmentPoints"];
        if (pointsJson == null || !pointsJson.Exists)
        {
            AttachmentPoints = [];
            return;
        }

        // Slots authored as slot_<code> markers in the bag's item shape (16-unit) take precedence: the box
        // becomes the [0,1] hitbox and the marker's composed rotation is the placed orientation. The patch
        // "hitbox"/"placed" are the fallback for unported bags. Use the passed api (during FromTreeAttributes
        // the BE's own Api is not set yet, so callers pass worldForResolving.Api).
        var shapeBase = (coll as Item)?.Shape?.Base?.ToString() ?? (coll as Block)?.Shape?.Base?.ToString();
        var shapeSlots = api != null
            ? AttachmentMesh.ReadSlots(api, shapeBase, coll.Code.Domain)
            : new Dictionary<string, AttachmentMesh.SlotMarker>();

        var raw = pointsJson.AsArray();
        var points = new List<AttachmentPoint>();
        foreach (var entry in raw)
        {
            var code = entry["code"].AsString();
            var cats = entry["categories"].AsArray<string>() ?? [];

            Cuboidf hitbox;
            AttachmentTransform placed;
            if (code != null && shapeSlots.TryGetValue(code, out var marker))
            {
                // Geometry comes entirely from the slot marker: box -> [0,1] hitbox, composed rotation ->
                // the placed orientation. The addon's own attachedTransform (scale etc.) is folded in later.
                var b = marker.Box;
                hitbox = new Cuboidf(b.X1 / 16f, b.Y1 / 16f, b.Z1 / 16f, b.X2 / 16f, b.Y2 / 16f, b.Z2 / 16f);
                placed = AttachmentTransform.FromRotation(marker.Rotation);
            }
            else
            {
                // No marker: fall back to a patch-defined hitbox/placed (legacy/unported bags).
                var hb = entry["hitbox"].AsArray<float>();
                if (hb == null || hb.Length < 6) continue;
                hitbox = new Cuboidf(hb[0], hb[1], hb[2], hb[3], hb[4], hb[5]);
                placed = AttachmentTransform.FromJson(entry["placed"]);
            }

            points.Add(new AttachmentPoint(code, hitbox, cats, placed, AttachmentTransform.Identity));
        }
        AttachmentPoints = [.. points];
    }

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);

        tree.SetString("backpackItemCode", BackpackItemCode?.ToString() ?? "");
        tree.SetFloat("meshAngle", MeshAngleRad);

        var addonsTree = new TreeAttribute();
        for (int i = 0; i < AttachmentPoints.Length; i++)
        {
            if (AttachedItems[i] == null) continue;
            addonsTree.SetItemstack(AttachmentPoints[i].Code, AttachedItems[i]);
        }
        tree["placed_addons"] = addonsTree;
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
    {
        BackpackItemCode = new AssetLocation(tree.GetString("backpackItemCode", "game:linensack"));
        MeshAngleRad = tree.GetFloat("meshAngle", 0f);
        var item = worldForResolving.GetItem(BackpackItemCode);
        if (item != null) LoadAttachmentConfig(worldForResolving.Api, item);

        var addonsTree = tree.GetTreeAttribute("placed_addons");
        AttachedItems = new ItemStack[AttachmentPoints.Length];
        if (addonsTree != null)
        {
            for (int i = 0; i < AttachmentPoints.Length; i++)
            {
                var s = addonsTree.GetItemstack(AttachmentPoints[i].Code);
                if (s == null) continue;
                s.ResolveBlockOrItem(worldForResolving);
                AttachedItems[i] = s;
            }
        }

        int baseSlots = item?.Attributes?["backpack"]?["quantitySlots"]?.AsInt(0) ?? 0;
        var layout = BackpackSlotLayout.Build(baseSlots, AttachedItems);
        if (cargoInv == null || cargoInv.Count != Math.Max(1, layout.Length))
            cargoInv = NewCargoInv(layout);

        base.FromTreeAttributes(tree, worldForResolving);
        renderer?.MarkDirty();

        // Attachment changes are processed server-side and reach the client only through this sync. The
        // client renders its own world lighting, so re-run the light diff here to make a freshly attached
        // (or removed) torch glow without relogging. Server already relights in OnAttachmentChanged; guard
        // to client so load-time calls (Api still null) and server are unaffected.
        if (Api?.Side == EnumAppSide.Client)
            UpdateEmittedLight();
    }

    public override void OnBlockUnloaded()
    {
        base.OnBlockUnloaded();
        renderer?.Dispose();
    }

    public override void OnBlockRemoved()
    {
        base.OnBlockRemoved();
        renderer?.Dispose();
    }

    public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc) { }
}
