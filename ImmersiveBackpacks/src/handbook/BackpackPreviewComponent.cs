using System;
using System.Collections.Generic;
using Cairo;
using ImmersiveBackpacks.attachments;
using ImmersiveBackpacks.items;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace ImmersiveBackpacks.handbook;

/// <summary>
/// A handbook widget showing the bag in 3D: drag to turn it, and each attachment point is marked with a dot.
/// Hovering a dot cycles through the addons that point accepts, with the addon's own tooltip.
///
/// The 3D draw is done with our own model matrix rather than <c>RenderItemstackToGui</c>, for two reasons: the
/// rotation has to follow the drag, and the same matrix is what makes hit-testing trivial. Because a GUI model
/// matrix maps model space straight to GUI pixels (the ortho projection and modelview only take pixels to NDC),
/// projecting a point marker is one matrix multiply — no ray casting, no viewport maths.
/// </summary>
public class BackpackPreviewComponent : ItemstackComponentBase
{
    private const float DragDegPerPx = 0.7f;
    private const int CycleMs = 1400;
    private const float DotSize = 11f;

    private readonly ItemStack bagStack;
    private readonly double height;
    private readonly List<Point> points = [];
    private readonly Matrixf modelMat = new();
    private readonly float[] modelViewMat = Mat4f.Create();
    private readonly DummySlot addonSlot = new();

    private MultiTextureMeshRef meshRef;
    private bool meshBuilt;
    private int dotTextureId;

    // Model-space fit, measured from the composed mesh: its centre and the scale that makes its largest
    // dimension fill the widget. Measured rather than assumed so every bag type frames the same way.
    private Vec3f fitCentre = new(0.5f, 0.5f, 0.5f);
    private float fitScale = 1f;

    private float yaw = 25f, pitch = 12f;
    private bool dragging;
    private int lastX, lastY;

    // The point under the cursor, resolved during render (where cursor and projected markers share one
    // coordinate frame) and consumed by the same frame's carousel draw.
    private int hovered = -1;

    // Depth of the model's own centre in the current frame, the plane that splits near-side points from
    // far-side ones. Not the matrix's translation column: that is where model-space (0,0,0) lands, which the
    // rotation moves away from the centre.
    private float centreDepth;

    private sealed class Point
    {
        public Vec3f Anchor;            // model space [0,1]
        public ItemStack[] Candidates;
        public float X, Y, Z;           // projected GUI pixels, per frame
    }

    /// <param name="addonGroups">Every attachable addon, grouped by type (the handbook's own list); each group's
    /// first stack represents it. Filtered per point through the point's own acceptance rule.</param>
    public BackpackPreviewComponent(ICoreClientAPI capi, ItemStack bagStack, double unscaledHeight,
        ItemStack[][] addonGroups) : base(capi)
    {
        this.bagStack = bagStack;
        height = GuiElement.scaled(unscaledHeight);
        Float = EnumFloat.None;
        BoundsPerLine = [new LineRectangled(0.0, 0.0, 0.0, height)];

        BuildPoints(capi, addonGroups);
    }

    // One entry per attachment point, anchored on its slot_<code> marker when the shape has one
    // (16-unit -> [0,1]), else on the point's own anchor.
    private void BuildPoints(ICoreClientAPI capi, ItemStack[][] addonGroups)
    {
        if (bagStack.Collectible is not ItemImmersiveBag bag) return;

        var node = bag.BagNodeFor(bagStack);
        // The same shape the mesh composer reads its markers from, so the dots land where addons would.
        var shape = AttachmentMesh.AttachedShapeComposite(bag) ?? bag.Shape;
        var markers = AttachmentMesh.ReadSlots(capi, shape?.Base?.ToString(), bag.Code.Domain);

        foreach (var pt in node.Points)
        {
            // The marker's box centre, not its pivot: the pivot sits on a box corner (where an addon is
            // anchored), while the centre is what reads as "the slot" and is the better thing to aim at.
            Vec3f anchor;
            if (markers.TryGetValue(pt.Code, out var marker) && marker.Box != null)
                anchor = new Vec3f((marker.Box.X1 + marker.Box.X2) / 32f,
                                   (marker.Box.Y1 + marker.Box.Y2) / 32f,
                                   (marker.Box.Z1 + marker.Box.Z2) / 32f);
            else if (pt.Box != null) anchor = pt.Origin;
            else continue;

            var accepted = new List<ItemStack>();
            foreach (var group in addonGroups ?? [])
            {
                if (group.Length == 0) continue;
                if (pt.Accepts(AttachmentFactory.For(group[0], capi.World))) accepted.Add(group[0]);
            }

            points.Add(new Point { Anchor = anchor, Candidates = accepted.ToArray() });
        }
    }

