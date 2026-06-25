using System.Collections.Generic;
using Vintagestory.API.Common;

namespace ImmersiveBackpacks;

public static class BackpackLight
{
    /// <summary>
    /// Brightest light (HSV, by value/brightness) emitted by any of the given addon stacks, or null if
    /// none emit light. Shared by the placed block (Block.GetLightHsv) and the worn-bag entity behavior.
    /// </summary>
    public static byte[] Brightest(IEnumerable<ItemStack> addonStacks, IBlockAccessor blockAccessor)
    {
        if (addonStacks == null) return null;

        byte[] best = null;
        foreach (var stack in addonStacks)
        {
            if (stack?.Collectible == null) continue;
            var light = stack.Collectible.GetLightHsv(blockAccessor, null, stack);
            if (light != null && light[2] > 0 && (best == null || light[2] > best[2]))
                best = light;
        }
        return best;
    }
}
