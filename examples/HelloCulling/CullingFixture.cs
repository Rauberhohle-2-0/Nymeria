using System.Numerics;
using Nymeria.Core.Models;

namespace HelloCulling;

/// <summary>
/// Builds the deterministic, original cube mesh used by every instance.
/// </summary>
/// <remarks>
/// The four vertices per face carry outward face normals, which the shared
/// material renderer requires. There are no texture coordinates, so the renderer
/// binds its white fallback texture and shows each shared material's tint.
/// </remarks>
internal static class CullingFixture
{
    private const float HalfExtent = 1.0f;

    private static readonly Vector3[] s_positions =
    {
        // Front (+Z)
        new(-HalfExtent, -HalfExtent,  HalfExtent),
        new( HalfExtent, -HalfExtent,  HalfExtent),
        new( HalfExtent,  HalfExtent,  HalfExtent),
        new(-HalfExtent,  HalfExtent,  HalfExtent),

        // Back (-Z)
        new( HalfExtent, -HalfExtent, -HalfExtent),
        new(-HalfExtent, -HalfExtent, -HalfExtent),
        new(-HalfExtent,  HalfExtent, -HalfExtent),
        new( HalfExtent,  HalfExtent, -HalfExtent),

        // Right (+X)
        new( HalfExtent, -HalfExtent,  HalfExtent),
        new( HalfExtent, -HalfExtent, -HalfExtent),
        new( HalfExtent,  HalfExtent, -HalfExtent),
        new( HalfExtent,  HalfExtent,  HalfExtent),

        // Left (-X)
        new(-HalfExtent, -HalfExtent, -HalfExtent),
        new(-HalfExtent, -HalfExtent,  HalfExtent),
        new(-HalfExtent,  HalfExtent,  HalfExtent),
        new(-HalfExtent,  HalfExtent, -HalfExtent),

        // Top (+Y)
        new(-HalfExtent,  HalfExtent,  HalfExtent),
        new( HalfExtent,  HalfExtent,  HalfExtent),
        new( HalfExtent,  HalfExtent, -HalfExtent),
        new(-HalfExtent,  HalfExtent, -HalfExtent),

        // Bottom (-Y)
        new(-HalfExtent, -HalfExtent, -HalfExtent),
        new( HalfExtent, -HalfExtent, -HalfExtent),
        new( HalfExtent, -HalfExtent,  HalfExtent),
        new(-HalfExtent, -HalfExtent,  HalfExtent),
    };

    private static readonly Vector3[] s_normals =
    {
        Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ,
        -Vector3.UnitZ, -Vector3.UnitZ, -Vector3.UnitZ, -Vector3.UnitZ,
        Vector3.UnitX, Vector3.UnitX, Vector3.UnitX, Vector3.UnitX,
        -Vector3.UnitX, -Vector3.UnitX, -Vector3.UnitX, -Vector3.UnitX,
        Vector3.UnitY, Vector3.UnitY, Vector3.UnitY, Vector3.UnitY,
        -Vector3.UnitY, -Vector3.UnitY, -Vector3.UnitY, -Vector3.UnitY,
    };

    private static readonly uint[] s_indices =
    {
         0,  1,  2,   0,  2,  3,
         4,  5,  6,   4,  6,  7,
         8,  9, 10,   8, 10, 11,
        12, 13, 14,  12, 14, 15,
        16, 17, 18,  16, 18, 19,
        20, 21, 22,  20, 22, 23,
    };

    public static MeshData CreateCubeMesh()
    {
        return new MeshData(
            s_positions,
            s_normals,
            ReadOnlyMemory<Vector2>.Empty,
            s_indices);
    }
}
