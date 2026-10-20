using System.Numerics;
using Nymeria.Core.Models;

namespace Nymeria.Core.Tests;

public sealed class ModelDataTests
{
    private static MeshData CreateTriangle()
    {
        return new MeshData(
            new[]
            {
                new Vector3(0.0f, 0.0f, 0.0f),
                new Vector3(1.0f, 0.0f, 0.0f),
                new Vector3(0.0f, 1.0f, 0.0f),
            },
            ReadOnlyMemory<Vector3>.Empty,
            ReadOnlyMemory<Vector2>.Empty,
            new uint[] { 0, 1, 2 });
    }

    [Fact]
    public void Model_ExposesNameAndMeshes()
    {
        MeshData mesh = CreateTriangle();
        var model = new ModelData("triangle", new[] { mesh });

        Assert.Equal("triangle", model.Name);
        Assert.Same(mesh, Assert.Single(model.Meshes));
    }

    [Fact]
    public void Model_ExposesMultipleMeshes()
    {
        MeshData first = CreateTriangle();
        MeshData second = CreateTriangle();

        var model = new ModelData("pair", new[] { first, second });

        Assert.Equal(new[] { first, second }, model.Meshes);
    }

    [Fact]
    public void Model_RejectsEmptyMeshList()
    {
        ModelFormatException exception = Assert.Throws<ModelFormatException>(() =>
            new ModelData("empty", Array.Empty<MeshData>()));

        Assert.Contains("at least one mesh", exception.Message);
    }

    [Fact]
    public void Model_RejectsNullMesh()
    {
        var meshes = new List<MeshData> { null! };

        ModelFormatException exception = Assert.Throws<ModelFormatException>(() =>
            new ModelData("broken", meshes));

        Assert.Contains("mesh 0 is null", exception.Message);
    }

    [Fact]
    public void Model_RejectsBlankName()
    {
        Assert.Throws<ArgumentException>(() =>
            new ModelData("  ", new[] { CreateTriangle() }));
    }

    [Fact]
    public void Model_RejectsNullMeshList()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new ModelData("triangle", null!));
    }

    [Fact]
    public void ModelFormatException_IsFormatException()
    {
        var exception = new ModelFormatException("malformed");

        Assert.IsAssignableFrom<FormatException>(exception);
        Assert.Equal("malformed", exception.Message);
    }
}
