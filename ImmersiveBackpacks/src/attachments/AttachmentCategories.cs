using System;
using System.Collections.Concurrent;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace ImmersiveModularBackpacks.Attachments;

/// <summary>
/// Reads an addon's declared attachment categories. The <c>immersiveBackpackAttachment.category</c>
/// attribute may be a single string or an array of strings; both collapse to a string[] here.
/// </summary>
public static class AttachmentCategories
{
    /// <summary>
    /// The category a toolstrap's points name: a tool held in both hands, which is what a strap is shaped to
    /// carry. Vanilla's axes, pickaxe, shovel, hoe and prospecting pick are patched into it; a mod puts its own
    /// item on a strap by declaring the same category, with no code either side - and an item that declares
    /// nothing does not strap, tool or not.
    /// </summary>
    public const string TwoHanded = "twohanded";

    /// <summary>The category a tool roll's points name: a tool worked in one hand - knife, chisel, hammer. Held
    /// weapons stay out of it deliberately, one-handed or not.</summary>
    public const string HandTool = "handtool";

    /// <summary>The categories that ride on a strap or roll rather than bolting onto a bag.</summary>
    public static bool IsCarriedTool(string category) => category is TwoHanded or HandTool;

    // Reading them means walking the attribute JSON and deserializing an array, and the slot filters ask on every
    // pickup merge and every drag. A collectible's categories are fixed once assets are loaded, so resolve each
    // one once. Client and server hold separate collectible instances; both land here, keyed apart.
    private static readonly ConcurrentDictionary<CollectibleObject, string[]> cache = new();

    /// <summary>Categories declared by <paramref name="collectible"/>, or empty if none.</summary>
    public static string[] Of(CollectibleObject collectible)
        => collectible == null ? [] : cache.GetOrAdd(collectible, c => Of(c.Attributes));

    /// <summary>Whether <paramref name="addon"/>'s declared categories intersect <paramref name="accepted"/>.</summary>
    public static bool Accepts(string[] accepted, CollectibleObject addon)
    {
        if (accepted is not { Length: > 0 }) return false;
        foreach (var cat in Of(addon))
            if (Array.IndexOf(accepted, cat) >= 0) return true;
        return false;
    }

    /// <summary>Categories from a collectible's attributes JSON, or empty if none.</summary>
    private static string[] Of(JsonObject attributes)
    {
        var cat = attributes?["immersiveBackpackAttachment"]?["category"];
        if (cat is not { Exists: true }) return [];
        if (cat.IsArray()) return cat.AsArray<string>() ?? [];
        string single = cat.AsString();
        return single == null ? [] : [single];
    }
}
