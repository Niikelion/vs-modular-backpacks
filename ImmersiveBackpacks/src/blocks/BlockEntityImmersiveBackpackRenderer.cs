using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ImmersiveBackpacks.blocks;

public class BlockEntityImmersiveBackpackRenderer(BlockPos pos, ICoreClientAPI capi, BlockEntityImmersiveBackpack be)
    : IRenderer
{
    private MeshRef bodyMeshRef;
    private readonly Dictionary<string, MeshRef> attachmentMeshRefs = new();
    private readonly Dictionary<string, MeshRef> attachmentTransparentRefs = new();
    private readonly Dictionary<string, (Vec3f center, Vec3f size)> attachmentBounds = new();
    private readonly Dictionary<string, int> attachmentTexId = new();
    private bool dirty = true;

    private readonly Matrixf modelMat = new();
    // Reused buffer for the transparent draws collected during the opaque pass.
    private readonly List<(float[] matrix, MeshRef mesh, int texId)> transparentDraws = new();

    // Fraction of the hitbox an attached item is scaled to fill.
    private const float FillFactor = 0.85f;

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
        // that needs both sides drawn. The lantern stays correct via per-addon atlas binding, not culling.
        rpi.GlDisableCullFace();
        transparentDraws.Clear();
        const float d2r = (float)(Math.PI / 180.0);
        for (int i = 0; i < be.AttachmentPoints.Length; i++)
        {
            var stack = be.AttachedItems[i];
            if (stack == null) continue;
            var key = AttachmentMeshKey(i, stack);
            if (!attachmentBounds.TryGetValue(key, out var bounds)) continue;

            // Block addons (lantern) tesselate into the block atlas, items into the item atlas.
            int texId = attachmentTexId.TryGetValue(key, out var tid)
                ? tid
                : capi.ItemTextureAtlas.AtlasTextures[0].TextureId;

            var point = be.AttachmentPoints[i];
            var hb = point.Hitbox;
            float cx = (hb.X1 + hb.X2) / 2f;
            float cy = (hb.Y1 + hb.Y2) / 2f;
            float cz = (hb.Z1 + hb.Z2) / 2f;

            // Auto-fit to the hitbox, then apply the point's placed transform combined with the item override.
            var tf = point.Placed.CombinedWith(AttachmentTransform.FromItem(stack.Collectible, "placed"));
            float scale = FitScale(bounds.size, hb) * tf.Scale;

            // Rotate about the block centre (placement orientation), then position at the hitbox centre
            // (relative to that centre) plus the transform offset, then rotate/scale/centre the mesh.
            float[] matrix = modelMat.Identity()
                .Translate(pos.X - camPos.X + 0.5, pos.Y - camPos.Y, pos.Z - camPos.Z + 0.5)
                .RotateY(angle)
                .Translate(cx - 0.5 + tf.Offset[0], cy + tf.Offset[1], cz - 0.5 + tf.Offset[2])
                .RotateX(tf.Rotation[0] * d2r)
                .RotateY(tf.Rotation[1] * d2r)
                .RotateZ(tf.Rotation[2] * d2r)
                .Scale(scale, scale, scale)
                .Translate(-bounds.center.X, -bounds.center.Y, -bounds.center.Z)
                .Values;

            if (attachmentMeshRefs.TryGetValue(key, out var opaqueRef))
            {
                prog.Tex2D = texId;
                prog.ModelMatrix = matrix;
                rpi.RenderMesh(opaqueRef);
            }

            if (attachmentTransparentRefs.TryGetValue(key, out var transpRef))
                transparentDraws.Add(((float[])matrix.Clone(), transpRef, texId));
        }

        // Blended pass for transparent faces (glass). Approximate (single OIT-less pass) but fine for
        // a small attached object: blend on, no depth writes, both faces visible.
        if (transparentDraws.Count > 0)
        {
            rpi.GlToggleBlend(true);
            rpi.GLDepthMask(false);
            rpi.GlDisableCullFace();
            foreach (var (matrix, mesh, texId) in transparentDraws)
            {
                prog.Tex2D = texId;
                prog.ModelMatrix = matrix;
                rpi.RenderMesh(mesh);
            }
            rpi.GlEnableCullFace();
            rpi.GLDepthMask(true);
            rpi.GlToggleBlend(false);
        }

        prog.Stop();
        rpi.GlEnableCullFace();
    }

    private static float FitScale(Vec3f meshSize, Cuboidf hb)
    {
        // Size by the largest mesh extent vs. the largest hitbox extent, so addons of differing aspect
        // ratios (e.g. linensack vs. miningbagsturdy shapes) render at a uniform size per slot instead
        // of the most-constraining axis shrinking some of them. Matches the worn look.
        float meshMax = Math.Max(meshSize.X, Math.Max(meshSize.Y, meshSize.Z));
        if (meshMax < 1e-4f) return FillFactor;
        float hbMax = Math.Max(hb.X2 - hb.X1, Math.Max(hb.Y2 - hb.Y1, hb.Z2 - hb.Z1));
        return FillFactor * hbMax / meshMax;
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
            if (attachmentBounds.ContainsKey(key)) continue;

            // Addons can be items (pouches, toolstrap) or blocks (lantern); tesselate accordingly and
            // remember which texture atlas the resulting UVs index.
            MeshData mesh = null;
            int texId;
            if (stack.Item != null)
            {
                capi.Tesselator.TesselateItem(stack.Item, out mesh);
                texId = capi.ItemTextureAtlas.AtlasTextures[0].TextureId;
            }
            else
            {
                capi.Tesselator.TesselateBlock(stack.Block, out mesh);
                texId = capi.BlockTextureAtlas.AtlasTextures[0].TextureId;
            }
            if (mesh == null) continue;

            attachmentBounds[key] = MeshBounds(mesh);
            attachmentTexId[key] = texId;

            var (opaque, transparent) = SplitByTransparency(mesh);
            if (opaque != null && opaque.VerticesCount > 0)
                attachmentMeshRefs[key] = capi.Render.UploadMesh(opaque);
            if (transparent != null && transparent.VerticesCount > 0)
                attachmentTransparentRefs[key] = capi.Render.UploadMesh(transparent);
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

    private static (Vec3f center, Vec3f size) MeshBounds(MeshData mesh)
    {
        float minX = float.MaxValue, minY = float.MaxValue, minZ = float.MaxValue;
        float maxX = float.MinValue, maxY = float.MinValue, maxZ = float.MinValue;

        var xyz = mesh.xyz;
        int n = mesh.VerticesCount * 3;
        for (int i = 0; i + 2 < n; i += 3)
        {
            float x = xyz[i], y = xyz[i + 1], z = xyz[i + 2];
            if (x < minX) minX = x; if (y < minY) minY = y; if (z < minZ) minZ = z;
            if (x > maxX) maxX = x; if (y > maxY) maxY = y; if (z > maxZ) maxZ = z;
        }

        if (minX > maxX) return (new Vec3f(0.5f, 0.5f, 0.5f), new Vec3f(1f, 1f, 1f));
        return (
            new Vec3f((minX + maxX) / 2f, (minY + maxY) / 2f, (minZ + maxZ) / 2f),
            new Vec3f(maxX - minX, maxY - minY, maxZ - minZ));
    }

    private static string AttachmentMeshKey(int pointIndex, ItemStack stack)
        => $"{pointIndex}:{stack?.Collectible?.Code}";

    private void DisposeMeshes()
    {
        bodyMeshRef?.Dispose();
        bodyMeshRef = null;

        foreach (var r in attachmentMeshRefs.Values) r?.Dispose();
        foreach (var r in attachmentTransparentRefs.Values) r?.Dispose();
        attachmentMeshRefs.Clear();
        attachmentTransparentRefs.Clear();
        attachmentBounds.Clear();
        attachmentTexId.Clear();
    }

    public void Dispose()
    {
        capi.Event.UnregisterRenderer(this, EnumRenderStage.Opaque);
        DisposeMeshes();
    }
}
