using System;
using System.Collections.Generic;
using System.Linq;
using ImmersiveModularBackpacks.Attachments;
using Vintagestory.API.Client;

internal static class AttachmentMeshTests
{
    public static void Run()
    {
        var quad = Quad();
        var triangles = Triangles();
        var normalized = AttachmentMeshNormalizer.CloneForComposition(triangles);
        var composed = AttachmentMeshNormalizer.CloneForComposition(quad);
        composed.AddMeshData(normalized);
        AssertTrianglesEqual(VisibleTriangles(quad).Concat(VisibleTriangles(triangles)), composed);
        var split = composed.SplitByTextureId();
        Assert(split.Length == 3, "All atlas pages must survive composition.");
        Assert(VisibleTriangles(composed).Order().SequenceEqual(split.SelectMany(VisibleTriangles).Order()),
            "Texture splitting must preserve geometry, winding, UVs, colours and flags.");

        var unchanged = AttachmentMeshNormalizer.CloneForComposition(quad);
        Assert(unchanged.VerticesCount == quad.VerticesCount && unchanged.Indices.SequenceEqual(quad.Indices)
            && unchanged.xyz.SequenceEqual(quad.xyz), "Existing standard quads must not be retessellated.");
        Assert(!ReferenceEquals(unchanged.xyz, quad.xyz), "Composition must own its vertex buffers.");
        Assert(triangles.VerticesCount == 36 && triangles.IndicesCount == 36 && triangles.VerticesPerFace == 3,
            "Normalization must not mutate the source mesh.");

        var shared = Triangles();
        shared.Indices[3] = 2;
        shared.Indices[4] = 1;
        shared.Indices[5] = 0;
        AssertTrianglesEqual(VisibleTriangles(shared), AttachmentMeshNormalizer.CloneForComposition(shared));

        var alternateQuad = Quad();
        alternateQuad.Indices = [0, 1, 3, 1, 2, 3];
        AssertTrianglesEqual(VisibleTriangles(alternateQuad), AttachmentMeshNormalizer.CloneForComposition(alternateQuad));

        var attributes = Triangles();
        attributes.Normals = Enumerable.Range(100, attributes.VerticesCount).ToArray();
        attributes.NormalsCount = attributes.VerticesCount;
        attributes.XyzFaces = Enumerable.Repeat((byte)3, 12).ToArray();
        attributes.XyzFacesCount = 12;
        attributes.RenderPassesAndExtraBits = Enumerable.Range(0, 12).Select(i => (short)(i % 2)).ToArray();
        attributes.RenderPassCount = 12;
        attributes.ClimateColorMapIds = Enumerable.Repeat((byte)7, 12).ToArray();
        attributes.SeasonColorMapIds = Enumerable.Repeat((byte)8, 12).ToArray();
        attributes.FrostableBits = Enumerable.Repeat(true, 12).ToArray();
        attributes.ColorMapIdsCount = 12;
        attributes.CustomFloats = new CustomMeshDataPartFloat
        {
            Values = Enumerable.Range(0, 72).Select(i => (float)i).ToArray(), Count = 72,
            InterleaveSizes = [1, 1], InterleaveOffsets = [0, 4], InterleaveStride = 8
        };
        var remapped = AttachmentMeshNormalizer.CloneForComposition(attributes);
        for (int face = 0; face < 12; face++)
        {
            Assert(remapped.RenderPassesAndExtraBits[face] == face % 2 && remapped.XyzFaces[face] == 3
                && remapped.ClimateColorMapIds[face] == 7 && remapped.SeasonColorMapIds[face] == 8
                && remapped.FrostableBits[face], "Per-face metadata must survive normalization.");
            for (int corner = 0; corner < 4; corner++)
            {
                int source = face * 3 + Math.Min(corner, 2), destination = face * 4 + corner;
                Assert(remapped.Normals[destination] == attributes.Normals[source]
                    && remapped.CustomFloats.Values[destination * 2] == attributes.CustomFloats.Values[source * 2]
                    && remapped.CustomFloats.Values[destination * 2 + 1] == attributes.CustomFloats.Values[source * 2 + 1],
                    "Normals and interleaved custom attributes must follow duplicated vertices.");
            }
        }
        Assert(attributes.CustomFloats.Count == 72 && remapped.CustomFloats.Count == 96,
            "Custom source data must remain untouched.");
        var filtered = normalized.EmptyClone();
        filtered.AddMeshData(normalized, face => face % 2 == 1);
        AssertTrianglesEqual(VisibleTriangles(triangles).Where((_, i) => i % 2 == 1), filtered);

        var untagged = Triangles();
        untagged.TextureIds = [];
        untagged.TextureIndices = null;
        untagged.TextureIndicesCount = 0;
        AttachmentMesh.TagAtlas(untagged, 123);
        Assert(untagged.TextureIndicesCount == 12 && untagged.TextureIndices.Length == 12,
            "Atlas tagging must count triangle faces, not groups of four vertices.");
        Console.WriteLine("Attachment mesh normalization checks passed.");
    }

