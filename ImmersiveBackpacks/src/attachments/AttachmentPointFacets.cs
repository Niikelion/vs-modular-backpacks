#nullable enable
using System.Collections.Generic;

namespace ImmersiveModularBackpacks.Attachments;

/// <summary>Optional metadata carried by an attachment point.</summary>
public interface ITaggedAttachmentPoint
{
    IReadOnlyList<string> Tags { get; }
}

/// <summary>Optional attachment facet receiving the point that currently hosts it.</summary>
public interface IAttachmentPointContextReceiver
{
    void SetAttachmentPointContext(IAttachmentPoint point);
}
