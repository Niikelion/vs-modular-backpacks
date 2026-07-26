using System;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace ImmersiveBackpacks.attachments;

/// <summary>
/// Reads an addon's declared attachment categories. The <c>immersiveBackpackAttachment.category</c>
/// attribute may be a single string or an array of strings; both collapse to a string[] here.
/// </summary>
public static class AttachmentCategories
{
    /// <summary>Categories declared by <paramref name="collectible"/>, or empty if none.</summary>
    public static string[] Of(CollectibleObject collectible)
        => Of(collectible?.Attributes);

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