    private static MeshData Quad()
    {
        var mesh = new MeshData(4, 6)
        {
            VerticesCount = 4, IndicesCount = 6,
            xyz = [0, 0, 0, 1, 0, 0, 1, 1, 0, 0, 1, 0],
            Indices = [0, 1, 2, 0, 2, 3],
            TextureIds = [10], TextureIndices = [0], TextureIndicesCount = 1
        };
        return mesh;
    }

    private static MeshData Triangles()
    {
        var mesh = new MeshData(36, 36)
        {
            VerticesCount = 36, IndicesCount = 36, VerticesPerFace = 3, IndicesPerFace = 3,
            TextureIds = [20, 30], TextureIndices = new byte[12], TextureIndicesCount = 12
        };
        for (int face = 0; face < 12; face++)
        {
            mesh.TextureIndices[face] = (byte)(face % 2);
            for (int corner = 0; corner < 3; corner++)
            {
                int vertex = face * 3 + corner;
                mesh.Indices[vertex] = vertex;
                mesh.xyz[vertex * 3] = face + 2;
                mesh.xyz[vertex * 3 + 1] = corner == 1 ? 1 : 0;
                mesh.xyz[vertex * 3 + 2] = corner == 2 ? 1 : 0;
                mesh.Uv[vertex * 2] = vertex;
                mesh.Uv[vertex * 2 + 1] = vertex + 0.5f;
                mesh.Flags[vertex] = vertex;
                for (int channel = 0; channel < 4; channel++) mesh.Rgba[vertex * 4 + channel] = (byte)(vertex + channel);
            }
        }
        return mesh;
    }

    private static IEnumerable<string> VisibleTriangles(MeshData mesh)
    {
        for (int i = 0; i < mesh.IndicesCount; i += 3)
        {
            int a = mesh.Indices[i], b = mesh.Indices[i + 1], c = mesh.Indices[i + 2];
            Assert(a >= 0 && b >= 0 && c >= 0 && a < mesh.VerticesCount && b < mesh.VerticesCount && c < mesh.VerticesCount,
                "Splitting must not produce out-of-range vertex indices.");
            var pa = mesh.xyz.Skip(a * 3).Take(3).ToArray();
            var pb = mesh.xyz.Skip(b * 3).Take(3).ToArray();
            var pc = mesh.xyz.Skip(c * 3).Take(3).ToArray();
            if (pa.SequenceEqual(pb) || pa.SequenceEqual(pc) || pb.SequenceEqual(pc)) continue;
            var corners = new[] { a, b, c }.Select(v => string.Join(",", mesh.xyz.Skip(v * 3).Take(3))
                + "/" + string.Join(",", mesh.Uv.Skip(v * 2).Take(2))
                + "/" + string.Join(",", mesh.Rgba.Skip(v * 4).Take(4)) + "/" + mesh.Flags[v]);
            yield return mesh.TextureIds[mesh.TextureIndices[i / mesh.IndicesPerFace]] + ":" + string.Join(";", corners);
        }
    }

    private static void AssertTrianglesEqual(IEnumerable<string> expected, MeshData actual)
        => Assert(expected.Order().SequenceEqual(VisibleTriangles(actual).Order()), "Visible textured triangles must remain identical.");

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
