#nullable enable
using System;
using Vintagestory.API.Client;

namespace ImmersiveModularBackpacks.Attachments;

internal static class AttachmentMeshNormalizer
{
    // VS's filtered AddMeshData assumes four vertices and six indices per face.
    internal static MeshData CloneForComposition(MeshData source)
    {
        if (source.mode != EnumDrawMode.Triangles)
            throw new NotSupportedException("Attachment meshes must use triangle primitives.");

        if (IsStandardQuadMesh(source))
        {
            var clone = source.Clone();
            clone.NormalsCount = source.NormalsCount;
            return clone;
        }

        if (source.IndicesCount % 3 != 0 || source.IndicesPerFace <= 0
            || source.IndicesPerFace % 3 != 0 || source.IndicesCount % source.IndicesPerFace != 0)
            throw new ArgumentException("Attachment mesh has an invalid triangle face layout.", nameof(source));

        int triangles = source.IndicesCount / 3;
        var vertices = new int[triangles * 4];
        var faces = new int[triangles];
        for (int triangle = 0; triangle < triangles; triangle++)
        {
            int input = triangle * 3;
            int output = triangle * 4;
            for (int corner = 0; corner < 3; corner++)
            {
                int vertex = source.Indices[input + corner];
                if ((uint)vertex >= (uint)source.VerticesCount)
                    throw new ArgumentException("Attachment mesh index references a missing vertex.", nameof(source));
                vertices[output + corner] = vertex;
            }
            vertices[output + 3] = vertices[output + 2];
            faces[triangle] = input / source.IndicesPerFace;
        }

        var result = source.Clone();
        result.VerticesPerFace = 4;
        result.IndicesPerFace = 6;
        result.VerticesCount = result.VerticesMax = vertices.Length;
        result.IndicesCount = result.IndicesMax = triangles * 6;
        result.xyz = Remap(source.xyz, vertices, 3)!;
        result.Uv = Remap(source.Uv, vertices, 2);
        result.Rgba = Remap(source.Rgba, vertices, 4);
        result.Flags = Remap(source.Flags, vertices, 1);
        result.Normals = Remap(source.Normals, vertices, 1);
        result.NormalsCount = result.Normals == null ? 0 : vertices.Length;
        result.Indices = new int[result.IndicesCount];
        for (int face = 0; face < triangles; face++)
        {
            int index = face * 6, vertex = face * 4;
            result.Indices[index] = vertex;
            result.Indices[index + 1] = vertex + 1;
            result.Indices[index + 2] = vertex + 2;
            result.Indices[index + 3] = vertex;
            result.Indices[index + 4] = vertex + 2;
            result.Indices[index + 5] = vertex + 3;
        }

        result.TextureIndices = Remap(source.TextureIndices, faces, 1);
        result.TextureIndicesCount = result.TextureIndices == null ? 0 : triangles;
        result.XyzFaces = source.XyzFacesCount == 0 ? [] : Remap(source.XyzFaces, faces, 1);
        result.XyzFacesCount = source.XyzFacesCount == 0 ? 0 : triangles;
        result.RenderPassesAndExtraBits = source.RenderPassCount == 0 ? [] : Remap(source.RenderPassesAndExtraBits, faces, 1);
        result.RenderPassCount = source.RenderPassCount == 0 ? 0 : triangles;
        result.ClimateColorMapIds = source.ColorMapIdsCount == 0 ? [] : Remap(source.ClimateColorMapIds, faces, 1);
        result.SeasonColorMapIds = source.ColorMapIdsCount == 0 ? [] : Remap(source.SeasonColorMapIds, faces, 1);
        result.FrostableBits = Remap(source.FrostableBits, faces, 1);
        result.ColorMapIdsCount = source.ColorMapIdsCount == 0 ? 0 : triangles;
        RemapCustom(result.CustomFloats, vertices, sizeof(float));
        RemapCustom(result.CustomInts, vertices, sizeof(int));
        RemapCustom(result.CustomShorts, vertices, sizeof(short));
        RemapCustom(result.CustomBytes, vertices, sizeof(byte));
        return result;
    }

    private static bool IsStandardQuadMesh(MeshData mesh)
    {
        if (mesh.VerticesPerFace != 4 || mesh.IndicesPerFace != 6
            || mesh.VerticesCount % 4 != 0 || mesh.IndicesCount != mesh.VerticesCount / 4 * 6)
            return false;

        for (int face = 0; face < mesh.VerticesCount / 4; face++)
        {
            int index = face * 6, vertex = face * 4;
            if (mesh.Indices[index] != vertex || mesh.Indices[index + 1] != vertex + 1
                || mesh.Indices[index + 2] != vertex + 2 || mesh.Indices[index + 3] != vertex
                || mesh.Indices[index + 4] != vertex + 2 || mesh.Indices[index + 5] != vertex + 3)
                return false;
        }
        return true;
    }

    private static T[]? Remap<T>(T[]? source, int[] map, int width)
    {
        if (source == null) return null;
        var result = new T[map.Length * width];
        for (int i = 0; i < map.Length; i++)
            Array.Copy(source, map[i] * width, result, i * width, width);
        return result;
    }

    private static void RemapCustom<T>(CustomMeshDataPart<T>? part, int[] vertices, int elementSize)
    {
        if (part == null || part.Instanced) return;
        int width = part.InterleaveStride == 0 ? part.InterleaveSizes[0] : part.InterleaveStride / elementSize;
        part.Values = Remap(part.Values, vertices, width)!;
        part.Count = part.Values.Length;
    }
}
