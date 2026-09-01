#nullable enable
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace ImmersiveModularBackpacks.Attachments;

public readonly record struct SlotData(
    string Code,
    Cuboidf Box,
    Vec3f Origin,
    AttachmentTransform Transform,
    AttachmentMirror Mirror,
    bool Virtual,
    string[] Slots,
    JsonObject Config
);
