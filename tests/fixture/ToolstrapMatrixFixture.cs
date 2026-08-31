using Vintagestory.API.Common;
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
                    code = new { type = "string" }
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
        slot.MarkDirty();
        return new { ok = true, slot = slotIndex, code = item.Code.ToString() };
    }
}
