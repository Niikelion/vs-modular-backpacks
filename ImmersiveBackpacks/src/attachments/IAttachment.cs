using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ImmersiveBackpacks.attachments;

/**
 * <summary>
 * A node in the attachment tree: something that can attach to a host AND host its own children — a tool on a
 * strap, a strap on a bag, a bag on an entity. Capabilities are optional facets: graphics (GetShape, mandatory),
 * nesting (Points), lifecycle (OnAttached/OnDetached, live hosts only). A node MUST be reconstructible from its
 * stack's tree state; live (BlockEntity/entity) hosts also get lifecycle and push invalidation, value (ItemStack)
 * hosts reconstruct per render and key caches on ContentHash. See [[attachment-system-design]].
 * </summary>
 */
public interface IAttachment
{
    /// <summary>The stack this node represents; its attribute tree is the node's persisted state.</summary>
    ItemStack Stack { get; }

    /// <summary>Content fingerprint render caches key on, folding this node's stack and its children recursively.</summary>
    int ContentHash { get; }

    /// <summary>This node's render geometry in its own local space, children composed at their points (textures unresolved). Null opts the node out of worn rendering.</summary>
    Shape GetShape(ICoreAPI api);

    /// <summary>Points this node exposes for children (empty = leaf).</summary>
    IReadOnlyList<IAttachmentPoint> Points { get; }

    /// <summary>The child occupying a point, or null. Read-only view over tree state.</summary>
    IAttachment GetAttached(string pointCode);

    /// <summary>Live-host attach; store the host to push invalidation. Not called on value (ItemStack) hosts.</summary>
    void OnAttached(IAttachmentHost host);

    /// <summary>Live-host detach; clear the host reference. Best-effort.</summary>
    void OnDetached();
}

/**
 * <summary>
 * Optional: a node that supplies its own placed/held mesh instead of a baked GetShape — the escape hatch for
 * collectibles whose look is more than a shape tesselation (the lantern's variant textures / glass / glow). Worn
 * still uses GetShape, so implementers supply both; shape-based attachments never implement it.
 * </summary>
 */
public interface IAttachmentMeshSource
{
    MeshData GetMesh(ICoreClientAPI capi);
}

/**
 * <summary>
 * A named place a child attaches. Geometry comes from a slot_&lt;code&gt; marker in the owner's shape, so points
 * work uniformly across block, item, and entity owners. Acceptance is category-based.
 * </summary>
 */
public interface IAttachmentPoint
{
    string Code { get; }

    /// <summary>Whether this point accepts the given attachment (category match, or a custom rule).</summary>
    bool Accepts(IAttachment attachment);

    /// <summary>Marker AABB in raw 16-unit shape space (from the owner shape's <c>slot_&lt;code&gt;</c>). Also the placed selection box.</summary>
    Cuboidf Box { get; }

    /// <summary>Placement anchor for the occupant. Defaults to the box centre when the point has nothing better.</summary>
    Vec3f Origin { get; }

    /// <summary>Transform applied to the occupant, in both the placed/held (mesh) and worn/entity (shape) contexts.</summary>
    AttachmentTransform Transform { get; }
}

/**
 * Abstraction over attachment host.
 */
public interface IAttachmentHost
{
    /**
     * Notifies the host that an attachment has been invalidated.
     */
    void OnAttachmentInvalidated(IAttachment source);
}
