using System;
using System.Collections.Generic;
using System.Text;
using ImmersiveBackpacks.attachments;
using ImmersiveBackpacks.inventory;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace ImmersiveBackpacks.blocks;

public class BlockEntityImmersiveBackpack : BlockEntityOpenableContainer, IAttachmentHost
{
    public record AttachmentPoint(string Code, Cuboidf Hitbox, string[] Categories,
        AttachmentTransform Transform, Vec3f Origin);

    private InventoryGeneric cargoInv = new(1, null, null);
    private BackpackSlotLayout.SlotSpec[] cargoLayout;   // layout cargoInv was built for; rebuild when it changes
    private BlockEntityImmersiveBackpackRenderer renderer;
    private byte[] lastEmittedLight;

    public AssetLocation BackpackItemCode { get; private set; }
    public AttachmentPoint[] AttachmentPoints { get; private set; } = [];
    public ItemStack[] AttachedItems { get; private set; } = [];

    // Horizontal placement orientation (radians), applied by the renderer.
    public float MeshAngleRad { get; set; }

    public override InventoryBase Inventory => cargoInv;
    public override string InventoryClassName => "immersivebackpack";

    public override void Initialize(ICoreAPI api)
    {
        base.Initialize(api);

        if (api is ICoreClientAPI capi)
        {
            renderer = new(Pos, capi, this);
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
        int pointIndex = blockSel.SelectionBoxIndex - 1;

        // Shift + right-click is our attach/detach gesture (box 0 is the body, boxes 1+ are the points). Always
        // consume it so a held placeable addon can't place itself against the bag; only a point box attaches.
        if (byPlayer.Entity.Controls.ShiftKey)
        {
            if (pointIndex >= 0 && pointIndex < AttachmentPoints.Length && Api.Side == EnumAppSide.Server)
                OnPlayerInteractWithPoint(pointIndex, byPlayer);
            return true;
        }

        // Ctrl + right-click opens the cargo dialog (vanilla-style modifier); plain right-click picks the
        // whole pack back up.
        if (byPlayer.Entity.Controls.CtrlKey)
        {
            if (Api.Side == EnumAppSide.Client)
                OpenCargoDialog(byPlayer);
            return true;
        }

        if (Api.Side == EnumAppSide.Server)
            PickUp(byPlayer);
        return true;
    }

    // Plain right-click returns the whole pack (addons + cargo) to the player and clears the block.
    private void PickUp(IPlayer byPlayer)
    {
        var stack = CreateDropItemStack(Api.World);
        if (stack == null) return;
        Expel(stack, byPlayer);
        Api.World.BlockAccessor.SetBlock(0, Pos);
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

            // Live-host lifecycle: fire once at the real attach, after cargo is in place so the node sees its
            // owned tools. Nodes are otherwise rebuilt per render, so this is their only attach signal.
            NodeAt(pointIndex)?.OnAttached(this);
        }
        else
        {
            var addon = AttachedItems[pointIndex];

            // Notify the node it is leaving the host before its cargo/layout is torn down below.
            NodeAt(pointIndex)?.OnDetached();

            // A bag addon carries its cargo back inside itself. Non-bag addons (the lantern) contribute no
            // slots, so this is a no-op for them; the guard just avoids writing an empty bag tree onto them.
            if (addon.Collectible?.GetCollectibleInterface<IHeldBag>() != null)
                StoreCargoIntoAddon(addon, point.Code);

            Expel(addon, byPlayer);
            AttachedItems[pointIndex] = null;

            RebuildCargo(oldAttached, byPlayer);
        }

        OnAttachmentContentChanged();
    }

    // The single coarse re-derive after an attachment's content changes (attach/detach today; a nested
    // toolstrap's tool swap once that lands): re-light, re-mesh, persist + sync. The design's IAttachmentHost
    // push routes here too. Re-lighting and MarkDirty run on both sides; the renderer exists only client-side
    // (server relights + syncs, the sync re-marks the client renderer via FromTreeAttributes anyway).
    private void OnAttachmentContentChanged()
    {
        UpdateEmittedLight();
        renderer?.MarkDirty();
        MarkDirty(true);
    }

