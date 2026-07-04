using System.Collections.Generic;
using Vintagestory.API.Common;

namespace ImmersiveBackpacks.attachments;

/// <summary>
/// Turns a stored addon stack into its <see cref="IAttachment"/> node. The single dispatch point every render
/// path (placed renderer, held/GUI mesh, worn shape) goes through, so adding a container attachment type
/// (a toolstrap that hosts tools) is a one-line change here rather than a change in each host. Today every
/// addon is a leaf <see cref="ItemAttachment"/>.
/// </summary>
public static class AttachmentFactory
{
    /// <summary>A leaf node: a stack that just renders (a tool, a pouch's own body, the lantern). A container
    /// whose children come from the host's cargo (a toolstrap) can't be built here — use <see cref="ForBagChild"/>.</summary>
    public static IAttachment For(ItemStack stack, IWorldAccessor world)
    {
        if (stack?.Collectible == null) return null;
        return new ItemAttachment(stack);
    }

    /// <summary>
    /// Builds the node for an addon attached to a backpack point, given the unified-cargo slots that addon
    /// owns (empty/null for a non-slot addon). A toolstrap becomes a container over those cargo stacks as its
    /// tools; everything else is a leaf. This is the only place that needs the cargo, so leaf resolution
    /// (including a toolstrap's own tools) stays on the simple <see cref="For"/> path.
    /// </summary>
    public static IAttachment ForBagChild(ItemStack addonStack, IReadOnlyList<ItemStack> ownedCargo,
        IWorldAccessor world)
    {
        if (addonStack?.Collectible == null) return null;

        var cat = addonStack.Collectible.Attributes?["immersiveBackpackAttachment"]?["category"]?.AsString();
        if (cat == "toolstrap") return new ToolstrapAttachment(addonStack, ownedCargo, world);
        return For(addonStack, world);
    }
}