    public override EnumCalcBoundsResult CalcBounds(TextFlowPath[] flowPath, double currentLineHeight,
        double offsetX, double lineY, out double nextOffsetX)
    {
        var section = GetCurrentFlowPathSection(flowPath, lineY);
        BoundsPerLine[0].X = 0.0;
        BoundsPerLine[0].Y = lineY + (offsetX > 0.0 ? currentLineHeight : 0.0);
        BoundsPerLine[0].Width = section.X2 - section.X1;
        BoundsPerLine[0].Height = height;
        nextOffsetX = 0.0;
        return EnumCalcBoundsResult.Nextline;
    }

    public override void ComposeElements(Context ctx, ImageSurface surface) { }

    public override void RenderInteractiveElements(float deltaTime, double renderX, double renderY, double renderZ)
    {
        EnsureResources();
        if (meshRef == null) return;

        var rect = BoundsPerLine[0];
        double x = renderX + rect.X + offX, y = renderY + rect.Y + offY;

        var clip = ElementBounds.FixedSize((int)(rect.Width / RuntimeEnv.GUIScale), (int)(rect.Height / RuntimeEnv.GUIScale));
        clip.ParentBounds = capi.Gui.WindowBounds;
        clip.CalcWorldBounds();
        clip.absFixedX = x;
        clip.absFixedY = y;
        capi.Render.PushScissor(clip, stacking: true);

        // Model space -> GUI pixels. Y is negated because GUI y grows downward while the model's grows up.
        float scale = (float)(height * 0.78) * fitScale;
        modelMat.Identity()
            .Translate(x + rect.Width / 2.0, y + rect.Height / 2.0, renderZ + 50.0)
            .Scale(scale, -scale, scale)
            .RotateXDeg(pitch)
            .RotateYDeg(yaw)
            .Translate(-fitCentre.X, -fitCentre.Y, -fitCentre.Z);

        RenderBag(renderZ);
        capi.Render.PopScissor();

        ProjectPoints();
        hovered = PickPoint(capi.Input.MouseX, capi.Input.MouseY);
        DrawDots(renderZ);
        DrawCarousel(x, y, renderZ, deltaTime);
    }

    private void RenderBag(double renderZ)
    {
        // gui.vsh: gl_Position = projectionMatrix * modelViewMatrix * vertex, and modelMatrix only orients
        // normals for the shading term. So the modelview carries our matrix; the projection stays the GUI's.
        Mat4f.Mul(modelViewMat, capi.Render.CurrentModelviewMatrix, modelMat.Values);

        var prog = capi.Render.CurrentActiveShader;
        prog.Uniform("rgbaIn", new Vec4f(1f, 1f, 1f, 1f));
        prog.Uniform("rgbaGlowIn", new Vec4f(0f, 0f, 0f, 0f));
        prog.Uniform("extraGlow", 0);
        prog.Uniform("applyColor", 0);
        prog.Uniform("tempGlowMode", 0);
        prog.Uniform("damageEffect", 0f);
        prog.Uniform("overlayOpacity", 0f);
        prog.Uniform("normalShaded", 1);
        prog.Uniform("alphaTest", 0.005f);
        prog.UniformMatrix("projectionMatrix", capi.Render.CurrentProjectionMatrix);
        prog.UniformMatrix("modelViewMatrix", modelViewMat);
        prog.UniformMatrix("modelMatrix", modelMat.Values);
        prog.Uniform("applyModelMat", 1);

        capi.Render.RenderMultiTextureMesh(meshRef, "tex2d");

        // Leave the shader as the rest of the GUI expects to find it.
        prog.Uniform("applyModelMat", 0);
        prog.Uniform("normalShaded", 0);
        prog.Uniform("alphaTest", 0f);
    }

    private void ProjectPoints()
    {
        centreDepth = Mat4f.MulWithVec4(modelMat.Values, fitCentre.X, fitCentre.Y, fitCentre.Z, 1f)[2];

        foreach (var p in points)
        {
            var v = Mat4f.MulWithVec4(modelMat.Values, p.Anchor.X, p.Anchor.Y, p.Anchor.Z, 1f);
            p.X = v[0];
            p.Y = v[1];
            p.Z = v[2];
        }
    }

