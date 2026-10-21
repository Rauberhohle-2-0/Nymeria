using System.Numerics;
using Nymeria.Core.Models;

namespace HelloLOD;

/// <summary>
/// Builds original procedural sphere meshes at three visibly different detail
/// levels. Nymeria does not generate or simplify these variants; the example
/// supplies them explicitly.
/// </summary>
internal static class LodFixture
{
    public const int HighSegments = 24;
    public const int HighRings = 12;
    public const int MediumSegments = 10;
    public const int MediumRings = 5;
    public const int LowSegments = 5;
    public const int LowRings = 3;

    public static MeshData CreateHighDetail()
    {
        return CreateSphere(HighSegments, HighRings);
    }

    public static MeshData CreateMediumDetail()
    {
        return CreateSphere(MediumSegments, MediumRings);
    }

    public static MeshData CreateLowDetail()
    {
        return CreateSphere(LowSegments, LowRings);
    }

    /// <summary>
    /// Creates a latitude/longitude sphere of radius one with outward normals.
    /// </summary>
    /// <remarks>
    /// The pole rows collapse to a point, so the two triangles touching each
    /// pole include a degenerate triangle. That keeps the index construction
    /// simple, and the degenerate triangle generates no fragments. No texture
    /// coordinates are generated because the example uses untextured materials.
    /// </remarks>
    private static MeshData CreateSphere(int segments, int rings)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(segments, 3);
        ArgumentOutOfRangeException.ThrowIfLessThan(rings, 2);

        int vertexCount = (rings + 1) * segments;
        var positions = new Vector3[vertexCount];
        var normals = new Vector3[vertexCount];
        int index = 0;

        for (int ring = 0; ring <= rings; ring++)
        {
            float latitude = (-MathF.PI / 2.0f) + (MathF.PI * ring / rings);
            float y = MathF.Sin(latitude);
            float radius = MathF.Cos(latitude);

            for (int segment = 0; segment < segments; segment++)
            {
                float longitude = MathF.Tau * segment / segments;
                var position = new Vector3(
                    MathF.Cos(longitude) * radius,
                    y,
                    MathF.Sin(longitude) * radius);
                positions[index] = position;
                normals[index] = position;
                index++;
            }
        }

        var indices = new uint[rings * segments * 6];
        int write = 0;

        for (int ring = 0; ring < rings; ring++)
        {
            for (int segment = 0; segment < segments; segment++)
            {
                uint topLeft = (uint)((ring * segments) + segment);
                uint topRight = (uint)((ring * segments) + ((segment + 1) % segments));
                uint bottomLeft = (uint)(((ring + 1) * segments) + segment);
                uint bottomRight = (uint)(((ring + 1) * segments) + ((segment + 1) % segments));

                // Counter-clockwise when viewed from outside.
                indices[write++] = topLeft;
                indices[write++] = bottomLeft;
                indices[write++] = topRight;

                indices[write++] = topRight;
                indices[write++] = bottomLeft;
                indices[write++] = bottomRight;
            }
        }

        return new MeshData(
            positions,
            normals,
            ReadOnlyMemory<Vector2>.Empty,
            indices);
    }
}
