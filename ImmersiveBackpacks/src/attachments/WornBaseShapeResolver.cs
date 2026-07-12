using System.Collections.Generic;
using Vintagestory.API.Datastructures;

namespace ImmersiveBackpacks.attachments;

/// <summary>
/// Resolves the worn base-shape path from a collectible's <c>attachableToEntity</c> config.
///
/// Vanilla keeps it at <c>attachedShape.base</c>, but mods relocate it: Equus REMOVEs that node and moves the
/// shape into the per-slot map <c>attachedShapeBySlotCode</c> (to vary the worn shape per attach slot, e.g.
/// horse vs player), setting its <c>"*"</c> default to vanilla's original path. Reading only <c>attachedShape</c>
/// then yields null and no worn bag renders.
///
/// Rather than bake a fixed fallback chain into the call site, this tries an ordered list of pluggable
/// <see cref="Source"/>s and returns the first that yields a path. A new relocation scheme is supported by
/// <see cref="Register"/>ing another source - no call-site change. The built-in order reproduces the prior
/// hard-coded behaviour exactly (direct node, then the slot map's specific entry, then its "*" default).
/// </summary>
public static class WornBaseShapeResolver
{
    /// <summary>Maps an <c>attachableToEntity</c> JSON node + attach slot code to a base shape path, or null.</summary>
    public delegate string Source(JsonObject attachableToEntity, string slotCode);

    private static readonly List<Source> Sources =
    [
        // Vanilla: the single worn shape.
        (atta, _) => atta["attachedShape"]["base"].AsString(),
        // Relocated per-slot (Equus & co): the slot's own entry, else the "*" default it maps to vanilla's path.
        (atta, slot) => atta["attachedShapeBySlotCode"][slot]["base"].AsString()
                        ?? atta["attachedShapeBySlotCode"]["*"]["base"].AsString(),
    ];

    /// <summary>Append a source, tried after the built-ins. For future relocation schemes / other mods.</summary>
    public static void Register(Source source) => Sources.Add(source);

    /// <summary>First non-empty base path any source yields, or null when none apply.</summary>
    public static string Resolve(JsonObject attachableToEntity, string slotCode = "*")
    {
        if (attachableToEntity is not { Exists: true }) return null;
        foreach (var src in Sources)
        {
            string path = src(attachableToEntity, slotCode);
            if (!string.IsNullOrEmpty(path)) return path;
        }
        return null;
    }
}
