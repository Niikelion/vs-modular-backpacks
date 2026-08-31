using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using VSMCP;

namespace ToolstrapMatrixFixture;

public sealed class ToolstrapMatrixFixtureMod : ModSystem
{
    public override void Start(ICoreAPI api)
    {
        VsmcpApi.RegisterAction("ib_matrix_set_tool", ActionSide.Server, SetTool,
            "Replace one cargo slot in a placed backpack for toolstrap screenshot generation.",
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

    private static object SetTool(ActionArgs args, ActionContext context)
    {
        var pos = new BlockPos(args.GetInt("x"), args.GetInt("y"), args.GetInt("z"));
        if (context.Server.World.BlockAccessor.GetBlockEntity(pos) is not IBlockEntityContainer container)
            return new { ok = false, error = "No container at the requested position." };

        int slotIndex = args.GetInt("slot");
        if (slotIndex < 0 || slotIndex >= container.Inventory.Count)
            return new { ok = false, error = "Cargo slot is out of range." };

        string code = args.GetString("code");
        var item = context.Server.World.GetItem(new AssetLocation(code));
        if (item == null) return new { ok = false, error = $"Item '{code}' was not found." };

        var slot = container.Inventory[slotIndex];
        slot.Itemstack = new ItemStack(item);
        ApplyPreset(slot.Itemstack, args.GetString("preset", null));
        slot.MarkDirty();
        return new { ok = true, slot = slotIndex, code = item.Code.ToString() };
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
}