    /// <summary>IAttachmentHost: a hosted attachment reports its content changed. Coarse by design — re-derive
    /// everything content-derived (model, light, save); layout changes are attach/detach, handled separately.</summary>
    public void OnAttachmentInvalidated(IAttachment source) => OnAttachmentContentChanged();

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

    /// <summary>
    /// Whether a shift+right-click holding <paramref name="stack"/> would attach it at this point: the point
    /// must be free (an occupied one detaches instead) and the stack's category accepted. The block's
    /// selection-box tint asks this to colour the slot the player is pointing at.
    /// </summary>
    public bool CanAccept(int pointIndex, ItemStack stack)
    {
        if (stack == null) return false;
        if (pointIndex < 0 || pointIndex >= AttachmentPoints.Length) return false;
        if (AttachedItems[pointIndex] != null) return false;
        return CanAcceptInPoint(AttachmentPoints[pointIndex], stack);
    }

    private bool CanAcceptInPoint(AttachmentPoint point, ItemStack stack)
    {
        foreach (var category in AttachmentCategories.Of(stack.Collectible))
            if (Array.IndexOf(point.Categories, category) >= 0) return true;
        return false;
    }

    private void OpenCargoDialog(IPlayer byPlayer)
    {
        int cols = Math.Max(1, Math.Min(4, cargoInv.Count));
        string title = Lang.Get("immersivemodularbackpacks:cargo-dialog-title");
        toggleInventoryDialogClient(byPlayer, () =>
            new GuiDialogBlockEntityInventory(title, Inventory, Pos, cols, Api as ICoreClientAPI));
    }

    public ItemStack CreateDropItemStack(IWorldAccessor world)
    {
        // Null before the BE is initialised/synced (e.g. the block-info HUD picking the block the same frame
        // it appears, before FromTreeAttributes runs) - GetItem(null) would throw.
        if (BackpackItemCode == null) return null;
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
        var backpackTree = new TreeAttribute { ["slots"] = slotsTree };
        stack.Attributes["backpack"] = backpackTree;

        return stack;
    }

    // Slot layout (base slots + addon slots, each with its type/color) shared with the worn-bag
    // IHeldBag implementation so the placed dialog and the worn bag look and store identically.
    private BackpackSlotLayout.SlotSpec[] Layout()
        => BackpackSlotLayout.Build(BagAttributes(), GetBaseSlots(), AttachedItems);

    private InventoryGeneric NewCargoInv(BackpackSlotLayout.SlotSpec[] layout)
    {
        cargoLayout = layout;
        int size = Math.Max(1, layout.Length);
        var inv = new InventoryGeneric(size, null, null,
            (slotId, slotInv) => slotId < layout.Length
                ? BackpackSlotLayout.CreateDialogSlot(slotInv, layout[slotId])
                : new(slotInv));

        if (Api != null)
            inv.LateInitialize(InventoryClassName + "-" + Pos.X + "/" + Pos.Y + "/" + Pos.Z, Api);

        // A toolstrap renders the tools in the cargo slots it owns, so a cargo edit can change the model.
        // Coarse: any cargo change re-meshes (client-only; renderer is null server-side). Cargo edits are
        // user-driven and infrequent, so the extra rebuilds don't matter. On the server, also push the BE
        // state so a cargo edit by ANY player (e.g. a remote client's dialog) reaches other clients - without
        // this the rendered tools and other clients' view stay stale until they reopen the dialog.
        inv.SlotModified += _ =>
        {
            renderer?.MarkDirty();
            if (Api?.Side == EnumAppSide.Server) MarkDirty(true);
        };
        return inv;
    }

    private static bool LayoutEquals(BackpackSlotLayout.SlotSpec[] a, BackpackSlotLayout.SlotSpec[] b)
    {
        if (a == null || b == null || a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++)
            if (a[i] != b[i]) return false;   // SlotSpec is a record: value equality
        return true;
    }

