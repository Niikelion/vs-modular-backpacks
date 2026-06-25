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
        // later addon change can diff against it.
        if (api.Side == EnumAppSide.Server)
            lastEmittedLight = ComputeLightHsv();
    }

    /// <summary>Brightest light emitted by the attached addons, or null. Read by Block.GetLightHsv.</summary>
    public byte[] ComputeLightHsv()
        => Api == null ? null : BackpackLight.Brightest(AttachedItems, Api.World.BlockAccessor);

    // Re-trigger chunk lighting when the emitted light changes: remove the old contribution, then
    // exchange the block with itself so the engine re-reads GetLightHsv (vanilla dynamic-light pattern).
    private void UpdateEmittedLight()
    {
        if (Api == null || Api.Side != EnumAppSide.Server) return;

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
        LoadAttachmentConfig(stack.Collectible);
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

        ResizeCargo();

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

        if (AttachedItems[pointIndex] == null)
        {
            if (activeSlot.Empty) return;
            if (!CanAcceptInPoint(point, activeSlot.Itemstack)) return;

            AttachedItems[pointIndex] = activeSlot.Itemstack.Clone();
            AttachedItems[pointIndex].StackSize = 1;
            activeSlot.TakeOut(1);
            activeSlot.MarkDirty();
        }
        else
        {
            if (!byPlayer.InventoryManager.TryGiveItemstack(AttachedItems[pointIndex]))
                Api.World.SpawnItemEntity(AttachedItems[pointIndex], Pos.ToVec3d().Add(0.5, 0.5, 0.5));
            AttachedItems[pointIndex] = null;
        }

        OnAttachmentChanged();
    }

    private bool CanAcceptInPoint(AttachmentPoint point, ItemStack stack)
    {
        var category = stack.Collectible.Attributes?["immersiveBackpackAttachment"]?["category"]?.AsString();
        if (category == null) return false;
        return Array.IndexOf(point.Categories, category) >= 0;
    }

    private void OnAttachmentChanged()
    {
        ResizeCargo();
        UpdateEmittedLight();
        MarkDirty(true);
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

    private void ResizeCargo()
    {
        var newInv = NewCargoInv(Layout());

        int copy = Math.Min(cargoInv?.Count ?? 0, newInv.Count);
        for (int i = 0; i < copy; i++)
            newInv[i].Itemstack = cargoInv[i].Itemstack;

        for (int i = newInv.Count; i < (cargoInv?.Count ?? 0); i++)
        {
            if (cargoInv[i].Itemstack == null) continue;
            Api?.World.SpawnItemEntity(cargoInv[i].Itemstack, Pos.ToVec3d().Add(0.5, 0.5, 0.5));
        }

        cargoInv = newInv;
    }

    private int GetBaseSlots()
    {
        if (BackpackItemCode == null) return 0;
        var item = Api?.World.GetItem(BackpackItemCode);
        return item?.Attributes?["backpack"]?["quantitySlots"]?.AsInt(0) ?? 0;
    }

    private void LoadAttachmentConfig(CollectibleObject coll)
    {
        var pointsJson = coll.Attributes?["immersiveBackpack"]?["attachmentPoints"];
        if (pointsJson == null || !pointsJson.Exists)
        {
            AttachmentPoints = [];
            return;
        }

        var raw = pointsJson.AsArray();
        var points = new List<AttachmentPoint>();
        foreach (var entry in raw)
        {
            var code = entry["code"].AsString();
            var hb = entry["hitbox"].AsArray<float>();
            if (hb == null || hb.Length < 6) continue;
            var cats = entry["categories"].AsArray<string>() ?? [];
            var hitbox = new Cuboidf(hb[0], hb[1], hb[2], hb[3], hb[4], hb[5]);
            var placed = AttachmentTransform.FromJson(entry["placed"]);
            var worn = AttachmentTransform.FromJson(entry["worn"]);
            points.Add(new AttachmentPoint(code, hitbox, cats, placed, worn));
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
        if (item != null) LoadAttachmentConfig(item);

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
