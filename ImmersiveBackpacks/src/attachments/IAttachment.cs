using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ImmersiveBackpacks.attachments;

/// <summary>
/// A node in the attachment tree. This is the whole generic system's primitive: not "a bag addon" but a
/// thing that can be attached to something AND can have things attached to it. "Host" and "attachment" are
/// roles the same node plays, so the tree is uniform — a tool on a strap, a strap on a bag, a bag on an
/// entity, a module in a structure slot are all <see cref="IAttachment"/>, differing only in which optional
/// facets they populate.
///
/// Capabilities are optional facets, not subtypes:
///   * graphics — <see cref="GetShape"/> is mandatory (every node renders); a mesh is an optional override
///     via <see cref="IAttachmentMeshSource"/>.
///   * storage — <see cref="Inventory"/> is null when the node contributes none (a bare tool leaf).
///   * nesting — <see cref="Points"/> is empty for a leaf; non-empty makes the node a host for children.
///   * behaviour — <see cref="OnAttached"/>/<see cref="OnDetached"/> fire on live hosts only (see below).
///
/// The three render paths consume this differently — placed/held bake a mesh, worn/entity feed the shape
/// tree straight into vanilla's gear pipeline — which is why the primary form is a <see cref="Shape"/> (tree,
/// unresolved texture codes, animatable, entity-atlas-resolvable) rather than a baked mesh: you can bake a
/// tree to a mesh but not the reverse. See [[attachment-system-design]].
///
/// LIVE vs VALUE hosts: a node hosted by a BlockEntity or entity behaviour is a long-lived instance and gets
/// real lifecycle + push invalidation. A node hosted by an ItemStack is reconstructed from the stack's
/// attribute tree every render and keys caches on <see cref="ContentHash"/> instead. The hard contract that
/// keeps both honest: a node MUST be fully reconstructible from its tree state; lifecycle is best-effort.
/// </summary>
public interface IAttachment
{
    /// <summary>The stack this node represents. Its attribute tree is the node's persisted state, so the
    /// whole node (including nested children and inventory) round-trips through it.</summary>
    ItemStack Stack { get; }

    /// <summary>
    /// Content fingerprint that render caches key on, folding this node's stack AND its children/inventory
    /// recursively. Changing a nested tool bubbles up to the root's hash so held/worn caches miss and the
    /// placed renderer's key changes. On value hosts this is the only invalidation signal; live hosts also
    /// get the push via <see cref="IAttachmentHost.OnAttachmentInvalidated"/>.
    /// </summary>
    int ContentHash { get; }

    /// <summary>
    /// This node's render geometry in its OWN local space, already composed with its occupied children at
    /// their point transforms (recursive). The parent positions the whole result at its point marker. Textures
    /// travel on the returned <see cref="Shape"/> (codes, unresolved) so the worn/entity pipeline can resolve
    /// them into the entity atlas. This is the mandatory floor — a node with no faithful shape opts out of
    /// worn rendering by returning null, it does not get to skip the method.
    /// </summary>
    Shape GetShape(ICoreAPI api);

    /// <summary>The node's own storage, or null if it contributes none. Flattened into the host's addressable
    /// inventory by the proxy; each slot carries its own filter/colour spec so the proxy is a pure concat.</summary>
    IInventory Inventory { get; }

    /// <summary>Attachment points this node exposes for children (empty = leaf). Marker-derived geometry +
    /// acceptance rule + placement transform. Non-empty makes this node a host in its own right.</summary>
    IReadOnlyList<IAttachmentPoint> Points { get; }

    /// <summary>The child occupying a point, or null if empty/unknown. Read-only view over current tree state;
    /// attach/detach + persistence is the host adapter's job.</summary>
    IAttachment GetAttached(string pointCode);

    /// <summary>Called when placed under a live host (BlockEntity/entity). Store the host to push invalidation.
    /// Not called on value (ItemStack) hosts — those reconstruct per render, so keep behaviour tree-derived.</summary>
    void OnAttached(IAttachmentHost host);

    /// <summary>Called when removed from a live host. Clear the host reference. Best-effort, live hosts only.</summary>
    void OnDetached();
}

/// <summary>
/// Optional capability: a node that supplies its own authoritative placed/held mesh instead of letting the
/// system bake <see cref="IAttachment.GetShape"/>. This is the escape hatch for collectibles whose appearance
/// is more than a plain tesselation of a shape file — VS <c>IContainedMeshSource</c> blocks like the lantern,
/// whose variant textures / glass render-pass flags / glow are baked by their own GenMesh and would be lost
/// by a naive shape bake. Worn still uses GetShape (there is no mesh path there), so implementers must supply
/// both. The composer prefers this for placed/held when present, and bakes GetShape otherwise — so ordinary
/// shape-based attachments (tools, straps, pouches) never implement it.
/// </summary>
public interface IAttachmentMeshSource
{
    MeshData GetMesh(ICoreClientAPI capi);
}

/// <summary>
/// A named place a child attaches, on a host or on a nesting attachment. Geometry comes from a
/// <c>slot_&lt;code&gt;</c> marker in the owner's shape (position/size/composed rotation), so points work
/// uniformly across block, item and entity owners — all have shapes. Acceptance is category-based; the
/// shift-click interaction cycles the accepted, available stacks in the tooltip.
/// </summary>
public interface IAttachmentPoint
{
    string Code { get; }

    /// <summary>Categories this point accepts (an attachment declares its own category); the basis of
    /// <see cref="Accepts"/> and of the tooltip's "available attachments" cycle.</summary>
    IReadOnlyList<string> Categories { get; }

    bool Accepts(ItemStack stack);

    /// <summary>Marker AABB in raw 16-unit shape space (from the owner shape's <c>slot_&lt;code&gt;</c>).</summary>
    Cuboidf Box { get; }

    /// <summary>Composed marker orientation as XYZ Euler degrees.</summary>
    float[] Rotation { get; }

    /// <summary>Render transform applied to the occupant in the placed/held (mesh) contexts.</summary>
    AttachmentTransform Placed { get; }

    /// <summary>Render transform applied to the occupant in the worn/entity (shape) context.</summary>
    AttachmentTransform Worn { get; }
}

/// <summary>
/// The live owner a node pushes invalidation to (a BlockEntity or entity behaviour). The only method is a
/// coarse "something about this attachment changed, re-derive content" — the host reacts by re-composing the
/// model, recomputing derived state (e.g. emitted light) and marking itself dirty for save + sync. It is
/// deliberately NOT a layout change: adding/removing slots is attach/detach territory, handled separately.
/// Value (ItemStack) hosts never receive this — they invalidate structurally via <see cref="IAttachment.ContentHash"/>.
/// </summary>
public interface IAttachmentHost
{
    void OnAttachmentInvalidated(IAttachment source);
}
