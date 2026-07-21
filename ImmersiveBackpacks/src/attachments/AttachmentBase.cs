using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace ImmersiveBackpacks.attachments;

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
    protected IAttachmentHost Host { get; private set; }

    /// <summary>Points this node hosts. Empty for a leaf. Geometry/acceptance come from the concrete type
    /// (typically read from the node's own shape markers + attribute config).</summary>
    public abstract IReadOnlyList<IAttachmentPoint> Points { get; }

    /// <summary>The child at a point, reconstructed from tree state, or null. Leaves always return null.</summary>
    public abstract IAttachment GetAttached(string pointCode);

    /// <summary>Folds this node's stack with its children recursively, so any nested change bubbles to the
    /// root's hash and every content-keyed render cache misses. Point codes are mixed in position-sensitively
    /// so swapping two children (same set, different points) still changes the hash.</summary>
    public virtual int ContentHash
    {
        get
        {
            int h = 17;
            h = h * 31 + (Stack?.GetHashCode() ?? 0);
            var points = Points;
            if (points != null)
                foreach (var pt in points)
                {
                    h = h * 31 + (pt.Code?.GetHashCode() ?? 0);
                    h = h * 31 + (GetAttached(pt.Code)?.ContentHash ?? 0);
                }
            return h;
        }
    }

    public Shape GetShape(ICoreAPI api) => AttachmentComposer.ComposeShape(api, this);

    /// <summary>The composed placed/held mesh (base + children). Not part of <see cref="IAttachment"/> — the
    /// composer resolves it via <see cref="AttachmentComposer.MeshFor"/>, honouring an
    /// <see cref="IAttachmentMeshSource"/> override — but exposed here for host adapters that want it directly.</summary>
    public MeshData GetComposedMesh(ICoreClientAPI capi) => AttachmentComposer.MeshFor(capi, this);

    public virtual void OnAttached(IAttachmentHost host) => Host = host;
    public virtual void OnDetached() => Host = null;

    /// <summary>Push a coarse "my content changed" to the live host (re-compose model, recompute derived
    /// state, mark dirty). No-op on value hosts, which invalidate structurally via <see cref="ContentHash"/>.</summary>
    protected void Invalidate() => Host?.OnAttachmentInvalidated(this);
}
