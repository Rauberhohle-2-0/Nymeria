using System.Numerics;
using Nymeria.Core.Models;

namespace HelloCube;

/// <summary>
/// Defines the example cube as format-independent model data and converts it to
/// the position/color layout used by the existing cube shader.
/// </summary>
internal static class CubeModel
{
    private const float HalfExtent = 1.0f;
    private const int FloatsPerGpuVertex = 6;

    // The model carries material names, not material properties. The example
    // maps those names to the colors its shader already uses. No material system
    // is involved.
    private static readonly Dictionary<string, Vector3> s_faceColors =
        new(StringComparer.Ordinal)
        {
            ["front"] = new Vector3(0.90f, 0.15f, 0.15f),
            ["back"] = new Vector3(0.15f, 0.75f, 0.25f),
            ["right"] = new Vector3(0.20f, 0.45f, 0.95f),
            ["left"] = new Vector3(0.95f, 0.75f, 0.15f),
            ["top"] = new Vector3(0.85f, 0.30f, 0.85f),
            ["bottom"] = new Vector3(0.20f, 0.80f, 0.85f),
        };

    // Twenty-four vertices: four per face. Corners are duplicated because each
    // face has its own normal and material reference. The GPU conversion also
    // duplicates them per subset so each face receives one solid color.
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

    // Texture coordinates use the foundation's top-left origin and +V down
    // convention. The shader does not sample a texture yet.
    private static readonly Vector2[] s_textureCoordinates =
    {
        new(0.0f, 1.0f), new(1.0f, 1.0f), new(1.0f, 0.0f), new(0.0f, 0.0f),
        new(0.0f, 1.0f), new(1.0f, 1.0f), new(1.0f, 0.0f), new(0.0f, 0.0f),
        new(0.0f, 1.0f), new(1.0f, 1.0f), new(1.0f, 0.0f), new(0.0f, 0.0f),
        new(0.0f, 1.0f), new(1.0f, 1.0f), new(1.0f, 0.0f), new(0.0f, 0.0f),
        new(0.0f, 1.0f), new(1.0f, 1.0f), new(1.0f, 0.0f), new(0.0f, 0.0f),
        new(0.0f, 1.0f), new(1.0f, 1.0f), new(1.0f, 0.0f), new(0.0f, 0.0f),
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

    public static ModelData Create()
    {
        var mesh = new MeshData(
            s_positions,
            s_normals,
            s_textureCoordinates,
            s_indices,
            s_subsets);

        return new ModelData("Cube", new[] { mesh });
    }

    /// <summary>
    /// Converts the model to the interleaved position/color vertex layout and
    /// 16-bit index buffer used by the existing cube pipeline.
    /// </summary>
    public static CubeGpuGeometry CreateGpuGeometry(ModelData model)
    {
        MeshData mesh = model.Meshes[0];
        ReadOnlySpan<Vector3> positions = mesh.Positions.Span;
        ReadOnlySpan<uint> sourceIndices = mesh.Indices.Span;

        var vertices = new List<float>(positions.Length * FloatsPerGpuVertex);
        var indices = new List<ushort>(sourceIndices.Length);

        foreach (MeshSubset subset in mesh.Subsets)
        {
            if (subset.MaterialName is null ||
                !s_faceColors.TryGetValue(subset.MaterialName, out Vector3 color))
            {
                throw new InvalidOperationException(
                    $"The cube model subset has no matching face color for material " +
                    $"'{subset.MaterialName ?? "<none>"}'.");
            }

            // A source vertex shared by subsets with different materials cannot
            // carry one color. The cube has disjoint face subsets, so each
            // subset emits its own vertices.
            var vertexMap = new Dictionary<uint, ushort>();

            for (int i = subset.IndexOffset; i < subset.IndexOffset + subset.IndexCount; i++)
            {
                uint sourceIndex = sourceIndices[i];

                if (!vertexMap.TryGetValue(sourceIndex, out ushort gpuIndex))
                {
                    int nextIndex = vertices.Count / FloatsPerGpuVertex;

                    if (nextIndex > ushort.MaxValue)
                    {
                        throw new InvalidOperationException(
                            "The cube model needs more than 65,535 vertices.");
                    }

                    gpuIndex = (ushort)nextIndex;
                    vertexMap.Add(sourceIndex, gpuIndex);

                    Vector3 position = positions[(int)sourceIndex];
                    vertices.Add(position.X);
                    vertices.Add(position.Y);
                    vertices.Add(position.Z);
                    vertices.Add(color.X);
                    vertices.Add(color.Y);
                    vertices.Add(color.Z);
                }

                indices.Add(gpuIndex);
            }
        }

        return new CubeGpuGeometry(vertices.ToArray(), indices.ToArray());
    }

    internal readonly record struct CubeGpuGeometry(float[] Vertices, ushort[] Indices);
}
