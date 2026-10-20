using System.Numerics;
using Nymeria.Core.Models;

namespace Nymeria.Core.Tests;

public sealed class ModelParserContractTests
{
    [Fact]
    public void Parser_CanReturnModelFromCallerOwnedStream()
    {
        using var stream = new MemoryStream(new byte[] { 1, 2, 3 });
        IModelParser parser = new StubParser(_ => CreateTriangleModel());

        ModelData model = parser.Parse(stream);

        Assert.Equal("triangle", model.Name);
        Assert.True(stream.CanRead);
    }

    [Fact]
    public void Parser_PropagatesModelFormatException()
    {
        using var stream = new MemoryStream();
        IModelParser parser = new StubParser(_ =>
            throw new ModelFormatException("unsupported chunk"));

        ModelFormatException exception = Assert.Throws<ModelFormatException>(
            () => parser.Parse(stream));

        Assert.Equal("unsupported chunk", exception.Message);
    }

    [Fact]
    public void InvalidGeometry_CannotCrossParserBoundary()
    {
        using var stream = new MemoryStream();
        IModelParser parser = new StubParser(_ => new ModelData(
            "broken",
            new[]
            {
                new MeshData(
                    new[]
                    {
                        new Vector3(0.0f, 0.0f, 0.0f),
                        new Vector3(1.0f, 0.0f, 0.0f),
                        new Vector3(0.0f, 1.0f, 0.0f),
                    },
                    ReadOnlyMemory<Vector3>.Empty,
                    ReadOnlyMemory<Vector2>.Empty,
                    new uint[] { 0, 1, 3 }),
            }));

        Assert.Throws<ModelFormatException>(() => parser.Parse(stream));
    }

    private static ModelData CreateTriangleModel()
    {
        var mesh = new MeshData(
            new[]
            {
                new Vector3(0.0f, 0.0f, 0.0f),
                new Vector3(1.0f, 0.0f, 0.0f),
                new Vector3(0.0f, 1.0f, 0.0f),
            },
            ReadOnlyMemory<Vector3>.Empty,
            ReadOnlyMemory<Vector2>.Empty,
            new uint[] { 0, 1, 2 });

        return new ModelData("triangle", new[] { mesh });
    }

    private sealed class StubParser(Func<Stream, ModelData> parse) : IModelParser
    {
        public ModelData Parse(Stream stream)
        {
            ArgumentNullException.ThrowIfNull(stream);
            return parse(stream);
        }
    }
}
