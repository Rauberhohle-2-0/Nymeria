using System.Numerics;
using Nymeria.Core.Models;

namespace Nymeria.Examples;

/// <summary>
/// Converts validated mesh data to the interleaved position/normal/UV vertex
/// layout used by the shared material shader.
/// </summary>
/// <remarks>
/// <para>
/// The lit material path requires one finite, non-zero model-space normal per
/// vertex. A mesh without normals, or with a zero-length normal, is rejected with
/// a clear <see cref="InvalidOperationException"/> instead of inventing geometry
/// or uploading a vector that would produce NaNs. Normals are normalized before
/// upload; the shader normalizes again after transforming them with the
/// inverse-transpose normal matrix.
/// </para>
/// <para>
/// Meshes without texture coordinates receive (0, 0) for every vertex. The
/// renderer then binds a white fallback texture, so the material tint is shown
/// instead of sampling an arbitrary texel.
/// </para>
/// </remarks>
internal static class MaterialGpuGeometryBuilder
{
    private const int FloatsPerVertex = 8;

    // The example is a viewer, not a streaming engine. Bound the converted
    // geometry before allocating or requesting a GPU buffer.
    private const int MaxGpuVertices = 2_000_000;
    private const int MaxGpuIndices = 6_000_000;

    public static MaterialGpuGeometry Build(MeshData mesh)
    {
        ArgumentNullException.ThrowIfNull(mesh);

        ReadOnlySpan<Vector3> positions = mesh.Positions.Span;
        ReadOnlySpan<Vector3> normals = mesh.Normals.Span;
        ReadOnlySpan<uint> indices = mesh.Indices.Span;

        if (normals.IsEmpty)
        {
            throw new InvalidOperationException(
                "The material renderer requires vertex normals for lighting, but " +
                "the mesh has none. Supply normals when constructing MeshData.");
        }

        if (positions.Length > MaxGpuVertices || indices.Length > MaxGpuIndices)
        {
            throw new InvalidOperationException(
                $"The mesh exceeds the example's GPU budget of {MaxGpuVertices} " +
                $"vertices and {MaxGpuIndices} indices. The mesh has " +
                $"{positions.Length} vertices and {indices.Length} indices.");
        }

        ReadOnlySpan<Vector2> textureCoordinates = mesh.TextureCoordinates.Span;
        bool hasTextureCoordinates = !textureCoordinates.IsEmpty;
        var vertices = new float[checked(positions.Length * FloatsPerVertex)];

        for (int i = 0; i < positions.Length; i++)
        {
            Vector3 normal = normals[i];
            float normalLengthSquared = normal.LengthSquared();

            if (!float.IsFinite(normalLengthSquared) || normalLengthSquared <= 0.0f)
            {
                throw new InvalidOperationException(
                    $"Vertex normal {i} is zero or non-finite. A lit mesh requires " +
                    "a finite, non-zero normal per vertex.");
            }

            normal /= MathF.Sqrt(normalLengthSquared);

            int offset = i * FloatsPerVertex;
            Vector3 position = positions[i];
            Vector2 textureCoordinate = hasTextureCoordinates
                ? textureCoordinates[i]
                : Vector2.Zero;

            vertices[offset] = position.X;
            vertices[offset + 1] = position.Y;
            vertices[offset + 2] = position.Z;
            vertices[offset + 3] = normal.X;
            vertices[offset + 4] = normal.Y;
            vertices[offset + 5] = normal.Z;
            vertices[offset + 6] = textureCoordinate.X;
            vertices[offset + 7] = textureCoordinate.Y;
        }

        return new MaterialGpuGeometry(
            vertices,
            indices.ToArray(),
            hasTextureCoordinates);
    }
}

internal readonly record struct MaterialGpuGeometry(
    float[] Vertices,
    uint[] Indices,
    bool HasTextureCoordinates);
