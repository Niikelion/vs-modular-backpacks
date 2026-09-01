#nullable enable
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace ImmersiveModularBackpacks.Attachments;

internal static class SlotDataLoader
{
    public static SlotData[] Load(ICoreAPI? api, CollectibleObject collectible, JsonObject? points,
        string? shapeBasePath = null, string context = "placed", AttachmentTransform? additionalTransform = null)
    {
        if (points is not { Exists: true }) return [];

        shapeBasePath ??= ShapeBase(collectible);
        var markers = api == null
            ? new()
            : AttachmentMesh.ReadSlots(api, shapeBasePath, collectible.Code.Domain);

        var result = new List<SlotData>();
        foreach (var config in points.AsArray() ?? [])
        {
            string? code = config["code"].AsString();
            if (string.IsNullOrEmpty(code)) continue;

            Cuboidf box;
            Vec3f origin;
            AttachmentTransform transform;
            if (markers.TryGetValue(code, out var marker))
            {
                box = Scale(marker.Box);
                origin = marker.Origin / 16f;
                transform = AttachmentTransform.FromRotation(marker.Rotation)
                    .CombinedWith(AttachmentTransform.FromModelTransform(config[context]));
            }
            else
            {
                if (ParseHitbox(config["hitbox"]) is not { } parsedBox) continue;
                box = parsedBox;
                origin = box.Center.ToVec3f();
                transform = AttachmentTransform.FromModelTransform(config[context]);
            }

            if (additionalTransform != null) transform = transform.CombinedWith(additionalTransform);

            result.Add(new(
                code,
                box,
                origin,
                transform,
                ParseMirror(Strings(config["mirror"])),
                config["virtual"].AsBool(),
                Strings(config["slots"]),
                config
            ));
        }
        return ValidateVirtuals(api, collectible, result);
    }

    private static string? ShapeBase(CollectibleObject collectible)
    {
        var shape = AttachmentMesh.AttachedShapeComposite(collectible)
            ?? (collectible as Item)?.Shape
            ?? (collectible as Block)?.Shape;
        return shape?.Base?.ToString();
    }

    private static Cuboidf Scale(Cuboidf box)
        => new(box.X1 / 16f, box.Y1 / 16f, box.Z1 / 16f,
            box.X2 / 16f, box.Y2 / 16f, box.Z2 / 16f);

    private static Cuboidf? ParseHitbox(JsonObject value)
    {
        float[]? coordinates = value.AsArray<float>();
        return coordinates is { Length: >= 6 }
            ? new Cuboidf(coordinates[0], coordinates[1], coordinates[2],
                coordinates[3], coordinates[4], coordinates[5])
            : null;
    }

    private static AttachmentMirror ParseMirror(string[] axes)
    {
        var mirror = AttachmentMirror.None;
        foreach (string axis in axes)
            mirror |= axis.ToLowerInvariant() switch
            {
                "x" => AttachmentMirror.X,
                "y" => AttachmentMirror.Y,
                "z" => AttachmentMirror.Z,
                _ => AttachmentMirror.None
            };
        return mirror;
    }

    private static SlotData[] ValidateVirtuals(ICoreAPI? api, CollectibleObject collectible, List<SlotData> slots)
    {
        var byCode = new Dictionary<string, SlotData>();
        foreach (var slot in slots)
            byCode.TryAdd(slot.Code, slot);

        var valid = new List<SlotData>(slots.Count);
        foreach (var slot in slots)
        {
            if (!slot.Virtual)
            {
                valid.Add(slot);
                continue;
            }

            bool membersValid = slot.Slots.Length > 0;
            foreach (string member in slot.Slots)
                membersValid &= byCode.TryGetValue(member, out var target) && !target.Virtual;

            if (membersValid)
            {
                valid.Add(slot);
                continue;
            }

            api?.Logger.Warning("Skipping invalid virtual attachment point {0} on {1}: members must be existing real points.",
                slot.Code, collectible.Code);
        }
        return [.. valid];
    }

    private static string[] Strings(JsonObject value)
        => [.. (value.AsArray<string?>() ?? []).OfType<string>()];
}
