using System;
using System.Linq;
using System.Reflection;
using ImmersiveBackpacks.blocks;
using ImmersiveBackpacks.inventory;
using ImmersiveModularBackpacks.Attachments;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using VSMCP;

namespace ToolstrapMatrixFixture;

public sealed class ToolstrapMatrixFixtureMod : ModSystem
{
    public override void Start(ICoreAPI api)
    {
        VsmcpApi.RegisterAction("ib_matrix_attach_toolstrap", ActionSide.Server, AttachToolstrap,
            "Attach a toolstrap to the placed backpack's left strap point for matrix setup.",
            new
            {
                type = "object",
                properties = new
                {
                    x = new { type = "integer" },
                    y = new { type = "integer" },
                    z = new { type = "integer" }
                },
                required = new[] { "x", "y", "z" }
            }, module: "toolstrapmatrixfixture");

        VsmcpApi.RegisterAction("ib_matrix_set_tool", ActionSide.Server, SetTool,
            "Attempt inserting a tool into the attached left toolstrap, reporting inventory and attachment acceptance.",
            new
            {
                type = "object",
                properties = new
                {
                    x = new { type = "integer" },
                    y = new { type = "integer" },
                    z = new { type = "integer" },
                    slot = new { type = "integer", minimum = 0 },
                    code = new { type = "string" },
                    preset = new { type = "string" }
                },
                required = new[] { "x", "y", "z", "slot", "code" }
            }, module: "toolstrapmatrixfixture");
    }

    private static object AttachToolstrap(ActionArgs args, ActionContext context)
    {
        try
        {
            var pos = new BlockPos(args.GetInt("x"), args.GetInt("y"), args.GetInt("z"));
            if (context.Server.World.BlockAccessor.GetBlockEntity(pos) is not BlockEntityImmersiveBackpack container)
                return new { ok = false, error = "No backpack at the requested position." };

            if (container.AttachmentPoints.Length == 0)
            {
                var backpack = context.Server.World.GetItem(new AssetLocation("game:backpack-normal"));
                if (backpack == null) return new { ok = false, error = "Backpack item was not found." };
                container.InitFromItemStack(new ItemStack(backpack));
            }

            // place_block fires no placement hook, so face the left-strap/flap corner at the matrix camera.
            container.MeshAngleRad = GameMath.PI * 1.5f;
            container.MarkDirty(true);

            int pointIndex = Array.FindIndex(container.AttachmentPoints, point => point.Code == "left_strap");
            if (pointIndex < 0) return new { ok = false, error = "Bag has no left_strap point." };
            if (container.AttachedItems[pointIndex] != null) return new { ok = true, attached = true };

            var item = context.Server.World.GetItem(new AssetLocation("immersivemodularbackpacks:toolstrap"));
            if (item == null) return new { ok = false, error = "Toolstrap item was not found." };

            var slot = new DummySlot(new ItemStack(item));
            var player = context.Server.World.AllOnlinePlayers.FirstOrDefault();
            AttachMethod.Invoke(container, new object[] { pointIndex, slot, player });
            return new { ok = container.AttachedItems[pointIndex] != null, attached = container.AttachedItems[pointIndex] != null };
        }
        catch (Exception exception)
        {
            return new { ok = false, error = exception.ToString() };
        }
    }

    private static object SetTool(ActionArgs args, ActionContext context)
    {
        try
        {
            return TrySetTool(args, context);
        }
        catch (Exception exception)
        {
            return new { ok = false, error = exception.ToString() };
        }
    }

    private static object TrySetTool(ActionArgs args, ActionContext context)
    {
        var pos = new BlockPos(args.GetInt("x"), args.GetInt("y"), args.GetInt("z"));
        if (context.Server.World.BlockAccessor.GetBlockEntity(pos) is not BlockEntityImmersiveBackpack container)
            return new { ok = false, error = "No backpack at the requested position." };

        int slotIndex = args.GetInt("slot");
        if (slotIndex < 0 || slotIndex >= container.Inventory.Count)
            return new { ok = false, error = "Cargo slot is out of range." };

