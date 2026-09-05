#if VSMCP_BRIDGE
using System;
using System.Linq;
using System.Runtime.CompilerServices;
using ImmersiveModularBackpacks.Attachments;
using ImmersiveBackpacks.blocks;
using ImmersiveBackpacks.inventory;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using VSMCP;

namespace ImmersiveBackpacks.compat;

/// <summary>
/// Dev-only seam for the VSMCP agent bridge: exposes the one thing a test cannot get generically — where a
/// placed bag's attachment points ARE and what they accept. State (what is attached, how many cargo slots)
/// is deliberately not here: the bridge's own `inspect_block` already reads the block entity's persisted
/// tree, which is where all of it lives. Nothing here ships — the whole file compiles out unless the sibling
/// VSMCP build is present (see VSMCP_BRIDGE in the csproj), and even then it stays inert until the bridge
/// mod is actually loaded.
///
/// The VSMCP types are confined to <see cref="Register"/>, which is NoInlining: the runtime only resolves an
/// assembly when it JITs a method mentioning it, so a dev build without the bridge running costs nothing.
/// Pattern lifted from TransformWriteback's BridgeActions.
/// </summary>
internal static class BridgeActions
{
    public static void TryRegister(ICoreClientAPI api)
    {
        if (!api.ModLoader.IsModEnabled("vsmcp")) return;

        try
        {
            Register();
            api.Logger.Notification("[immersivebackpacks] VSMCP detected — registered bag inspection actions.");
        }
        catch (Exception e)
        {
            api.Logger.Warning("[immersivebackpacks] Could not register VSMCP actions: " + e.Message);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Register()
    {
        VsmcpApi.RegisterAction("ib_points_at", ActionSide.Server, (a, c) => PointsAt(a, c),
            "Every attachment point on the placed modular backpack at (x, y, z): the categories it accepts, " +
            "the selection box index that targets it, its world-space bounds, and an 'aim' point to pass " +
            "straight to look(). Geometry only — for what is ATTACHED, read the block entity's placed_addons " +
            "tree with inspect_block.",
            new
            {
                type = "object",
                properties = new
                {
                    x = new { type = "integer" },
                    y = new { type = "integer" },
                    z = new { type = "integer" }
                },
                required = new[] { "x", "y", "z" }
            },
            module: "immersivebackpacks");

        VsmcpApi.RegisterAction("ib_attachment_tree_at", ActionSide.Server, (a, c) => AttachmentTreeAt(a, c),
            "Render-facing attachment tree for a placed modular backpack, including host-owned cargo projected " +
            "into each addon and the children reconstructed from it.",
            new
            {
                type = "object",
                properties = new
                {
                    x = new { type = "integer" },
                    y = new { type = "integer" },
                    z = new { type = "integer" }
                },
                required = new[] { "x", "y", "z" }
            },
            module: "immersivebackpacks");
    }

    private static object AttachmentTreeAt(ActionArgs a, ActionContext c)
    {
        var pos = new BlockPos(a.GetInt("x"), a.GetInt("y"), a.GetInt("z"));
        if (c.Server.World.BlockAccessor.GetBlockEntity(pos) is not BlockEntityImmersiveBackpack be)
            return new { found = false };

        var nodes = be.AttachedItems.Select((stack, i) =>
        {
            if (stack == null) return null;
            var cargo = be.OwnedCargo(i);
            var node = BackpackAttachmentFactory.For(
                stack, c.Server.World, cargo, be.AttachmentPoints[i]);
            return new
            {
                point = be.AttachmentPoints[i].Code,
                tags = (be.AttachmentPoints[i] as ITaggedAttachmentPoint)?.Tags,
                addon = stack.Collectible?.Code?.ToString(),
                cargo = cargo?.Select(s => s?.Collectible?.Code?.ToString()).ToArray(),
                renderKey = node?.RenderKey,
                children = node?.Points.Select(p => new
                {
                    point = p.Code,
                    transform = new { p.Transform.Rotation, p.Transform.Offset, p.Transform.Scale },
                    child = node.GetAttached(p.Code)?.Stack?.Collectible?.Code?.ToString()
                }).ToArray()
            };
        }).Where(n => n != null).ToArray();

        return new { found = true, nodes };
    }

    private static object PointsAt(ActionArgs a, ActionContext c)
    {
        var pos = new BlockPos(a.GetInt("x"), a.GetInt("y"), a.GetInt("z"));
        var accessor = c.Server.World.BlockAccessor;
        var block = accessor.GetBlock(pos);

        if (accessor.GetBlockEntity(pos) is not BlockEntityImmersiveBackpack be)
            return new { found = false, block = block?.Code?.ToString() };

        // Selection boxes are body boxes, real attachment points, then nested interaction points.
        var boxes = block?.GetSelectionBoxes(accessor, pos) ?? [];
        int bodyCount = block?.SelectionBoxes?.Length ?? 0;
        var eye = EyePos(c);
        int selectable = 0;

        var points = be.AttachmentPoints.Select((pt, i) =>
        {
            int selectionBoxIndex = pt.IsVirtual ? -1 : bodyCount + selectable++;
            var box = selectionBoxIndex >= 0 && selectionBoxIndex < boxes.Length
                ? boxes[selectionBoxIndex]
                : pt.Box;
            return new
            {
                code = pt.Code,
                index = i,
                selectionBoxIndex,
                virtualPoint = pt.IsVirtual,
                slots = pt.MemberCodes,
                categories = pt.Categories,
                box = new
                {
                    x1 = pos.X + box.X1, y1 = pos.Y + box.Y1, z1 = pos.Z + box.Z1,
                    x2 = pos.X + box.X2, y2 = pos.Y + box.Y2, z2 = pos.Z + box.Z2
                },
                aim = Aim(pos, box, eye)
            };
        }).ToArray();

        int interactionStart = bodyCount + selectable;
        var player = c.Server.World.AllOnlinePlayers.FirstOrDefault();
        var interactions = be.InteractionTargets().Select((target, i) =>
        {
            var box = boxes[interactionStart + i];
            bool active = false;
            string help = null;
            if (player != null)
            {
                var context = be.InteractionContext(target, player.InventoryManager.ActiveHotbarSlot);
                active = target.Interaction.IsInteractionActive(context);
                if (active) help = target.Interaction.GetInteractionHelpCode(context);
            }
            return new
            {
                owner = be.AttachmentPoints[target.OwnerPointIndex].Code,
                point = target.PointCode,
                selectionBoxIndex = interactionStart + i,
                active,
                help,
                box = new
                {
                    x1 = pos.X + box.X1, y1 = pos.Y + box.Y1, z1 = pos.Z + box.Z1,
                    x2 = pos.X + box.X2, y2 = pos.Y + box.Y2, z2 = pos.Z + box.Z2
                },
                aim = Aim(pos, box, eye)
            };
        }).ToArray();

        return new { found = true, bodyBoxes = bodyCount, points, interactions };
    }

    /// <summary>
    /// A point to aim at that actually HITS this box. The box centre does not: a marker box overlaps the
    /// bag's body box, so a ray from the wrong side reaches the body first and the click lands on the bag
    /// instead of the point. Aiming just inside the face the player is on keeps the point box first along
    /// the ray. Falls back to the centre when there is no player to face.
    /// </summary>
    private static object Aim(BlockPos pos, Cuboidf box, Vec3d eye)
    {
        double cx = pos.X + (box.X1 + box.X2) / 2f;
        double cy = pos.Y + (box.Y1 + box.Y2) / 2f;
        double cz = pos.Z + (box.Z1 + box.Z2) / 2f;
        if (eye == null) return new { x = cx, y = cy, z = cz };

        double dx = eye.X - cx, dy = eye.Y - cy, dz = eye.Z - cz;
        double ax = Math.Abs(dx), ay = Math.Abs(dy), az = Math.Abs(dz);

        if (ax >= ay && ax >= az) cx = FaceInset(pos.X + box.X1, pos.X + box.X2, dx);
        else if (ay >= az) cy = FaceInset(pos.Y + box.Y1, pos.Y + box.Y2, dy);
        else cz = FaceInset(pos.Z + box.Z1, pos.Z + box.Z2, dz);

        return new { x = cx, y = cy, z = cz };
    }

    // 20% in from the face nearest the viewer - inside the box (so the ray registers it) but well clear of
    // the far side, where the body box would win.
    private static double FaceInset(double lo, double hi, double toward)
    {
        double inset = (hi - lo) * 0.2;
        return toward >= 0 ? hi - inset : lo + inset;
    }

    private static Vec3d EyePos(ActionContext c)
    {
        var entity = c.Server.World.AllOnlinePlayers.FirstOrDefault()?.Entity;
        return entity == null ? null : entity.Pos.XYZ.Add(0, entity.LocalEyePos.Y, 0);
    }
}
#endif
