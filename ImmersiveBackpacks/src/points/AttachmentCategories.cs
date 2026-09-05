#nullable enable
using System;
using System.Collections.Concurrent;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace ImmersiveBackpacks.points;

/// <summary>Reads the backpack mod's custom attachment-category attributes.</summary>
public static class AttachmentCategories
{
    private static readonly ConcurrentDictionary<CollectibleObject, string[]> cache = new();

    public static string[] Of(CollectibleObject? collectible)
        => collectible == null ? [] : cache.GetOrAdd(collectible, c => Read(
            c.Attributes?["immersiveBackpackAttachment"]?["category"]));

    public static bool Accepts(string[] accepted, CollectibleObject? addon)
    {
        if (accepted is not { Length: > 0 }) return false;
        foreach (string category in Of(addon))
            if (Array.IndexOf(accepted, category) >= 0) return true;
        return false;
    }

    internal static string[] Read(JsonObject? value)
    {
        if (value is not { Exists: true }) return [];
        if (value.IsArray()) return [.. (value.AsArray<string?>() ?? []).OfType<string>()];
        string? single = value.AsString();
        return single == null ? [] : [single];
    }
}