        int ownerIndex = Array.FindIndex(container.AttachmentPoints, point => point.Code == "left_strap");
        if (ownerIndex < 0 || container.AttachedItems[ownerIndex] is not { } strapStack)
            return new { ok = false, error = "No attachment at left_strap." };
        int baseSlots = container.Inventory.Count - container.AttachedItems.Sum(BackpackSlotLayout.AddonSlotCount);
        var range = BackpackSlotLayout.AddonRanges(baseSlots, container.AttachedItems)[ownerIndex];
        var strap = AttachmentFactory.For(strapStack, context.Server.World, container.AttachmentPoints[ownerIndex]);
        if (range.count != 1 || slotIndex != range.offset || strap?.Points.Count != 1)
            return new { ok = false, error = "Requested slot is not the left toolstrap's single tool slot." };

        var slot = container.Inventory[slotIndex];
        if (!slot.Empty) slot.TakeOutWhole();
        slot.MarkDirty();

        string code = args.GetString("code");
        var item = context.Server.World.GetItem(new AssetLocation(code));
        if (item == null)
            return new { ok = true, accepted = false, available = false, reason = $"Item '{code}' was not found." };

        var source = new DummySlot(new ItemStack(item));
        ApplyPreset(source.Itemstack, args.GetString("preset", null));
        var candidate = AttachmentFactory.For(source.Itemstack, context.Server.World);
        bool attachmentAccepted = candidate != null && strap.Points[0].Accepts(candidate);
        bool inventoryAccepted = slot.CanHold(source);
        int moved = attachmentAccepted && inventoryAccepted
            ? source.TryPutInto(context.Server.World, slot, 1)
            : 0;
        bool accepted = moved == 1 && slot.Itemstack?.Collectible == item && source.Empty;
        string reason = !inventoryAccepted && !attachmentAccepted ? "Inventory filter and attachment point reject this item."
            : !inventoryAccepted ? "Inventory filter rejects this item (tags/storage flags)."
            : !attachmentAccepted ? "Attachment point rejects this item."
            : !accepted ? "Slot transfer failed."
            : "Inserted through the inventory transfer path; attachment point accepts it.";
        slot.MarkDirty();
        return new { ok = true, accepted, available = true, inventoryAccepted, attachmentAccepted, moved, reason,
            slot = slotIndex, code = item.Code.ToString() };
    }

    private static void ApplyPreset(ItemStack stack, string preset)
    {
        if (preset == null || !preset.StartsWith("toolsmith:")) return;

        string tool = preset["toolsmith:".Length..];
        var multi = new TreeAttribute();
        multi.SetAttribute("head", ToolsmithNode($"toolsmith:item/parts/{tool}/heads/advanced",
            ("material", "game:block/metal/ingot/steel")));
        multi.SetAttribute("handle", ToolsmithNode($"toolsmith:item/parts/{tool}/handles/fine/handle",
            ("wood", "game:block/wood/debarked/oak")));
        multi.SetAttribute("binding", ToolsmithNode($"toolsmith:item/parts/{tool}/handles/universal/binding/metal-metalhead",
            ("material", "game:block/metal/ingot/copper")));

        ((TreeAttribute)stack.Attributes).SetAttribute("modularMultiPartRenderData", multi);
    }

    private static TreeAttribute ToolsmithNode(string shapePath, params (string Code, string Texture)[] textures)
    {
        var part = new TreeAttribute();
        part.SetString("partShapeIndex", shapePath);

        var partTextures = new TreeAttribute();
        foreach (var (code, texture) in textures)
        {
            partTextures.SetString(code, texture);
        }

        part.SetAttribute("partTextures", partTextures);

        var node = new TreeAttribute();
        node.SetAttribute("modularPartRenderData", part);
        return node;
    }

    private static readonly MethodInfo AttachMethod = typeof(BlockEntityImmersiveBackpack).GetMethod(
        "Attach",
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingMethodException(nameof(BlockEntityImmersiveBackpack), "Attach");
}
