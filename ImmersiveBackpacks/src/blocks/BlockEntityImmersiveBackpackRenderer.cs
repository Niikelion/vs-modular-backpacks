using System;
using System.Collections.Generic;
using ImmersiveBackpacks.attachments;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ImmersiveBackpacks.blocks;

public class BlockEntityImmersiveBackpackRenderer(BlockPos pos, ICoreClientAPI capi, BlockEntityImmersiveBackpack be)
    : IRenderer
{
    private MeshRef bodyMeshRef;
    // Multi-texture so a composed addon whose faces span both atlases (item-atlas strap + block-atlas tool)
    // binds each atlas per sub-mesh, instead of one atlas for the whole addon.
    private readonly Dictionary<string, MultiTextureMeshRef> attachmentMeshRefs = new();
    private readonly Dictionary<string, MultiTextureMeshRef> attachmentTransparentRefs = new();
    private readonly HashSet<string> builtKeys = new();
    // Current cache key per attachment point, set on rebuild and read by the draw loop, so a frame needn't
    // rebuild a node just to look its mesh up.
    private string[] pointKeys = Array.Empty<string>();
    private bool dirty = true;
    // The generation these meshes were composed at. A live /tfedit tweak changes neither the addon placement nor
    // its contents, so nothing else would invalidate them - and a tool's transform is baked into its toolstrap's
    // composed mesh, so it can only be seen by rebuilding.
    private int builtGeneration = -1;

    private readonly Matrixf modelMat = new();
    // Reused buffer for the transparent draws collected during the opaque pass.
    private readonly List<(float[] matrix, MultiTextureMeshRef mesh)> transparentDraws = new();

    public double RenderOrder => 0.5;
    public int RenderRange => 24;

    public void MarkDirty() => dirty = true;

    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        if (builtGeneration != AttachmentTransform.TuningGeneration) dirty = true;
        if (dirty) RebuildMeshes();

        if (bodyMeshRef == null) return;

        var rpi = capi.Render;
        var camPos = capi.World.Player.Entity.CameraPos;
        var lightRgbs = capi.World.BlockAccessor.GetLightRGBs(pos.X, pos.Y, pos.Z);

        IStandardShaderProgram prog = rpi.StandardShader;
        prog.Use();
        prog.RgbaAmbientIn = rpi.AmbientColor;
        prog.RgbaFogIn = rpi.FogColor;
        prog.FogMinIn = rpi.FogMin;
        prog.FogDensityIn = rpi.FogDensity;
        prog.RgbaTint = ColorUtil.WhiteArgbVec;
        prog.DontWarpVertices = 0;
        prog.AddRenderFlags = 0;
        prog.NormalShaded = 1;
        prog.RgbaLightIn = lightRgbs;
        prog.RgbaGlowIn = new Vec4f(0, 0, 0, 0);
        prog.ExtraGlow = 0;
        prog.OverlayOpacity = 0;
        prog.ViewMatrix = rpi.CameraMatrixOriginf;
        prog.ProjectionMatrix = rpi.CurrentProjectionMatrix;

        rpi.GlEnableCullFace();

        float angle = be.MeshAngleRad;

        // Body (always an item, fully opaque). Rotated about the block's vertical centre axis.
        prog.Tex2D = capi.ItemTextureAtlas.AtlasTextures[0].TextureId;
        prog.ModelMatrix = modelMat.Identity()
            .Translate(pos.X - camPos.X + 0.5, pos.Y - camPos.Y, pos.Z - camPos.Z + 0.5)
            .RotateY(angle)
            .Translate(-0.5, 0, -0.5)
            .Values;
        rpi.RenderMesh(bodyMeshRef);

        // Attachments: opaque now, transparent (e.g. lantern glass) collected for a blended second pass.
        // No culling: addon meshes like the linensack sack have open/flap geometry (some faces disabled)
        // that needs both sides drawn. RenderMultiTextureMesh binds each sub-mesh's atlas per draw.
        rpi.GlDisableCullFace();
        transparentDraws.Clear();
        const float d2r = (float)(Math.PI / 180.0);
        for (int i = 0; i < be.AttachmentPoints.Length; i++)
        {
            var stack = be.AttachedItems[i];
            if (stack == null) continue;
            var key = i < pointKeys.Length ? pointKeys[i] : null;
            if (key == null) continue;
            bool hasOpaque = attachmentMeshRefs.TryGetValue(key, out var opaqueRef);
            bool hasTransp = attachmentTransparentRefs.TryGetValue(key, out var transpRef);
            if (!hasOpaque && !hasTransp) continue;                       // addon had no mesh; skip

            var point = be.AttachmentPoints[i];
            var anchor = point.Origin;                                   // marker pivot, [0,1]
            var origin = AttachmentMesh.ModelOrigin(stack.Collectible);  // addon's fixed model origin

            // Apply the point's placed transform combined with the item override (no hitbox auto-fit).
            var tf = point.Transform.CombinedWith(AttachmentTransform.ForItem(stack.Collectible, "placed"));
            float scale = tf.Scale;

            // Rotate about the block centre (placement orientation), then position at the point's anchor
            // (relative to that centre) plus the transform offset, then rotate/scale and align the addon's
            // model origin. The addon rotation comes from the composed shape slot (X,Y,Z order, as authored).
            float[] matrix = modelMat.Identity()
                .Translate(pos.X - camPos.X + 0.5, pos.Y - camPos.Y, pos.Z - camPos.Z + 0.5)
                .RotateY(angle)
                .Translate(anchor.X - 0.5, anchor.Y, anchor.Z - 0.5)
                .RotateX(tf.Rotation[0] * d2r)
                .RotateY(tf.Rotation[1] * d2r)
                .RotateZ(tf.Rotation[2] * d2r)
                .Scale(scale, scale, scale)
                // Offset applied here (after the addon rotation) so it follows the addon's local axes.
                .Translate(tf.Offset[0] - origin.X, tf.Offset[1] - origin.Y, tf.Offset[2] - origin.Z)
                .Values;

            if (hasOpaque)
            {
                prog.ModelMatrix = matrix;
                rpi.RenderMultiTextureMesh(opaqueRef, "tex");
            }

            if (hasTransp)
                transparentDraws.Add(((float[])matrix.Clone(), transpRef));
        }

        // Blended pass for transparent faces (glass). Approximate (single OIT-less pass) but fine for
        // a small attached object: blend on, no depth writes, both faces visible.
        if (transparentDraws.Count > 0)
        {
            rpi.GlToggleBlend(true);
            rpi.GLDepthMask(false);
            rpi.GlDisableCullFace();
            foreach (var (matrix, mesh) in transparentDraws)
            {
                prog.ModelMatrix = matrix;
                rpi.RenderMultiTextureMesh(mesh, "tex");
            }
            rpi.GlEnableCullFace();
            rpi.GLDepthMask(true);
            rpi.GlToggleBlend(false);
        }

        prog.Stop();
        rpi.GlEnableCullFace();
    }

    private void RebuildMeshes()
    {
        dirty = false;

        // A /tfedit tuning tweak bakes into every composed mesh, so on a generation change everything is stale.
        if (builtGeneration != AttachmentTransform.TuningGeneration)
        {
            DisposeMeshes();
            builtGeneration = AttachmentTransform.TuningGeneration;
        }

        if (be.BackpackItemCode == null)
        {
            pointKeys = Array.Empty<string>();
            return;
        }

        if (bodyMeshRef == null)
        {
            var bodyItem = capi.World.GetItem(be.BackpackItemCode);
            if (bodyItem != null)
            {
                capi.Tesselator.TesselateItem(bodyItem, out MeshData bodyMesh);
                if (bodyMesh != null)
                    bodyMeshRef = capi.Render.UploadMesh(bodyMesh);
            }
        }

        int n = be.AttachmentPoints.Length;
        pointKeys = new string[n];
        var desired = new HashSet<string>();

        for (int i = 0; i < n; i++)
        {
            var stack = be.AttachedItems[i];
            if (stack?.Collectible == null) continue;

            // Key on the node's ContentHash: it folds the addon's cargo children (a toolstrap's tools, which
            // live in the bag's cargo rather than the addon stack), so a tool swap changes only this key and the
            // reconcile below re-meshes just this addon instead of every one.
            var node = AttachmentFactory.For(stack, capi.World, be.OwnedCargo(i));
            string key = $"{i}:{node.ContentHash}";
            pointKeys[i] = key;
            desired.Add(key);

            if (!builtKeys.Add(key)) continue;                           // already built (or known mesh-less)

            // Addons can be items (pouches, toolstrap) or blocks (lantern); compose through the shared core so
            // a container addon (a toolstrap) folds its own children (tools) into the mesh, while a leaf addon
            // is just tesselated (honouring per-stack appearance like the lantern's metal).
            MeshData mesh = AttachmentComposer.MeshFor(capi, node);
            if (mesh == null) continue;

            // Composed meshes are already atlas-tagged; tag defensively so an IAttachmentMeshSource override
            // that skips tagging still uploads with per-face atlas ids (single-atlas fallback).
            mesh = AttachmentMesh.TagAtlas(mesh, stack.Item != null
                ? capi.ItemTextureAtlas.AtlasTextures[0].TextureId
                : capi.BlockTextureAtlas.AtlasTextures[0].TextureId);

            var (opaque, transparent) = SplitByTransparency(mesh);
            if (opaque != null && opaque.VerticesCount > 0)
                attachmentMeshRefs[key] = capi.Render.UploadMultiTextureMesh(opaque);
            if (transparent != null && transparent.VerticesCount > 0)
                attachmentTransparentRefs[key] = capi.Render.UploadMultiTextureMesh(transparent);
        }

        PruneStaleMeshes(desired);
    }

    // Dispose and forget cached addon meshes whose key is no longer wanted (an addon detached, or its content
    // changed so a new key replaced it), keeping the rest so one addon's change doesn't re-mesh the others.
    private void PruneStaleMeshes(HashSet<string> desired)
    {
        List<string> stale = null;
        foreach (var k in builtKeys)
            if (!desired.Contains(k))
                (stale ??= new List<string>()).Add(k);
        if (stale == null) return;

        foreach (var k in stale)
        {
            if (attachmentMeshRefs.TryGetValue(k, out var o)) { o?.Dispose(); attachmentMeshRefs.Remove(k); }
            if (attachmentTransparentRefs.TryGetValue(k, out var t)) { t?.Dispose(); attachmentTransparentRefs.Remove(k); }
            builtKeys.Remove(k);
        }
    }

    // Splits a mesh into its opaque and transparent (blended) faces by render pass. Returns (full, null)
    // when there are no transparent faces, so item addons skip the split entirely.
    private static (MeshData opaque, MeshData transparent) SplitByTransparency(MeshData full)
    {
        bool hasTransparent = full.NeedsRenderPass(EnumChunkRenderPass.Transparent)
                           || full.NeedsRenderPass(EnumChunkRenderPass.BlendNoCull)
                           || full.NeedsRenderPass(EnumChunkRenderPass.Liquid);
        if (!hasTransparent) return (full, null);

        var opaque = full.EmptyClone();
        opaque.AddMeshData(full, faceIndex => !IsTransparent(full.RenderPassesAndExtraBits[faceIndex]));
        var transparent = full.EmptyClone();
        transparent.AddMeshData(full, faceIndex => IsTransparent(full.RenderPassesAndExtraBits[faceIndex]));
        return (opaque, transparent);
    }

    private static bool IsTransparent(short rawPass) => (EnumChunkRenderPass)rawPass switch
    {
        EnumChunkRenderPass.Transparent => true,
        EnumChunkRenderPass.BlendNoCull => true,
        EnumChunkRenderPass.Liquid => true,
        _ => false
    };

    private void DisposeMeshes()
    {
        bodyMeshRef?.Dispose();
        bodyMeshRef = null;

        foreach (var r in attachmentMeshRefs.Values) r?.Dispose();
        foreach (var r in attachmentTransparentRefs.Values) r?.Dispose();
        attachmentMeshRefs.Clear();
        attachmentTransparentRefs.Clear();
        builtKeys.Clear();
    }

    public void Dispose()
    {
        capi.Event.UnregisterRenderer(this, EnumRenderStage.Opaque);
        DisposeMeshes();
    }
}
