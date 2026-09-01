#nullable enable
using System.Collections.Generic;
using Vintagestory.API.Common;

namespace ImmersiveModularBackpacks.Attachments;

/// <summary>
/// Default <see cref="IAttachment"/> node. Supplies everything host-agnostic — identity, a recursive content
/// hash, lifecycle/invalidation plumbing, and shape/mesh composition delegated to <see cref="AttachmentComposer"/>
/// — so a concrete attachment only declares WHAT it hosts (its <see cref="Points"/> and how it resolves an
/// occupant via <see cref="GetAttached"/>). A leaf (a tool) supplies no points and gets rendered as just its
/// own shape; a container (a toolstrap) supplies tool points and its children compose in for free.
///
/// The node is a pure function of its <see cref="Stack"/>'s tree state (see the reconstructible-from-tree
/// contract on <see cref="IAttachment"/>): live hosts additionally get lifecycle + <see cref="Invalidate"/>,
/// but correctness never depends on them. See [[attachment-system-design]].
/// </summary>
public abstract class AttachmentBase(ItemStack stack) : IAttachment
{
    public ItemStack Stack { get; } = stack;

    /// <summary>Live host, set while attached under a BlockEntity/entity. Null on value (ItemStack) hosts.</summary>
    protected IAttachmentHost? Host { get; private set; }

    /// <summary>Points this node hosts. Empty for a leaf. Geometry/acceptance come from the concrete type
    /// (typically read from the node's own shape markers and attribute config).</summary>
    public abstract IReadOnlyList<IAttachmentPoint> Points { get; }

    /// <summary>The child at a point, reconstructed from tree state, or null. Leaves always return null.</summary>
    public abstract IAttachment? GetAttached(string pointCode);

    /// <summary>Folds this node's render state with its children recursively. Point codes are mixed in order so
    /// swapping two children produces a different key.</summary>
    public virtual ulong RenderKey
    {
        get
        {
            var key = new AttachmentRenderKeyBuilder();
            AppendOwnRenderState(ref key);

            var points = Points;
            key.Add(points?.Count ?? -1);
            if (points == null) return key.Build();

            foreach (var pt in points)
            {
                key.Add(pt.Code);
                var child = GetAttached(pt.Code);
                key.Add(child != null);
                if (child != null) key.Add(child.RenderKey);
            }
            return key.Build();
        }
    }

    /// <summary>Adds this node's own render-relevant state. The default conservatively includes all persisted
    /// stack states; specialized nodes can narrow it or append non-persisted state.</summary>
    protected virtual void AppendOwnRenderState(ref AttachmentRenderKeyBuilder key) => key.Add(Stack);

    public Shape? GetShape(ICoreAPI api) => AttachmentComposer.ComposeShape(api, this);

    public virtual void OnAttached(IAttachmentHost host) => Host = host;
    public virtual void OnDetached() => Host = null;

    /// <summary>Push a coarse "my content changed" to the live host (re-compose model, recompute derived
    /// state, mark dirty). No-op on value hosts, which invalidate structurally via <see cref="RenderKey"/>.</summary>
    protected void Invalidate() => Host?.OnAttachmentInvalidated(this);
}
