using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.GameContent;

namespace ImmersiveBackpacks;

/// <summary>
/// Appends the modular backpack's handbook additions - an intro blurb, then a row of clickable addon models,
/// one per addon type, each rotating through that item's variants.
///
/// It hooks the vanilla handbook's official extension point: the base handbook behavior calls
/// <see cref="ICustomHandbookPageContent.OnHandbookPageComposed"/> on whatever collectible interface it finds,
/// and <c>GetCollectibleInterface</c> searches behaviors with inheritance - so implementing it on this behavior
/// (added to backpacks in <see cref="BackpackHandbookModSystem"/>) is enough, and it sidesteps GetBehavior's
/// exact-type match that a subclass of the handbook behavior would fail.
/// </summary>
public class BackpackHandbookBehavior : CollectibleBehavior, ICustomHandbookPageContent
{
    // Cached per load: one entry per addon type, each the item's full set of handbook variant stacks.
    private static ItemStack[][] addonGroups;

    public BackpackHandbookBehavior(CollectibleObject collObj) : base(collObj) { }

    public static void InvalidateCache() => addonGroups = null;

    public void OnHandbookPageComposed(List<RichTextComponentBase> components, ItemSlot inSlot,
        ICoreClientAPI capi, ItemStack[] allStacks, ActionConsumable<string> openDetailPageFor)
    {
        var font = CairoFont.WhiteSmallText();
        bool haveText = true;

        CollectibleBehaviorHandbookTextAndExtraInfo.AddHeading(components, capi,
            "immersivemodularbackpacks:handbook-backpack-title", ref haveText);
        components.Add(new ClearFloatTextComponent(capi, 2f));
        components.AddRange(VtmlUtil.Richtextify(capi,
            Lang.Get("immersivemodularbackpacks:handbook-backpack-text") + "\n", font));

        var groups = addonGroups ??= BuildAddonGroups(capi);
        if (groups.Length > 0)
        {
            CollectibleBehaviorHandbookTextAndExtraInfo.AddHeading(components, capi,
                "immersivemodularbackpacks:handbook-backpack-addons-title", ref haveText);
            components.Add(new ClearFloatTextComponent(capi, 2f));
            foreach (var stacks in groups)
                components.Add(new SlideshowItemstackTextComponent(capi, stacks, 40.0, EnumFloat.Inline,
                    cs => openDetailPageFor(GuiHandbookItemStackPage.PageCodeForStack(cs))));
            components.Add(new ClearFloatTextComponent(capi, 2f));
        }
    }

    // Group every attachable collectible by category + base code (so metal/material variants collapse into one
    // rotating icon), each group carrying the item's full handbook stack set - so variants and their textures
    // (e.g. the lantern's, which come from stack attributes, not the code) render correctly. Ordered by
    // category, then name.
    private static ItemStack[][] BuildAddonGroups(ICoreClientAPI capi)
    {
        var groups = new Dictionary<string, (string cat, string name, List<ItemStack> stacks)>();

        foreach (var coll in capi.World.Collectibles)
        {
            string category = coll.Attributes?["immersiveBackpackAttachment"]["category"].AsString();
            string baseCode = coll.Code?.FirstCodePart();
            if (category == null || baseCode == null) continue;

            var stacks = coll.GetHandBookStacks(capi);
            if (stacks == null || stacks.Count == 0)
                stacks = new List<ItemStack> { coll is Block b ? new ItemStack(b) : new ItemStack((Item)coll) };

            string key = category + "/" + baseCode;
            if (!groups.TryGetValue(key, out var group))
            {
                string name = coll.GetHeldItemName(stacks[0]);
                if (string.IsNullOrEmpty(name)) name = coll.Code.ToShortString();
                group = (category, name, new List<ItemStack>());
                groups[key] = group;
            }
            group.stacks.AddRange(stacks);
        }

        return groups.Values
            .OrderBy(v => v.cat, StringComparer.Ordinal)
            .ThenBy(v => v.name, StringComparer.Ordinal)
            .Select(v => v.stacks.ToArray())
            .ToArray();
    }
}
