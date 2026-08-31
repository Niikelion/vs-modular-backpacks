using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace ImmersiveModularBackpacks.Attachments;

internal static class SlotDataLoader
{
    public static SlotData[] Load(ICoreAPI api, CollectibleObject collectible, JsonObject points,
        string shapeBasePath = null, string context = "placed", AttachmentTransform additionalTransform = null)
    {
        if (points is not { Exists: true }) return [];

        shapeBasePath ??= ShapeBase(collectible);
        var markers = api == null
            ? new Dictionary<string, AttachmentMesh.SlotMarker>()
            : AttachmentMesh.ReadSlots(api, shapeBasePath, collectible.Code.Domain);

        var result = new List<SlotData>();
        foreach (var config in points.AsArray() ?? [])
        {
            string code = config["code"].AsString();
            if (string.IsNullOrEmpty(code)) continue;

            Cuboidf box;
            Vec3f origin;
            AttachmentTransform transform;
            if (markers.TryGetValue(code, out var marker))
            {
                box = Scale(marker.Box);
                origin = marker.Origin / 16f;
                transform = AttachmentTransform.FromRotation(marker.Rotation)
                    .CombinedWith(AttachmentTransform.FromJson(config[context]));
            }
            else
            {
                float[] hitbox = config["hitbox"].AsArray<float>();
                if (hitbox is not { Length: >= 6 }) continue;
                box = new(hitbox[0], hitbox[1], hitbox[2], hitbox[3], hitbox[4], hitbox[5]);
                origin = new((hitbox[0] + hitbox[3]) / 2f, (hitbox[1] + hitbox[4]) / 2f,
                    (hitbox[2] + hitbox[5]) / 2f);
                transform = AttachmentTransform.FromJson(config[context]);
            }

            if (additionalTransform != null) transform = transform.CombinedWith(additionalTransform);

            result.Add(new SlotData(
                code,
                config["categories"].AsArray<string>() ?? [],
                box,
                origin,
                transform,
                ParseMirror(config["mirror"].AsArray<string>()),
                config["virtual"].AsBool(false),
                config["slots"].AsArray<string>() ?? [],
                config
            ));
        }
        return ValidateVirtuals(api, collectible, result);
    }

    private static string ShapeBase(CollectibleObject collectible)
    {
        var shape = AttachmentMesh.AttachedShapeComposite(collectible)
            ?? (collectible as Item)?.Shape
            ?? (collectible as Block)?.Shape;
        return shape?.Base?.ToString();
    }

    private static Cuboidf Scale(Cuboidf box)
        => new(box.X1 / 16f, box.Y1 / 16f, box.Z1 / 16f,
            box.X2 / 16f, box.Y2 / 16f, box.Z2 / 16f);

    private static AttachmentMirror ParseMirror(string[] axes)
    {
        AttachmentMirror mirror = AttachmentMirror.None;
        foreach (string axis in axes ?? Array.Empty<string>())
            mirror |= axis?.ToLowerInvariant() switch
            {
                "x" => AttachmentMirror.X,
                "y" => AttachmentMirror.Y,
                "z" => AttachmentMirror.Z,
                _ => AttachmentMirror.None
            };
        return mirror;
    }

    private static SlotData[] ValidateVirtuals(ICoreAPI api, CollectibleObject collectible, List<SlotData> slots)
    {
        var byCode = new Dictionary<string, SlotData>();
        foreach (var slot in slots)
            if (!byCode.ContainsKey(slot.Code)) byCode[slot.Code] = slot;

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
}
