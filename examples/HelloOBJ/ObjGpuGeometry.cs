using System.Numerics;
using Nymeria.Core.Models;

namespace HelloOBJ;

/// <summary>
/// Converts a parsed model to the interleaved position/color vertex layout used
/// by this example's existing GPU pipeline.
/// </summary>
/// <remarks>
/// The parser preserves OBJ <c>usemtl</c> names as opaque subset labels but
/// loads no material library. This example maps its fixture's six labels to
/// solid colors and uses one neutral color for every other label. It is not a
/// material system.
/// </remarks>
internal static class ObjGpuGeometryBuilder
{
    private const int FloatsPerVertex = 6;

    private static readonly Vector3 DefaultColor = new(0.62f, 0.68f, 0.78f);

    private static readonly Dictionary<string, Vector3> s_materialColors =
        new(StringComparer.Ordinal)
        {
            ["front"] = new Vector3(0.90f, 0.15f, 0.15f),
            ["back"] = new Vector3(0.15f, 0.75f, 0.25f),
            ["right"] = new Vector3(0.20f, 0.45f, 0.95f),
            ["left"] = new Vector3(0.95f, 0.75f, 0.15f),
            ["top"] = new Vector3(0.85f, 0.30f, 0.85f),
            ["bottom"] = new Vector3(0.20f, 0.80f, 0.85f),
        };

    public static ObjGpuGeometry Build(ModelData model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var vertices = new List<float>();
        var indices = new List<uint>();

        foreach (MeshData mesh in model.Meshes)
        {
            ReadOnlySpan<Vector3> positions = mesh.Positions.Span;
            ReadOnlySpan<uint> sourceIndices = mesh.Indices.Span;

            foreach (MeshSubset subset in mesh.Subsets)
            {
                Vector3 color = ResolveColor(subset.MaterialName);

                // A source vertex that appears in subsets with different labels
                // cannot carry one color, so each subset emits its own vertices.
                var vertexMap = new Dictionary<uint, uint>();

                for (int i = subset.IndexOffset; i < subset.IndexOffset + subset.IndexCount; i++)
                {
                    uint sourceIndex = sourceIndices[i];

                    if (!vertexMap.TryGetValue(sourceIndex, out uint gpuIndex))
                    {
                        int nextIndex = vertices.Count / FloatsPerVertex;
                        gpuIndex = (uint)nextIndex;
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
        }

        return new ObjGpuGeometry(vertices.ToArray(), indices.ToArray());
    }

    private static Vector3 ResolveColor(string? materialName)
    {
        return materialName is not null &&
               s_materialColors.TryGetValue(materialName, out Vector3 color)
            ? color
            : DefaultColor;
    }
}

internal readonly record struct ObjGpuGeometry(float[] Vertices, uint[] Indices);