    /// <summary>
    /// The unified-cargo stacks the addon at the given attachment point owns, in slot order — a toolstrap's
    /// rendered tools. Null when the point has no addon or the addon contributes no slots. The placed renderer
    /// hands this to <c>AttachmentFactory.For</c> so the toolstrap composes its tools.
    /// </summary>
    // The live-host attachment node for the addon at a point (its owned cargo composed in), or null if empty.
    private IAttachment NodeAt(int pointIndex)
    {
        if (pointIndex < 0 || pointIndex >= AttachedItems.Length) return null;
        var addon = AttachedItems[pointIndex];
        return addon == null ? null : AttachmentFactory.For(addon, Api.World, OwnedCargo(pointIndex));
    }

    public IReadOnlyList<ItemStack> OwnedCargo(int pointIndex)
    {
        if (cargoInv == null || pointIndex < 0 || pointIndex >= AttachedItems.Length) return null;
        var (off, count) = BackpackSlotLayout.AddonRanges(GetBaseSlots(), AttachedItems)[pointIndex];
        if (count <= 0) return null;

        var owned = new List<ItemStack>(count);
        for (int k = 0; k < count && off + k < cargoInv.Count; k++)
            owned.Add(cargoInv[off + k].Itemstack);
        return owned;
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
    // dropped) instead of silently sliding into a neighboring addon's differently filtered slots.
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
                newSlotsByOwner[newOwners[i]] = slots = [];
            slots.Add(i);
        }

        var filled = new Dictionary<string, int>();   // owner -> how many of its new slots are taken
        int oldCount = cargoInv?.Count ?? 0;
        for (int oldIdx = 0; oldIdx < oldCount; oldIdx++)
        {
            var stack = cargoInv?[oldIdx]?.Itemstack;
            if (stack == null) continue;

            string owner = oldIdx < oldOwners.Count ? oldOwners[oldIdx] : "";
            int pos = filled.GetValueOrDefault(owner, 0);
            filled[owner] = pos + 1;

            if (newSlotsByOwner.TryGetValue(owner, out var slots) && pos < slots.Count)
                newInv[slots[pos]].Itemstack = stack;
            else
                Expel(stack, byPlayer);
        }

