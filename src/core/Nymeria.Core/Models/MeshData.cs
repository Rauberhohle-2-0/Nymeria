using System.Numerics;

namespace Nymeria.Core.Models;

/// <summary>
/// A validated, format-independent triangle mesh held in CPU memory.
/// </summary>
/// <remarks>
/// <para>
/// Positions, normals, and texture coordinates are parallel arrays indexed by
/// vertex. Normals and texture coordinates are optional, but when present they
/// must contain one entry per position. Indices are zero-based and describe a
/// triangle list.
/// </para>
/// <para>
/// The constructor takes ownership of the supplied memory instead of copying
/// it. Callers must not modify those arrays after constructing the mesh.
/// </para>
/// <para>
/// The coordinate and winding conventions are documented in
/// <c>docs/MODEL_DATA.md</c>.
/// </para>
/// </remarks>
public sealed class MeshData
{
    private readonly ReadOnlyMemory<Vector3> _positions;
    private readonly ReadOnlyMemory<Vector3> _normals;
    private readonly ReadOnlyMemory<Vector2> _textureCoordinates;
    private readonly ReadOnlyMemory<uint> _indices;

    /// <summary>
    /// Creates a mesh and validates its geometry.
    /// </summary>
    /// <param name="positions">
    /// The model-space position of each vertex. Must not be empty and must contain
    /// only finite values.
    /// </param>
    /// <param name="normals">
    /// The model-space normal of each vertex, or an empty sequence when the source
    /// has no normals. When present, it must match the position count and contain
    /// only finite values.
    /// </param>
    /// <param name="textureCoordinates">
    /// The texture coordinate of each vertex, or an empty sequence when the source
    /// has no texture coordinates. When present, it must match the position count
    /// and contain only finite values.
    /// </param>
    /// <param name="indices">
    /// Zero-based triangle indices. Must not be empty, must be divisible by three,
    /// and every value must reference a position.
    /// </param>
    /// <param name="subsets">
    /// Optional draw ranges with material names. When omitted or empty, the mesh
    /// gets one subset that covers all indices and has no material name.
    /// </param>
    /// <exception cref="ModelFormatException">
    /// The geometry or a subset violates a mesh invariant.
    /// </exception>
    public MeshData(
        ReadOnlyMemory<Vector3> positions,
        ReadOnlyMemory<Vector3> normals,
        ReadOnlyMemory<Vector2> textureCoordinates,
        ReadOnlyMemory<uint> indices,
        IReadOnlyList<MeshSubset>? subsets = null)
    {
        Bounds = ValidatePositions(positions);
        ValidateNormals(normals, positions.Length);
        ValidateTextureCoordinates(textureCoordinates, positions.Length);
        ValidateIndices(indices, positions.Length);

        _positions = positions;
        _normals = normals;
        _textureCoordinates = textureCoordinates;
        _indices = indices;
        Subsets = CreateSubsets(subsets, indices.Length);
    }

    /// <summary>
    /// Gets the model-space position of each vertex.
    /// </summary>
    public ReadOnlyMemory<Vector3> Positions => _positions;

    /// <summary>
    /// Gets the model-space axis-aligned bounding box of every position.
    /// </summary>
    /// <remarks>
    /// The box is computed once during construction, so it costs no per-frame
    /// traversal. It is local-space geometry data: a renderer transforms it by
    /// the object's model matrix when it needs world-space visibility bounds.
    /// See <see cref="BoundingBox.Transform"/> and <c>docs/CULLING.md</c>.
    /// </remarks>
    public BoundingBox Bounds { get; }

    /// <summary>
    /// Gets the model-space normal of each vertex, or an empty sequence when the
    /// mesh has no normals.
    /// </summary>
    public ReadOnlyMemory<Vector3> Normals => _normals;

    /// <summary>
    /// Gets the texture coordinate of each vertex, or an empty sequence when the
    /// mesh has no texture coordinates.
    /// </summary>
    public ReadOnlyMemory<Vector2> TextureCoordinates => _textureCoordinates;

    /// <summary>
    /// Gets the zero-based triangle indices.
    /// </summary>
    public ReadOnlyMemory<uint> Indices => _indices;

    /// <summary>
    /// Gets the draw ranges of this mesh. There is always at least one subset.
    /// </summary>
    public IReadOnlyList<MeshSubset> Subsets { get; }

