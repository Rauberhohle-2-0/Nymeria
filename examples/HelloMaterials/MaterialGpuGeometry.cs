using System.Numerics;
using Nymeria.Core.Models;

namespace HelloMaterials;

/// <summary>
/// Converts validated mesh data to the interleaved position/UV vertex layout used
/// by this example's material shader.
/// </summary>
/// <remarks>
/// Meshes without texture coordinates receive (0, 0) for every vertex. The
/// renderer then binds a white fallback texture, so the material tint is shown
/// instead of sampling an arbitrary texel.
/// </remarks>
internal static class MaterialGpuGeometryBuilder
{
    private const int FloatsPerVertex = 5;

    // The example is a viewer, not a streaming engine. Bound the converted
    // geometry before allocating or requesting a GPU buffer.
    private const int MaxGpuVertices = 2_000_000;
    private const int MaxGpuIndices = 6_000_000;

    public static MaterialGpuGeometry Build(MeshData mesh)
    {
        ArgumentNullException.ThrowIfNull(mesh);

        ReadOnlySpan<Vector3> positions = mesh.Positions.Span;
        ReadOnlySpan<uint> indices = mesh.Indices.Span;

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
            int offset = i * FloatsPerVertex;
            Vector3 position = positions[i];
            Vector2 textureCoordinate = hasTextureCoordinates
                ? textureCoordinates[i]
                : Vector2.Zero;

            vertices[offset] = position.X;
            vertices[offset + 1] = position.Y;
            vertices[offset + 2] = position.Z;
            vertices[offset + 3] = textureCoordinate.X;
            vertices[offset + 4] = textureCoordinate.Y;
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
