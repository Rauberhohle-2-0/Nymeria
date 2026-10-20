using System.Numerics;
using Nymeria.Core.Models;

namespace Nymeria.Core.Tests;

public sealed class MeshDataTests
{
    private static readonly Vector3[] s_positions =
    {
        new(0.0f, 0.0f, 0.0f),
        new(1.0f, 0.0f, 0.0f),
        new(0.0f, 1.0f, 0.0f),
        new(1.0f, 1.0f, 0.0f),
    };

    private static readonly Vector3[] s_normals =
    {
        Vector3.UnitZ,
        Vector3.UnitZ,
        Vector3.UnitZ,
        Vector3.UnitZ,
    };

    private static readonly Vector2[] s_textureCoordinates =
    {
        new(0.0f, 0.0f),
        new(1.0f, 0.0f),
        new(0.0f, 1.0f),
        new(1.0f, 1.0f),
    };

    private static readonly uint[] s_indices = { 0, 1, 2, 2, 1, 3 };

    [Fact]
    public void Mesh_ExposesGeometryAndDefaultSubset()
    {
        var mesh = new MeshData(
            s_positions,
            s_normals,
            s_textureCoordinates,
            s_indices);

        Assert.Equal(s_positions, mesh.Positions.ToArray());
        Assert.Equal(s_normals, mesh.Normals.ToArray());
        Assert.Equal(s_textureCoordinates, mesh.TextureCoordinates.ToArray());
        Assert.Equal(s_indices, mesh.Indices.ToArray());

        MeshSubset subset = Assert.Single(mesh.Subsets);
        Assert.Equal(0, subset.IndexOffset);
        Assert.Equal(s_indices.Length, subset.IndexCount);
        Assert.Null(subset.MaterialName);
    }

    [Fact]
    public void Mesh_DoesNotCopyGeometryArrays()
    {
        ReadOnlyMemory<Vector3> positions = s_positions;
        ReadOnlyMemory<Vector3> normals = s_normals;
        ReadOnlyMemory<Vector2> textureCoordinates = s_textureCoordinates;
        ReadOnlyMemory<uint> indices = s_indices;

        var mesh = new MeshData(positions, normals, textureCoordinates, indices);

        Assert.True(mesh.Positions.Equals(positions));
        Assert.True(mesh.Normals.Equals(normals));
        Assert.True(mesh.TextureCoordinates.Equals(textureCoordinates));
        Assert.True(mesh.Indices.Equals(indices));
    }

    [Fact]
    public void Mesh_ExposesExplicitSubsets()
    {
        var subsets = new[]
        {
            new MeshSubset(0, 3, "first"),
            new MeshSubset(3, 3, "second"),
        };

        var mesh = new MeshData(
            s_positions,
            s_normals,
            s_textureCoordinates,
            s_indices,
            subsets);

        Assert.Equal(subsets, mesh.Subsets);
    }

    [Fact]
    public void Mesh_AllowsAbsentNormalsAndTextureCoordinates()
    {
        var mesh = new MeshData(
            s_positions,
            ReadOnlyMemory<Vector3>.Empty,
            ReadOnlyMemory<Vector2>.Empty,
            s_indices);

        Assert.True(mesh.Normals.IsEmpty);
        Assert.True(mesh.TextureCoordinates.IsEmpty);
    }

    [Fact]
    public void Mesh_RejectsEmptyPositions()
    {
        ModelFormatException exception = Assert.Throws<ModelFormatException>(() =>
            new MeshData(
                ReadOnlyMemory<Vector3>.Empty,
                ReadOnlyMemory<Vector3>.Empty,
                ReadOnlyMemory<Vector2>.Empty,
                s_indices));

        Assert.Contains("at least one vertex position", exception.Message);
    }

    [Fact]
    public void Mesh_RejectsEmptyIndices()
    {
        ModelFormatException exception = Assert.Throws<ModelFormatException>(() =>
            new MeshData(
                s_positions,
                ReadOnlyMemory<Vector3>.Empty,
                ReadOnlyMemory<Vector2>.Empty,
                ReadOnlyMemory<uint>.Empty));

        Assert.Contains("at least one triangle index", exception.Message);
    }

