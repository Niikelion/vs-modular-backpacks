using System;
using System.Collections.Generic;
using Cairo;
using ImmersiveModularBackpacks.Attachments;
using ImmersiveBackpacks.items;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace ImmersiveBackpacks.handbook;

/// <summary>
/// A handbook widget showing the bag in 3D: drag to turn it, and each attachment point is marked with a square.
/// Hovering a marker lists every addon that point accepts, each with the addon's own tooltip.
///
/// The 3D draw is done with our own model matrix rather than <c>RenderItemstackToGui</c>, for two reasons: the
/// rotation has to follow the drag, and the same matrix is what makes hit-testing trivial. Because a GUI model
/// matrix maps model space straight to GUI pixels (the ortho projection and modelview only take pixels to NDC),
/// projecting a point marker is one matrix multiply — no ray casting, no viewport maths.
/// </summary>
public class BackpackPreviewComponent : ItemstackComponentBase
{
    private const float DragDegPerPx = 0.7f;
    private const float MarkerSize = 11f;
    private const double CandidateCell = 28.0;
    private const double CandidateGap = 12.0;

    // Share of the widget's width the grid may use before wrapping. The model is centred and about its own
    // height wide, so keeping the grid inside the left third means the two never overlap.
    private const double CandidateWidthShare = 0.3;
    private const int VariantCycleMs = 1400;

    // Facing band, in the cosine between a point's outward direction and the view. Fully drawn from FadeIn up,
    // gone below FadeOut, ramped between - a hard cut would make points on the silhouette blink during a drag.
    private const float FadeIn = 0.1f;
    private const float FadeOut = -0.35f;

    private readonly ItemStack bagStack;
    private readonly double height;
    private readonly List<Point> points = [];
    private readonly Matrixf modelMat = new();
    private readonly Matrixf boxMat = new();
    private readonly float[] modelViewMat = Mat4f.Create();
    private readonly DummySlot addonSlot = new();

    private MultiTextureMeshRef meshRef;
    private MeshRef boxMeshRef;
    private bool meshBuilt;
    private int markerTextureId;

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

    // Depth of the model's own centre in the current frame, the plane the points' depths are measured against.
    // Not the matrix's translation column: that is where model-space (0,0,0) lands, which the rotation moves
    // away from the centre.
    private float centreDepth;

    private sealed class Point
    {
        public Vec3f Anchor;            // model space [0,1]
        public Cuboidf Box;             // the point's own bounds, model space, drawn as the hover outline
        public float Radius;            // model-space distance from the fit centre, for the facing cosine
        public ItemStack[][] Candidates; // one accepted addon type per entry, holding that type's variants
        public float X, Y;              // projected GUI pixels, per frame
        public float Opacity;           // the facing cosine put through the fade band
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

