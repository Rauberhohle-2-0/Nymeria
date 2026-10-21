using System.Numerics;
using Nymeria.Core.Materials;
using Nymeria.Core.Models;

namespace HelloMaterials;

/// <summary>
/// Builds the cube mesh and generated checkerboard texture used by the example.
/// </summary>
/// <remarks>
/// Both are original project content generated in code. The example does not
/// require or load an OMSI, OBJ, O3D, or image asset to run. The checkerboard
/// uses four differently colored quadrants so a flipped U or V axis is visible
/// on a face, with an alternating brightness pattern inside each quadrant.
/// </remarks>
internal static class MaterialFixtures
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

    // One outward normal per face, repeated for the face's four vertices. The
    // shared material renderer requires normals even when the scene is unlit.
    private static readonly Vector3[] s_normals =
    {
        new(0.0f, 0.0f, 1.0f), new(0.0f, 0.0f, 1.0f),
        new(0.0f, 0.0f, 1.0f), new(0.0f, 0.0f, 1.0f),
        new(0.0f, 0.0f, -1.0f), new(0.0f, 0.0f, -1.0f),
        new(0.0f, 0.0f, -1.0f), new(0.0f, 0.0f, -1.0f),
        new(1.0f, 0.0f, 0.0f), new(1.0f, 0.0f, 0.0f),
        new(1.0f, 0.0f, 0.0f), new(1.0f, 0.0f, 0.0f),
        new(-1.0f, 0.0f, 0.0f), new(-1.0f, 0.0f, 0.0f),
        new(-1.0f, 0.0f, 0.0f), new(-1.0f, 0.0f, 0.0f),
        new(0.0f, 1.0f, 0.0f), new(0.0f, 1.0f, 0.0f),
        new(0.0f, 1.0f, 0.0f), new(0.0f, 1.0f, 0.0f),
        new(0.0f, -1.0f, 0.0f), new(0.0f, -1.0f, 0.0f),
        new(0.0f, -1.0f, 0.0f), new(0.0f, -1.0f, 0.0f),
    };

    // Each face repeats the same UV layout: the first vertex is the bottom-left
    // corner as viewed from outside the cube, with (0, 1) at the bottom-left,
    // (1, 1) at the bottom-right, (1, 0) at the top-right, and (0, 0) at the
    // top-left. Model data uses a top-left UV origin with +V down.
    private static readonly Vector2[] s_textureCoordinates =
    {
        new(0.0f, 1.0f), new(1.0f, 1.0f), new(1.0f, 0.0f), new(0.0f, 0.0f),
        new(0.0f, 1.0f), new(1.0f, 1.0f), new(1.0f, 0.0f), new(0.0f, 0.0f),
        new(0.0f, 1.0f), new(1.0f, 1.0f), new(1.0f, 0.0f), new(0.0f, 0.0f),
        new(0.0f, 1.0f), new(1.0f, 1.0f), new(1.0f, 0.0f), new(0.0f, 0.0f),
        new(0.0f, 1.0f), new(1.0f, 1.0f), new(1.0f, 0.0f), new(0.0f, 0.0f),
        new(0.0f, 1.0f), new(1.0f, 1.0f), new(1.0f, 0.0f), new(0.0f, 0.0f),
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
            s_textureCoordinates,
            s_indices);
    }

    public static TextureData CreateCheckerboardTexture()
    {
        const int size = 128;
        const int cells = 8;
        const int cellSize = size / cells;
        var pixels = new byte[size * size * 4];

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                Vector3 quadrant = (x, y) switch
                {
                    (< size / 2, < size / 2) => new Vector3(0.90f, 0.15f, 0.15f),
                    (>= size / 2, < size / 2) => new Vector3(0.15f, 0.80f, 0.25f),
                    (< size / 2, >= size / 2) => new Vector3(0.20f, 0.45f, 0.95f),
                    _ => new Vector3(0.95f, 0.80f, 0.15f),
                };
                bool lightCell = ((x / cellSize) + (y / cellSize)) % 2 == 0;
                float brightness = lightCell ? 1.0f : 0.45f;
                int offset = (y * size + x) * 4;

                pixels[offset] = ToByte(quadrant.X * brightness);
                pixels[offset + 1] = ToByte(quadrant.Y * brightness);
                pixels[offset + 2] = ToByte(quadrant.Z * brightness);
                pixels[offset + 3] = 255;
            }
        }

        return new TextureData(size, size, pixels, "generated checkerboard");
    }

    private static byte ToByte(float value)
    {
        return (byte)Math.Clamp((int)MathF.Round(value * 255.0f), 0, 255);
    }
}
