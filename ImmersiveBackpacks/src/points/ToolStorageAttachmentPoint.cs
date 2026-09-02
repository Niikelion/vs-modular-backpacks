#nullable enable
using System;
using System.Globalization;
using ImmersiveModularBackpacks.Attachments;
using Vintagestory.API.MathTools;

namespace ImmersiveBackpacks.points;

/// <summary>A toolstrap point that swaps its stored tool with the player's active hand.</summary>
internal sealed class ToolStorageAttachmentPoint(in SlotData slot)
    : CategoryAttachmentPoint(slot), IAttachmentPointInteraction
{
    private readonly Vec4f interactionColor = ParseColor(slot.Config["interactionColor"].AsString("#b3975b"));

    public bool IsInteractionActive(in AttachmentPointInteractionContext context)
    {
        if (context.ActiveSlot.Empty) return !context.ContentSlot.Empty;
        if (!context.ContentSlot.Empty) return false;

        var candidate = AttachmentFactory.For(context.ActiveSlot.Itemstack, context.World);
        return candidate != null && Accepts(candidate);
    }

    public Vec4f GetInteractionColor(in AttachmentPointInteractionContext context)
        => interactionColor;

    public string GetInteractionHelpCode(in AttachmentPointInteractionContext context)
        => context.ActiveSlot.Empty
            ? "immersivemodularbackpacks:take-item"
            : "immersivemodularbackpacks:store-item";

    public AttachmentPointInteractionResult OnInteract(in AttachmentPointInteractionContext context)
    {
        if (!IsInteractionActive(context)) return AttachmentPointInteractionResult.Pass;

        if (context.ActiveSlot.Empty)
        {
            int quantity = context.ContentSlot.Itemstack!.StackSize;
            return context.ContentSlot.TryPutInto(context.World, context.ActiveSlot, quantity) > 0
                ? AttachmentPointInteractionResult.Changed
                : AttachmentPointInteractionResult.Handled;
        }

        return context.ActiveSlot.TryPutInto(context.World, context.ContentSlot, 1) > 0
            ? AttachmentPointInteractionResult.Changed
            : AttachmentPointInteractionResult.Handled;
    }

    private static Vec4f ParseColor(string value)
    {
        string hex = value?.Trim().TrimStart('#') ?? "";
        if (hex.Length != 6 || !uint.TryParse(hex, NumberStyles.HexNumber,
                CultureInfo.InvariantCulture, out uint rgb))
            throw new FormatException($"Invalid interaction color '{value}'. Expected #RRGGBB.");

        return new(
            ((rgb >> 16) & 0xff) / 255f,
            ((rgb >> 8) & 0xff) / 255f,
            (rgb & 0xff) / 255f,
            0.55f);
    }
}