    // One entry per real point. Its candidates include available virtual points that span it.
    private void BuildPoints(ICoreClientAPI capi, ItemStack[][] addonGroups)
    {
        if (bagStack.Collectible is not ItemImmersiveBag bag) return;

        var node = bag.BagNodeFor(bagStack);
        var occupants = new ItemStack[node.Points.Count];
        for (int i = 0; i < node.Points.Count; i++)
            occupants[i] = node.GetAttached(node.Points[i].Code)?.Stack;

        for (int pointIndex = 0; pointIndex < node.Points.Count; pointIndex++)
        {
            var pt = node.Points[pointIndex];
            if (pt.IsVirtual || pt.Box == null) continue;
            var box = pt.Box;
            var anchor = new Vec3f((box.X1 + box.X2) / 2f, (box.Y1 + box.Y2) / 2f,
                (box.Z1 + box.Z2) / 2f);
            var available = AttachmentPointRouting.AvailablePointsAt(node.Points, occupants, pointIndex);

            // Whole groups, not just their first stack: the cell then cycles that addon's variants the way the
            // handbook's own addon row does. A group's stacks only differ by variant, so testing one is enough.
            var accepted = new List<ItemStack[]>();
            foreach (var group in addonGroups ?? [])
            {
                if (group.Length == 0) continue;
                var candidate = AttachmentFactory.For(group[0], capi.World);
                foreach (var point in available)
                    if (point.Accepts(candidate))
                    {
                        accepted.Add(group);
                        break;
                    }
            }

            points.Add(new Point { Anchor = anchor, Box = box, Candidates = accepted.ToArray() });
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

        // The model occupies GUI depth too: rotated, it reaches half its diagonal (<= 0.87 * scale) either side
        // of its centre. Overlays have to clear that, or the near face depth-tests over the markers in front of it.
        float overlayZ = (float)(renderZ + 50.0) + scale;
        modelMat.Identity()
            .Translate(x + rect.Width / 2.0, y + rect.Height / 2.0, renderZ + 50.0)
            .Scale(scale, -scale, scale)
            .RotateXDeg(pitch)
            .RotateYDeg(yaw)
            .Translate(-fitCentre.X, -fitCentre.Y, -fitCentre.Z);

        RenderBag(renderZ);
        // The outline belongs to the previous frame's hover: picking needs this frame's projection, which the
        // model matrix above has only just settled. A frame of lag is invisible and it keeps one matrix build.
        DrawHoveredBox();

        ProjectPoints(scale);
        // A drag holds the current hover: the cursor sweeps across the model then, and letting it pick would
        // flick the highlight from point to point while the reader is only turning the bag.
        if (!dragging) hovered = PickPoint(capi.Input.MouseX, capi.Input.MouseY);
        DrawMarkers(overlayZ);
        DrawCandidates(x, y, rect.Width, overlayZ);

        // Everything the widget draws stays inside its own box - a page scrolled so the preview is half cut off
        // would otherwise have markers and addon cells painting over the text around it.
        capi.Render.PopScissor();
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

    // The hovered point's own bounds, as a wireframe box in the model's frame. Depth testing is off for it so
    // the near face of the bag doesn't hide the edges that wrap behind the slot.
    private void DrawHoveredBox()
    {
        if (boxMeshRef == null || hovered < 0) return;
        var box = points[hovered].Box;

        // The line cube spans -1..1, so half the box's size is its scale.
        Array.Copy(modelMat.Values, boxMat.Values, modelMat.Values.Length);
        boxMat.Translate((box.X1 + box.X2) / 2f, (box.Y1 + box.Y2) / 2f, (box.Z1 + box.Z2) / 2f)
              .Scale(box.Width / 2f, box.Height / 2f, box.Length / 2f);

        Mat4f.Mul(modelViewMat, capi.Render.CurrentModelviewMatrix, boxMat.Values);

        var prog = capi.Render.CurrentActiveShader;
        prog.Uniform("rgbaIn", new Vec4f(0.4f, 1f, 0.45f, 1f));
        prog.Uniform("applyColor", 0);
        prog.Uniform("noTexture", 1f);
        prog.UniformMatrix("projectionMatrix", capi.Render.CurrentProjectionMatrix);
        prog.UniformMatrix("modelViewMatrix", modelViewMat);

        capi.Render.LineWidth = 2f;
        capi.Render.GLDisableDepthTest();
        capi.Render.RenderMesh(boxMeshRef);
        capi.Render.GLEnableDepthTest();
        capi.Render.LineWidth = 1f;

        // Leave the shader as the rest of the GUI expects to find it.
        prog.Uniform("noTexture", 0f);
        prog.Uniform("rgbaIn", new Vec4f(1f, 1f, 1f, 1f));
    }

    // Projects every marker and works out how much of it the viewer can see. A point's outward direction is
    // taken from the fit centre, so its rotated depth divided by its (scaled) radius is the cosine against the
    // view - no per-point normal needed, and it is scale-free, so one fade band suits every bag and point.
    private void ProjectPoints(float scale)
    {
        centreDepth = Mat4f.MulWithVec4(modelMat.Values, fitCentre.X, fitCentre.Y, fitCentre.Z, 1f)[2];

        foreach (var p in points)
        {
            var v = Mat4f.MulWithVec4(modelMat.Values, p.Anchor.X, p.Anchor.Y, p.Anchor.Z, 1f);
            p.X = v[0];
            p.Y = v[1];

            float span = scale * p.Radius;
            float facing = span > 0.001f ? (v[2] - centreDepth) / span : 1f;
            p.Opacity = GameMath.Clamp((facing - FadeOut) / (FadeIn - FadeOut), 0f, 1f);
        }
    }

    // Nearest marker within a grab radius, ignoring the ones that have faded out: a point on the far side of the
    // bag projects onto the same pixels as a near one, and the near one is the one the player means.
    private int PickPoint(int mouseX, int mouseY)
    {
        double radius = GuiElement.scaled(11.0);
        double bestDist = radius * radius;
        int best = -1;

        for (int i = 0; i < points.Count; i++)
        {
            var p = points[i];
            if (p.Opacity < 0.5f) continue;
            double dx = p.X - mouseX, dy = p.Y - mouseY;
            double dist = dx * dx + dy * dy;
            if (dist > bestDist) continue;
            bestDist = dist;
            best = i;
        }
        return best;
    }

    private void DrawMarkers(float overlayZ)
    {
        if (markerTextureId == 0) return;

        for (int i = 0; i < points.Count; i++)
        {
            var p = points[i];
            bool hot = i == hovered;
            float opacity = hot ? 1f : p.Opacity;
            if (opacity <= 0.01f) continue;

            // The texture is premultiplied, so the tint fades on every channel, not just alpha.
            var colour = hot
                ? new Vec4f(0.4f, 1f, 0.45f, 1f)
                : new Vec4f(1f * opacity, 0.94f * opacity, 0.75f * opacity, 0.75f * opacity);
            float size = (float)GuiElement.scaled(hot ? MarkerSize * 1.6 : MarkerSize);
            capi.Render.Render2DTexturePremultipliedAlpha(markerTextureId,
                p.X - size / 2f, p.Y - size / 2f, size, size, overlayZ, colour);
        }
    }

    // Everything the selected point accepts, all at once, as a grid starting at the widget's top-left - one cell
    // per addon type, cycling that type's variants. Cells run left to right and wrap onto the next row at the end
    // of their share of the width, clear of the model. Shown only while a marker is hovered - there is no
    // selection to click into, so the grid is a read-only answer to "what fits here".
    private void DrawCandidates(double x, double y, double width, float overlayZ)
    {
        if (dragging || hovered < 0) return;
        var candidates = points[hovered].Candidates;
        if (candidates.Length == 0) return;

        double cell = GuiElement.scaled(CandidateCell);
        double gap = GuiElement.scaled(CandidateGap);
        double margin = GuiElement.scaled(6.0);
        int columns = Math.Max(1, (int)((width * CandidateWidthShare - margin + gap) / (cell + gap)));

        for (int i = 0; i < candidates.Length; i++)
        {
            double left = x + margin + i % columns * (cell + gap);
            double top = y + margin + i / columns * (cell + gap);

            // Variants cycle on a wall clock, so every cell advances at the same visible rate.
            var variants = candidates[i];
            addonSlot.Itemstack = variants[(int)(capi.ElapsedMilliseconds / VariantCycleMs % variants.Length)];

            // x/y are the cell's centre for this call, unlike the 2D texture draws above.
            capi.Render.RenderItemstackToGui(addonSlot, left + cell / 2.0, top + cell / 2.0,
                overlayZ, (float)cell, -1, shading: true, rotate: false, showStackSize: false);
        }
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

        // Only now is the fit centre known, so this is where each point's outward radius can be taken.
        foreach (var p in points) p.Radius = p.Anchor.DistanceTo(fitCentre);

        markerTextureId = GenMarkerTexture();
        boxMeshRef = capi.Render.UploadMesh(LineMeshUtil.GetCube(ColorUtil.WhiteArgb));
    }

    // A white square on a dark outline, tinted per-marker at draw time. Square rather than round: it reads as a
    // slot - the thing an addon sits in - and its edges give the eye something to judge the bag's facing by.
    private int GenMarkerTexture()
    {
        const int px = 32;
        const double border = 4.0;
        using var surface = new ImageSurface(Format.Argb32, px, px);
        using var ctx = new Context(surface);
        ctx.SetSourceRGBA(0.0, 0.0, 0.0, 0.55);
        ctx.Rectangle(1.0, 1.0, px - 2.0, px - 2.0);
        ctx.Fill();
        ctx.SetSourceRGBA(1.0, 1.0, 1.0, 1.0);
        ctx.Rectangle(border, border, px - border * 2.0, px - border * 2.0);
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
        boxMeshRef?.Dispose();
        boxMeshRef = null;
        if (markerTextureId != 0) capi.Render.GLDeleteTexture(markerTextureId);
        markerTextureId = 0;
    }
}