    // Nearest projected marker within a grab radius, front-facing only: a point on the far side of the bag
    // projects onto the same pixels as a near one, and the near one is the one the player means.
    private int PickPoint(int mouseX, int mouseY)
    {
        double radius = GuiElement.scaled(11.0);
        double bestDist = radius * radius;
        int best = -1;

        for (int i = 0; i < points.Count; i++)
        {
            var p = points[i];
            if (p.Z < centreDepth) continue;
            double dx = p.X - mouseX, dy = p.Y - mouseY;
            double dist = dx * dx + dy * dy;
            if (dist > bestDist) continue;
            bestDist = dist;
            best = i;
        }
        return best;
    }

    private void DrawDots(double renderZ)
    {
        if (dotTextureId == 0) return;

        var idle = new Vec4f(1f, 0.94f, 0.75f, 0.75f);
        var hot = new Vec4f(0.4f, 1f, 0.45f, 1f);

        for (int i = 0; i < points.Count; i++)
        {
            var p = points[i];
            bool front = p.Z >= centreDepth;
            if (!front && i != hovered) continue;

            float size = (float)GuiElement.scaled(i == hovered ? DotSize * 1.6 : DotSize);
            capi.Render.Render2DTexturePremultipliedAlpha(dotTextureId,
                p.X - size / 2f, p.Y - size / 2f, size, size, (float)renderZ + 60f,
                i == hovered ? hot : idle);
        }
    }

    // The hovered point's candidates, cycling. Drawn at a fixed corner of the widget rather than floating by
    // the dot: it never overlaps the model or the dot, and it stays put while the carousel advances.
    private void DrawCarousel(double x, double y, double renderZ, float deltaTime)
    {
        if (dragging || hovered < 0) return;
        var candidates = points[hovered].Candidates;
        if (candidates.Length == 0) return;

        // Cycles on a wall clock so every point's carousel advances at the same visible rate.
        var stack = candidates[(int)(capi.ElapsedMilliseconds / CycleMs % candidates.Length)];
        addonSlot.Itemstack = stack;

        float size = (float)GuiElement.scaled(28.0);
        capi.Render.RenderItemstackToGui(addonSlot,
            x + size, y + size,
            GuiElement.scaled(100.0), size, -1, shading: true, rotate: false, showStackSize: false);
        RenderItemstackTooltip(addonSlot, capi.Input.MouseX + offX, capi.Input.MouseY + offY, deltaTime);
    }

    // Mesh and textures are GL resources, so they are built on the first frame rather than in the constructor -
    // a handbook page can be composed off the main thread.
    private void EnsureResources()
    {
        if (meshBuilt) return;
        meshBuilt = true;

        if (bagStack.Collectible is ItemImmersiveBag bag)
        {
            var mesh = AttachmentComposer.ComposeMesh(capi, bag.BagNodeFor(bagStack));
            if (mesh != null)
            {
                var (centre, size) = AttachmentMesh.Bounds(mesh);
                fitCentre = centre;
                float largest = Math.Max(size.X, Math.Max(size.Y, size.Z));
                if (largest > 0.001f) fitScale = 1f / largest;
                meshRef = capi.Render.UploadMultiTextureMesh(mesh);
            }
        }

        dotTextureId = GenDotTexture();
    }

    // A soft white disc, tinted per-dot at draw time.
    private int GenDotTexture()
    {
        const int px = 32;
        using var surface = new ImageSurface(Format.Argb32, px, px);
        using var ctx = new Context(surface);
        ctx.SetSourceRGBA(0.0, 0.0, 0.0, 0.55);
        ctx.Arc(px / 2.0, px / 2.0, px / 2.0 - 1.0, 0.0, Math.PI * 2.0);
        ctx.Fill();
        ctx.SetSourceRGBA(1.0, 1.0, 1.0, 1.0);
        ctx.Arc(px / 2.0, px / 2.0, px / 2.0 - 5.0, 0.0, Math.PI * 2.0);
        ctx.Fill();
        return capi.Gui.LoadCairoTexture(surface, true);
    }

    public override void OnMouseDown(MouseEvent args)
    {
        if (!BoundsPerLine[0].PointInside(args.X, args.Y)) return;
        dragging = true;
        lastX = args.X;
        lastY = args.Y;
        args.Handled = true;
    }

    public override void OnMouseMove(MouseEvent args)
    {
        if (!dragging) return;
        yaw += (args.X - lastX) * DragDegPerPx;
        pitch = GameMath.Clamp(pitch + (args.Y - lastY) * DragDegPerPx, -80f, 80f);
        lastX = args.X;
        lastY = args.Y;
        args.Handled = true;
    }

    public override void OnMouseUp(MouseEvent args) => dragging = false;

    public override void Dispose()
    {
        base.Dispose();
        meshRef?.Dispose();
        meshRef = null;
        if (dotTextureId != 0) capi.Render.GLDeleteTexture(dotTextureId);
        dotTextureId = 0;
    }
}
