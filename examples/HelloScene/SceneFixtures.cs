using System.Numerics;
using Nymeria.Core.Models;

namespace HelloScene;

/// <summary>
/// Builds the deterministic fallback mesh used by the example.
/// </summary>
/// <remarks>
/// The cube is original project content. It needs no OBJ or O3D fixture and
/// contains no OMSI game assets. Its six material labels are mapped to solid
/// colors by the shared example geometry builder.
/// </remarks>
internal static class SceneFixtures
{
    private const float HalfExtent = 1.0f;

    // Twenty-four vertices: four per face. Corners are duplicated so each face
    // is one subset with its own material label and solid color.
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

    // Two triangles per face, wound counter-clockwise when viewed from outside
    // the cube.
    private static readonly uint[] s_indices =
    {
         0,  1,  2,   0,  2,  3,
         4,  5,  6,   4,  6,  7,
         8,  9, 10,   8, 10, 11,
        12, 13, 14,  12, 14, 15,
        16, 17, 18,  16, 18, 19,
        20, 21, 22,  20, 22, 23,
    };

    private static readonly MeshSubset[] s_subsets =
    {
        new(0, 6, "front"),
        new(6, 6, "back"),
        new(12, 6, "right"),
        new(18, 6, "left"),
        new(24, 6, "top"),
        new(30, 6, "bottom"),
    };

    public static MeshData CreateCubeMesh()
    {
        return new MeshData(
            s_positions,
            ReadOnlyMemory<Vector3>.Empty,
            ReadOnlyMemory<Vector2>.Empty,
            s_indices,
            s_subsets);
    }
}