    [Fact]
    public void Mesh_RejectsIndexCountNotDivisibleByThree()
    {
        ModelFormatException exception = Assert.Throws<ModelFormatException>(() =>
            new MeshData(
                s_positions,
                ReadOnlyMemory<Vector3>.Empty,
                ReadOnlyMemory<Vector2>.Empty,
                new uint[] { 0, 1 }));

        Assert.Contains("not divisible by three", exception.Message);
    }

    [Fact]
    public void Mesh_RejectsOutOfRangeIndex()
    {
        ModelFormatException exception = Assert.Throws<ModelFormatException>(() =>
            new MeshData(
                s_positions,
                ReadOnlyMemory<Vector3>.Empty,
                ReadOnlyMemory<Vector2>.Empty,
                new uint[] { 0, 1, 4 }));

        Assert.Contains("references vertex 4", exception.Message);
    }

    [Fact]
    public void Mesh_RejectsNonFinitePosition()
    {
        var positions = (Vector3[])s_positions.Clone();
        positions[1] = new Vector3(float.NaN, 0.0f, 0.0f);

        ModelFormatException exception = Assert.Throws<ModelFormatException>(() =>
            new MeshData(
                positions,
                ReadOnlyMemory<Vector3>.Empty,
                ReadOnlyMemory<Vector2>.Empty,
                s_indices));

        Assert.Contains("Vertex position 1", exception.Message);
    }

    [Fact]
    public void Mesh_RejectsNonFiniteNormal()
    {
        var normals = (Vector3[])s_normals.Clone();
        normals[2] = new Vector3(0.0f, float.PositiveInfinity, 0.0f);

        ModelFormatException exception = Assert.Throws<ModelFormatException>(() =>
            new MeshData(
                s_positions,
                normals,
                ReadOnlyMemory<Vector2>.Empty,
                s_indices));

        Assert.Contains("Vertex normal 2", exception.Message);
    }

    [Fact]
    public void Mesh_RejectsNonFiniteTextureCoordinate()
    {
        var textureCoordinates = (Vector2[])s_textureCoordinates.Clone();
        textureCoordinates[3] = new Vector2(0.0f, float.NegativeInfinity);

        ModelFormatException exception = Assert.Throws<ModelFormatException>(() =>
            new MeshData(
                s_positions,
                ReadOnlyMemory<Vector3>.Empty,
                textureCoordinates,
                s_indices));

        Assert.Contains("Texture coordinate 3", exception.Message);
    }

    [Fact]
    public void Mesh_RejectsNormalCountMismatch()
    {
        ModelFormatException exception = Assert.Throws<ModelFormatException>(() =>
            new MeshData(
                s_positions,
                s_normals[..2],
                ReadOnlyMemory<Vector2>.Empty,
                s_indices));

        Assert.Contains("must be empty or match the position count", exception.Message);
    }

    [Fact]
    public void Mesh_RejectsTextureCoordinateCountMismatch()
    {
        ModelFormatException exception = Assert.Throws<ModelFormatException>(() =>
            new MeshData(
                s_positions,
                ReadOnlyMemory<Vector3>.Empty,
                s_textureCoordinates[..3],
                s_indices));

        Assert.Contains("must be empty or match the position count", exception.Message);
    }

    [Theory]
    [InlineData(-3, 3)]
    [InlineData(1, 3)]
    [InlineData(0, 0)]
    [InlineData(0, 2)]
    [InlineData(0, 9)]
    public void Mesh_RejectsInvalidSubsetRange(int offset, int count)
    {
        ModelFormatException exception = Assert.Throws<ModelFormatException>(() =>
            new MeshData(
                s_positions,
                ReadOnlyMemory<Vector3>.Empty,
                ReadOnlyMemory<Vector2>.Empty,
                s_indices,
                new[] { new MeshSubset(offset, count, "material") }));

        Assert.Contains("invalid range", exception.Message);
    }

    [Fact]
    public void Mesh_RejectsEmptyMaterialName()
    {
        ModelFormatException exception = Assert.Throws<ModelFormatException>(() =>
            new MeshData(
                s_positions,
                ReadOnlyMemory<Vector3>.Empty,
                ReadOnlyMemory<Vector2>.Empty,
                s_indices,
                new[] { new MeshSubset(0, s_indices.Length, string.Empty) }));

        Assert.Contains("empty material name", exception.Message);
    }
}