        cargoInv = newInv;
    }

    // Attributes of the bag item this block was placed from - the source of both its base slot count and,
    // for a compat patch that sets them, its base slots' storage flags/color.
    private JsonObject BagAttributes()
        => BackpackItemCode == null ? null : Api?.World.GetItem(BackpackItemCode)?.Attributes;

    private int GetBaseSlots()
        => BagAttributes()?["backpack"]?["quantitySlots"]?.AsInt(0) ?? 0;

    private void LoadAttachmentConfig(ICoreAPI api, CollectibleObject coll)
    {
        var ibAttr = coll.Attributes?["immersiveBackpack"];
        var pointsJson = ibAttr?["attachmentPoints"];
        if (pointsJson is not { Exists: true })
        {
            AttachmentPoints = [];
            return;
        }

        // Slots authored as slot_<code> markers in the bag's item shape (16-unit) take precedence: the box
        // becomes the [0,1] hitbox and the marker's composed rotation is the placed orientation. The patch
        // "hitbox"/"placed" are the fallback for unported bags. Use the passed api (during FromTreeAttributes
        // the BE's own Api is not set yet, so callers pass worldForResolving.Api).
        string shapeBase = (coll as Item)?.Shape?.Base?.ToString() ?? (coll as Block)?.Shape?.Base?.ToString();
        var shapeSlots = api != null
            ? AttachmentMesh.ReadSlots(api, shapeBase, coll.Code.Domain)
            : new();

        var raw = pointsJson.AsArray();
        var points = new List<AttachmentPoint>();
        foreach (var entry in raw)
        {
            var code = entry["code"].AsString();
            var cats = entry["categories"].AsArray<string>() ?? [];

            Cuboidf hitbox;
            AttachmentTransform transform;
            Vec3f origin;
            if (code != null && shapeSlots.TryGetValue(code, out var marker))
            {
                // Geometry comes entirely from the slot marker: box -> [0,1] hitbox, composed rotation ->
                // the placed orientation, pivot -> the placement anchor. The addon's own attachedTransform
                // (scale etc.) is folded in later.
                var b = marker.Box;
                hitbox = new(b.X1 / 16f, b.Y1 / 16f, b.Z1 / 16f, b.X2 / 16f, b.Y2 / 16f, b.Z2 / 16f);
                transform = AttachmentTransform.FromRotation(marker.Rotation);
                origin = new(marker.Origin.X / 16f, marker.Origin.Y / 16f, marker.Origin.Z / 16f);
            }
            else
            {
                // No marker: fall back to a patch-defined hitbox/placed (legacy/unported bags); anchor at the
                // hitbox center.
                float[] hb = entry["hitbox"].AsArray<float>();
                if (hb == null || hb.Length < 6) continue;
                hitbox = new(hb[0], hb[1], hb[2], hb[3], hb[4], hb[5]);
                transform = AttachmentTransform.FromJson(entry["placed"]);
                origin = new((hb[0] + hb[3]) / 2f, (hb[1] + hb[4]) / 2f, (hb[2] + hb[5]) / 2f);
            }

            points.Add(new(code, hitbox, cats, transform, origin));
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
        // Rebuild on any layout change, not just a slot-count change, so swapping an addon for another with the
        // same count but a different slot type refreshes each slot's filter/color on the client.
        var layout = BackpackSlotLayout.Build(item?.Attributes, baseSlots, AttachedItems);
        bool layoutChanged = !LayoutEquals(cargoLayout, layout);
        if (layoutChanged)
            cargoInv = NewCargoInv(layout);

        base.FromTreeAttributes(tree, worldForResolving);
        renderer?.MarkDirty();

        // Attachment changes are processed server-side and reach the client only through this sync. The
        // client renders its own world lighting, so re-run the light diff here to make a freshly attached
        // (or removed) torch glow without relogging. Server already relights in OnAttachmentChanged; guard
        // to client so load-time calls (Api still null) and server are unaffected.
        if (Api?.Side == EnumAppSide.Client)
        {
            UpdateEmittedLight();
            // The cargo inventory instance was just replaced; an open dialog still points at the old one and
            // shows stale slots. Rebind it to the new inventory so attaching/detaching a slot-bearing addon
            // updates the open UI live instead of requiring the player to close and reopen it.
            if (layoutChanged) RebindOpenDialog();
        }
    }

    // Reopen the cargo dialog against the current inventory when one is open and the slot layout just changed.
    private void RebindOpenDialog()
    {
        if (invDialog == null || Api is not ICoreClientAPI capi) return;
        var byPlayer = capi.World.Player;
        invDialog.TryClose();          // OnClosed nulls invDialog and notifies the server we closed
        OpenCargoDialog(byPlayer);     // reopen bound to the new cargoInv
    }

    public override void OnBlockUnloaded()
    {
        base.OnBlockUnloaded();
        renderer?.Dispose();
    }

    public override void OnBlockRemoved()
    {
        // Our light is emitted dynamically via GetLightHsv (the block's own static LightHsv is 0), so the
        // engine's default block-removal relight - which keys off the static value - never un-spreads it,
        // leaving an orphan glow with no source in the lightmap. Remove our contribution explicitly. Runs on
        // both sides (each owns its lighting engine); RemoveBlockLight re-floods from remaining sources so a
        // redundant call is harmless.
        if (lastEmittedLight != null && lastEmittedLight[2] > 0)
            Api?.World.BlockAccessor.RemoveBlockLight(lastEmittedLight, Pos);

        base.OnBlockRemoved();
        renderer?.Dispose();
    }

    public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc) { }
}
