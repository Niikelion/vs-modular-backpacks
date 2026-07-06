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
    private bool dirty = true;

    private readonly Matrixf modelMat = new();
    // Reused buffer for the transparent draws collected during the opaque pass.
    private readonly List<(float[] matrix, MultiTextureMeshRef mesh)> transparentDraws = new();

    public double RenderOrder => 0.5;
    public int RenderRange => 24;

    public void MarkDirty() => dirty = true;

    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
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
            var key = AttachmentMeshKey(i, stack);
            bool hasOpaque = attachmentMeshRefs.TryGetValue(key, out var opaqueRef);
            bool hasTransp = attachmentTransparentRefs.TryGetValue(key, out var transpRef);
            if (!hasOpaque && !hasTransp) continue;                       // addon had no mesh; skip

            var point = be.AttachmentPoints[i];
            var anchor = point.Origin;                                   // marker pivot, [0,1]
            var origin = AttachmentMesh.ModelOrigin(stack.Collectible);  // addon's fixed model origin

            // Apply the point's placed transform combined with the item override (no hitbox auto-fit).
            var tf = point.Placed.CombinedWith(AttachmentTransform.ForItem(stack.Collectible, "placed"));
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

        DisposeMeshes();

        if (be.BackpackItemCode == null) return;

        var bodyItem = capi.World.GetItem(be.BackpackItemCode);
        if (bodyItem != null)
        {
            capi.Tesselator.TesselateItem(bodyItem, out MeshData bodyMesh);
            if (bodyMesh != null)
                bodyMeshRef = capi.Render.UploadMesh(bodyMesh);
        }

        for (int i = 0; i < be.AttachmentPoints.Length; i++)
        {
            var stack = be.AttachedItems[i];
            if (stack?.Collectible == null) continue;

            var key = AttachmentMeshKey(i, stack);
            if (!builtKeys.Add(key)) continue;                           // dedupe identical addons across points

            // Addons can be items (pouches, toolstrap) or blocks (lantern); compose through the shared core so
            // a container addon (a toolstrap) folds its own children (tools) into the mesh, while a leaf addon
            // is just tesselated (honouring per-stack appearance like the lantern's metal).
            MeshData mesh = AttachmentComposer.MeshFor(capi,
                AttachmentFactory.ForBagChild(stack, be.OwnedCargo(i), capi.World));
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

    // Keyed by the stack's full content hash (not just its collectible code) so two toolstraps holding
    // different tools - or any addon whose composed mesh depends on nested content - get distinct meshes.
    // ItemStack.GetHashCode() folds the attribute tree, so nested child stacks are already reflected.
    private static string AttachmentMeshKey(int pointIndex, ItemStack stack)
        => $"{pointIndex}:{stack?.GetHashCode()}";

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