    private static BoundingBox ValidatePositions(ReadOnlyMemory<Vector3> positions)
    {
        if (positions.IsEmpty)
        {
            throw new ModelFormatException("A mesh must contain at least one vertex position.");
        }

        ReadOnlySpan<Vector3> values = positions.Span;
        Vector3 min = new(float.MaxValue);
        Vector3 max = new(-float.MaxValue);

        for (int i = 0; i < values.Length; i++)
        {
            Vector3 position = values[i];

            if (!float.IsFinite(position.X) ||
                !float.IsFinite(position.Y) ||
                !float.IsFinite(position.Z))
            {
                throw new ModelFormatException(
                    $"Vertex position {i} contains a non-finite component.");
            }

            min = Vector3.Min(min, position);
            max = Vector3.Max(max, position);
        }

        return new BoundingBox(min, max);
    }

    private static void ValidateNormals(ReadOnlyMemory<Vector3> normals, int positionCount)
    {
        if (normals.IsEmpty)
        {
            return;
        }

        if (normals.Length != positionCount)
        {
            throw new ModelFormatException(
                $"The mesh has {positionCount} positions but {normals.Length} normals. " +
                "Normals must be empty or match the position count.");
        }

        ReadOnlySpan<Vector3> values = normals.Span;

        for (int i = 0; i < values.Length; i++)
        {
            Vector3 normal = values[i];

            if (!float.IsFinite(normal.X) ||
                !float.IsFinite(normal.Y) ||
                !float.IsFinite(normal.Z))
            {
                throw new ModelFormatException(
                    $"Vertex normal {i} contains a non-finite component.");
            }
        }
    }

    private static void ValidateTextureCoordinates(
        ReadOnlyMemory<Vector2> textureCoordinates,
        int positionCount)
    {
        if (textureCoordinates.IsEmpty)
        {
            return;
        }

        if (textureCoordinates.Length != positionCount)
        {
            throw new ModelFormatException(
                $"The mesh has {positionCount} positions but {textureCoordinates.Length} " +
                "texture coordinates. Texture coordinates must be empty or match the " +
                "position count.");
        }

        ReadOnlySpan<Vector2> values = textureCoordinates.Span;

        for (int i = 0; i < values.Length; i++)
        {
            Vector2 textureCoordinate = values[i];

            if (!float.IsFinite(textureCoordinate.X) ||
                !float.IsFinite(textureCoordinate.Y))
            {
                throw new ModelFormatException(
                    $"Texture coordinate {i} contains a non-finite component.");
            }
        }
    }

    private static void ValidateIndices(
        ReadOnlyMemory<uint> indices,
        int positionCount)
    {
        if (indices.IsEmpty)
        {
            throw new ModelFormatException("A mesh must contain at least one triangle index.");
        }

        if (indices.Length % 3 != 0)
        {
            throw new ModelFormatException(
                $"The mesh has {indices.Length} indices, which is not divisible by three. " +
                "Indices must describe whole triangles.");
        }

        ReadOnlySpan<uint> values = indices.Span;
        ulong vertexCount = (ulong)positionCount;

        for (int i = 0; i < values.Length; i++)
        {
            uint index = values[i];

            if (index >= vertexCount)
            {
                throw new ModelFormatException(
                    $"Index {i} references vertex {index}, but the mesh has only " +
                    $"{positionCount} vertices.");
            }
        }
    }

    private static IReadOnlyList<MeshSubset> CreateSubsets(
        IReadOnlyList<MeshSubset>? subsets,
        int indexCount)
    {
        if (subsets is null || subsets.Count == 0)
        {
            return Array.AsReadOnly(new[] { new MeshSubset(0, indexCount) });
        }

        var copy = new MeshSubset[subsets.Count];

        for (int i = 0; i < subsets.Count; i++)
        {
            MeshSubset subset = subsets[i];

            if (subset.IndexOffset < 0 ||
                subset.IndexCount <= 0 ||
                subset.IndexCount % 3 != 0 ||
                subset.IndexOffset % 3 != 0 ||
                (long)subset.IndexOffset + subset.IndexCount > indexCount)
            {
                throw new ModelFormatException(
                    $"Mesh subset {i} has an invalid range: offset " +
                    $"{subset.IndexOffset}, count {subset.IndexCount}, index count " +
                    $"{indexCount}. A subset must be triangle-aligned and lie within " +
                    "the index buffer.");
            }

            if (subset.MaterialName is { Length: 0 })
            {
                throw new ModelFormatException(
                    $"Mesh subset {i} has an empty material name. Use null when the " +
                    "subset has no material reference.");
            }

            copy[i] = subset;
        }

        return Array.AsReadOnly(copy);
    }
}
