using System.Globalization;
using System.Numerics;
using System.Text;
using Nymeria.Core.Models;
using Nymeria.Formats.Obj;

namespace Nymeria.Formats.Obj.Tests;

public sealed class ObjModelParserTests
{
    [Fact]
    public void ParsesTriangleWithPositionsOnly()
    {
        ModelData model = Parse("""
            v 0 0 0
            v 1 0 0
            v 0 1 0
            f 1 2 3
            """);

        Assert.Equal("OBJ", model.Name);
        MeshData mesh = Assert.Single(model.Meshes);
        Assert.Equal(3, mesh.Positions.Length);
        Assert.True(mesh.Normals.IsEmpty);
        Assert.True(mesh.TextureCoordinates.IsEmpty);
        Assert.Equal(new uint[] { 0, 1, 2 }, mesh.Indices.ToArray());

        MeshSubset subset = Assert.Single(mesh.Subsets);
        Assert.Equal(0, subset.IndexOffset);
        Assert.Equal(3, subset.IndexCount);
        Assert.Null(subset.MaterialName);
    }

    [Fact]
    public void TriangulatesQuadWithFan()
    {
        ModelData model = Parse("""
            v 0 0 0
            v 1 0 0
            v 1 1 0
            v 0 1 0
            f 1 2 3 4
            """);

        MeshData mesh = Assert.Single(model.Meshes);
        Assert.Equal(4, mesh.Positions.Length);
        Assert.Equal(new uint[] { 0, 1, 2, 0, 2, 3 }, mesh.Indices.ToArray());
    }

    [Fact]
    public void TriangulatesConvexPentagonWithFan()
    {
        ModelData model = Parse("""
            v 0 0 0
            v 1 0 0
            v 1.5 1 0
            v 0.5 2 0
            v -0.5 1 0
            f 1 2 3 4 5
            """);

        MeshData mesh = Assert.Single(model.Meshes);
        Assert.Equal(5, mesh.Positions.Length);
        Assert.Equal(
            new uint[] { 0, 1, 2, 0, 2, 3, 0, 3, 4 },
            mesh.Indices.ToArray());
    }

    [Fact]
    public void SharesCornersAcrossTriangles()
    {
        ModelData model = Parse("""
            v 0 0 0
            v 1 0 0
            v 0 1 0
            v 1 1 0
            f 1 2 3
            f 1 3 4
            """);

        MeshData mesh = Assert.Single(model.Meshes);
        Assert.Equal(4, mesh.Positions.Length);
        Assert.Equal(new uint[] { 0, 1, 2, 0, 2, 3 }, mesh.Indices.ToArray());
    }

    [Fact]
    public void DuplicatesCornersWithDifferentAttributeCombinations()
    {
        ModelData model = Parse("""
            v 0 0 0
            v 1 0 0
            v 0 1 0
            vt 0 0
            vt 1 0
            f 1/1 2/1 3/1
            f 1/2 2/1 3/1
            """);

        MeshData mesh = Assert.Single(model.Meshes);
        Assert.Equal(4, mesh.Positions.Length);
        Assert.Equal(new uint[] { 0, 1, 2, 3, 1, 2 }, mesh.Indices.ToArray());
    }

    [Fact]
    public void ParsesPositionTextureFaceReferences()
    {
        ModelData model = Parse("""
            v 0 0 0
            v 1 0 0
            v 0 1 0
            vt 0 0
            vt 1 0
            vt 0 1
            f 1/1 2/2 3/3
            """);

        MeshData mesh = Assert.Single(model.Meshes);
        Assert.True(mesh.Normals.IsEmpty);
        Assert.Equal(
            new[] { new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, 0) },
            mesh.TextureCoordinates.ToArray());
        Assert.Equal(new uint[] { 0, 1, 2 }, mesh.Indices.ToArray());
    }

    [Fact]
    public void ParsesPositionNormalFaceReferences()
    {
        ModelData model = Parse("""
            v 0 0 0
            v 1 0 0
            v 0 1 0
            vn 0 0 1
            f 1//1 2//1 3//1
            """);

        MeshData mesh = Assert.Single(model.Meshes);
        Assert.True(mesh.TextureCoordinates.IsEmpty);
        Assert.Equal(
            new[]
            {
                new Vector3(0, 0, 1),
                new Vector3(0, 0, 1),
                new Vector3(0, 0, 1),
            },
            mesh.Normals.ToArray());
        Assert.Equal(new uint[] { 0, 1, 2 }, mesh.Indices.ToArray());
    }

    [Fact]
    public void ParsesPositionTextureNormalFaceReferences()
    {
        ModelData model = Parse("""
            v 0 0 0
            v 1 0 0
            v 0 1 0
            vt 0 0
            vt 1 0
            vn 0 0 1
            f 1/1/1 2/2/1 3/1/1
            """);

        MeshData mesh = Assert.Single(model.Meshes);
        Assert.Equal(3, mesh.TextureCoordinates.Length);
        Assert.Equal(3, mesh.Normals.Length);
        Assert.Equal(new uint[] { 0, 1, 2 }, mesh.Indices.ToArray());
    }

    [Fact]
    public void PreservesIndependentPositionTextureAndNormalIndices()
    {
        ModelData model = Parse("""
            v 0 0 0
            v 1 0 0
            v 0 1 0
            vt 0 0
            vt 1 0
            vn 0 0 1
            vn 0 1 0
            f 1/2/1 2/1/2 3/2/1
            """);

        MeshData mesh = Assert.Single(model.Meshes);

        Assert.Equal(
            new[]
            {
                new Vector3(0, 0, 0),
                new Vector3(1, 0, 0),
                new Vector3(0, 1, 0),
            },
            mesh.Positions.ToArray());
        Assert.Equal(
            new[] { new Vector2(1, 1), new Vector2(0, 1), new Vector2(1, 1) },
            mesh.TextureCoordinates.ToArray());
        Assert.Equal(
            new[]
            {
                new Vector3(0, 0, 1),
                new Vector3(0, 1, 0),
                new Vector3(0, 0, 1),
            },
            mesh.Normals.ToArray());
        Assert.Equal(new uint[] { 0, 1, 2 }, mesh.Indices.ToArray());
    }

    [Fact]
    public void ResolvesNegativePositionIndicesRelativeToCurrentDefinitions()
    {
        ModelData model = Parse("""
            v 0 0 0
            v 1 0 0
            v 0 1 0
            f -3 -2 -1
            v 5 5 5
            v 6 5 5
            v 5 6 5
            f -3 -2 -1
            """);

        MeshData mesh = Assert.Single(model.Meshes);
        Assert.Equal(6, mesh.Positions.Length);
        Assert.Equal(new uint[] { 0, 1, 2, 3, 4, 5 }, mesh.Indices.ToArray());
    }

    [Fact]
    public void ResolvesNegativeTextureAndNormalIndices()
    {
        ModelData model = Parse("""
            v 0 0 0
            v 1 0 0
            v 0 1 0
            vt 0 0
            vt 1 0
            vn 0 0 1
            f -3/-2/-1 -2/-1/-1 -1/-1/-1
            """);

        MeshData mesh = Assert.Single(model.Meshes);
        Assert.Equal(
            new[] { new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 1) },
            mesh.TextureCoordinates.ToArray());
        Assert.Equal(new uint[] { 0, 1, 2 }, mesh.Indices.ToArray());
    }

    [Fact]
    public void LeavesAttributeStreamsEmptyWhenNoFaceUsesThem()
    {
        ModelData model = Parse("""
            v 0 0 0
            v 1 0 0
            v 0 1 0
            vt 0 0
            vn 0 0 1
            f 1 2 3
            """);

        MeshData mesh = Assert.Single(model.Meshes);
        Assert.True(mesh.TextureCoordinates.IsEmpty);
        Assert.True(mesh.Normals.IsEmpty);
    }

    [Fact]
    public void FillsMissingCornerAttributesWithDefaultsWhenTheStreamIsPresent()
    {
        ModelData model = Parse("""
            v 0 0 0
            v 1 0 0
            v 0 1 0
            vt 0 0
            vn 0 0 1
            f 1/1/1 2/1/1 3/1/1
            f 1 2 3
            """);

        MeshData mesh = Assert.Single(model.Meshes);
        Assert.Equal(6, mesh.TextureCoordinates.Length);
        Assert.Equal(6, mesh.Normals.Length);

        Assert.Equal(new Vector2(0, 1), mesh.TextureCoordinates.Span[0]);
        Assert.Equal(Vector2.Zero, mesh.TextureCoordinates.Span[3]);
        Assert.Equal(new Vector3(0, 0, 1), mesh.Normals.Span[0]);
        Assert.Equal(Vector3.Zero, mesh.Normals.Span[3]);
        Assert.Equal(new uint[] { 0, 1, 2, 3, 4, 5 }, mesh.Indices.ToArray());
    }

    [Fact]
    public void ConvertsTextureCoordinatesToTopLeftOrigin()
    {
        ModelData model = Parse("""
            v 0 0 0
            v 1 0 0
            v 0 1 0
            vt 0 0
            vt 1 1
            vt 0.25 0.75
            f 1/1 2/2 3/3
            """);

        MeshData mesh = Assert.Single(model.Meshes);
        Assert.Equal(
            new[] { new Vector2(0, 1), new Vector2(1, 0), new Vector2(0.25f, 0.25f) },
            mesh.TextureCoordinates.ToArray());
    }

    [Fact]
    public void AcceptsOptionalPositionAndTextureCoordinateComponents()
    {
        ModelData model = Parse("""
            v 1 2 3 1.0
            v 2 0 0
            v 0 2 0
            vt 0.5
            vt 0.25 0.75 0.0
            vt 0 1
            vn 0 0 1
            f 1/1/1 2/2/1 3/3/1
            """);

        MeshData mesh = Assert.Single(model.Meshes);
        Assert.Equal(new Vector3(1, 2, 3), mesh.Positions.Span[0]);
        Assert.Equal(
            new[] { new Vector2(0.5f, 1), new Vector2(0.25f, 0.25f), new Vector2(0, 0) },
            mesh.TextureCoordinates.ToArray());
    }

    [Fact]
    public void HandlesCommentsWhitespaceAndMixedLineEndings()
    {
        string obj =
            "# leading comment\r\n" +
            "\t  v 0 0 0  \r\n" +
            "v\t1 0 0 # right\n" +
            "  v 0 1 0\r" +
            "f\t1 2 3 # triangle\n" +
            "\r\n" +
            "   \n";

        ModelData model = Parse(obj);

        MeshData mesh = Assert.Single(model.Meshes);
        Assert.Equal(3, mesh.Positions.Length);
        Assert.Equal(new uint[] { 0, 1, 2 }, mesh.Indices.ToArray());
    }

    [Fact]
    public void ParsesNumbersWithInvariantCulture()
    {
        CultureInfo previousCulture = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");

            ModelData model = Parse("""
                v 1.5 -2.25 3e1
                v 0 0 0
                v 0 0 0
                f 1 2 3
                """);

            Assert.Equal(
                new Vector3(1.5f, -2.25f, 30.0f),
                Assert.Single(model.Meshes).Positions.Span[0]);
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
        }
    }

    [Fact]
    public void RejectsDecimalCommaUnderGermanCulture()
    {
        CultureInfo previousCulture = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");

            ModelFormatException exception = ParseError("""
                v 1,5 0 0
                f 1 1 1
                """);

            Assert.Contains("position x", exception.Message);
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
        }
    }

    [Fact]
    public void CreatesSubsetsFromMaterialChanges()
    {
        ModelData model = Parse("""
            v 0 0 0
            v 1 0 0
            v 0 1 0
            v 1 1 0
            f 1 2 3
            usemtl red paint
            f 2 4 3
            usemtl
            f 1 3 2
            """);

        MeshData mesh = Assert.Single(model.Meshes);
        Assert.Equal(3, mesh.Subsets.Count);

        Assert.Equal(new MeshSubset(0, 3), mesh.Subsets[0]);
        Assert.Equal(new MeshSubset(3, 3, "red paint"), mesh.Subsets[1]);
        Assert.Equal(new MeshSubset(6, 3), mesh.Subsets[2]);
    }

    [Fact]
    public void RecognizesObjectGroupSmoothingAndMaterialLibraryDirectives()
    {
        ModelData model = Parse("""
            o My Model
            g group
            s 1
            mtllib missing.mtl
            maplib missing.map
            usemap none
            v 0 0 0
            v 1 0 0
            v 0 1 0
            f 1 2 3
            o Ignored Second Object
            g other
            f 1 2 3
            """);

        Assert.Equal("My Model", model.Name);
        MeshData mesh = Assert.Single(model.Meshes);
        Assert.Equal(6, mesh.Indices.Length);
    }

    [Fact]
    public void UsesSourceNameWhenTheObjectHasNoName()
    {
        ModelData model = Parse(
            """
            v 0 0 0
            v 1 0 0
            v 0 1 0
            f 1 2 3
            """,
            "fallback.obj");

        Assert.Equal("fallback.obj", model.Name);
    }

    [Fact]
    public void IncludesSourceNameAndLineNumberInDiagnostics()
    {
        ModelFormatException exception = ParseError(
            """
            v 0 0 0
            f 1 2 3
            """,
            "broken.obj");

        Assert.Contains("broken.obj", exception.Message);
        Assert.Contains("line 2", exception.Message);
        Assert.Contains("references position 2", exception.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  \r\n\t\n")]
    [InlineData("# only a comment\n  # another comment\n")]
    [InlineData("v 0 0 0\nv 1 0 0\nv 0 1 0\n")]
    public void InputWithoutFacesFails(string obj)
    {
        ModelFormatException exception = ParseError(obj);

        Assert.Contains("no faces", exception.Message);
    }

    [Theory]
    [InlineData("v 1 2\n", "position requires")]
    [InlineData("v nope 0 0\n", "position x")]
    [InlineData("v NaN 0 0\n", "not a finite")]
    [InlineData("v 1e999 0 0\n", "not a finite")]
    [InlineData("v 0 0 0\nvt\nf 1/1/1 1 1\n", "texture coordinate requires")]
    [InlineData("v 0 0 0\nvn 0 1\nf 1//1 1 1\n", "normal requires")]
    [InlineData("v 0 0 0\nv 1 0 0\nv 0 1 0\nf 1 2\n", "face requires")]
    [InlineData("v 0 0 0\nf 0 1 1\n", "index 0")]
    [InlineData("v 0 0 0\nf 1 2 3\n", "references position 2")]
    [InlineData("v 0 0 0\nv 1 0 0\nv 0 1 0\nf -4 1 2\n", "position -4")]
    [InlineData("f 1 2 3\n", "0 position")]
    [InlineData("v 0 0 0\nf 1// 1 1\n", "neither a texture coordinate nor a normal")]
    [InlineData("v 0 0 0\nf /1/1 1 1\n", "no position index")]
    [InlineData("v 0 0 0\nf 1/1/1/1 1 1\n", "more than three")]
    [InlineData("v 0 0 0\nf 1/x 1 1\n", "invalid texture coordinate")]
    [InlineData("v 0 0 0\nf 1/ 1 1\n", "empty texture coordinate")]
    [InlineData("v 0 0 0\nl 1 1\n", "'l' directive is not supported")]
    [InlineData("v 0 0 0\nfoo bar\n", "unsupported OBJ directive")]
    public void MalformedInputFailsWithDiagnostics(string obj, string expected)
    {
        ModelFormatException exception = ParseError(obj);

        Assert.Contains(expected, exception.Message);
        Assert.Contains("line", exception.Message);
    }

    [Fact]
    public void ParserDoesNotDisposeTheStreamOnSuccess()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("""
            v 0 0 0
            v 1 0 0
            v 0 1 0
            f 1 2 3
            """));

        ModelData model = new ObjModelParser().Parse(stream);

        Assert.Single(model.Meshes);
        Assert.True(stream.CanRead);
    }

    [Fact]
    public void ParserDoesNotDisposeTheStreamOnFailure()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("f 0 1 2\n"));

        Assert.Throws<ModelFormatException>(() => new ObjModelParser().Parse(stream));
        Assert.True(stream.CanRead);
    }

    [Fact]
    public void ParserReadsFromTheStreamsCurrentPosition()
    {
        byte[] prefix = Encoding.UTF8.GetBytes("ignored by the test\n");
        byte[] body = Encoding.UTF8.GetBytes("""
            v 0 0 0
            v 1 0 0
            v 0 1 0
            f 1 2 3
            """);

        using var stream = new MemoryStream(prefix.Concat(body).ToArray());
        stream.Position = prefix.Length;

        ModelData model = new ObjModelParser().Parse(stream);

        Assert.Equal(3, Assert.Single(model.Meshes).Positions.Length);
    }

    [Fact]
    public void ParserRejectsNullStream()
    {
        Assert.Throws<ArgumentNullException>(() => new ObjModelParser().Parse(null!));
    }

    [Fact]
    public void ParsesCheckedInHelloObjFixture()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Assets", "cube.obj");

        using FileStream stream = File.OpenRead(path);
        ModelData model = new ObjModelParser(path).Parse(stream);

        Assert.Equal("Cube", model.Name);

        MeshData mesh = Assert.Single(model.Meshes);
        Assert.Equal(24, mesh.Positions.Length);
        Assert.Equal(24, mesh.TextureCoordinates.Length);
        Assert.Equal(24, mesh.Normals.Length);
        Assert.Equal(36, mesh.Indices.Length);

        Assert.Equal(
            new[] { "front", "back", "right", "left", "top", "bottom" },
            mesh.Subsets.Select(subset => subset.MaterialName).ToArray());
        Assert.All(mesh.Subsets, subset =>
        {
            Assert.Equal(0, subset.IndexOffset % 6);
            Assert.Equal(6, subset.IndexCount);
        });
    }

    [Fact]
    public void RejectsInputLargerThanTheConfiguredLimit()
    {
        var limits = new ObjImportLimits(
            maxInputBytes: 24,
            maxVertexCount: 100,
            maxTriangleCount: 100,
            maxSubsetCount: 10,
            maxLineLength: 100);

        ModelFormatException exception = ParseError(
            """
            v 0 0 0
            v 1 0 0
            v 0 1 0
            f 1 2 3
            """,
            limits: limits);

        Assert.Contains("larger than the supported limit", exception.Message);
        Assert.Contains("24 bytes", exception.Message);
    }

    [Fact]
    public void AcceptsInputThatEndsExactlyAtTheConfiguredLimit()
    {
        const string obj = "v 0 0 0\nv 1 0 0\nv 0 1 0\nf 1 2 3\n";
        int length = Encoding.UTF8.GetByteCount(obj);
        var limits = new ObjImportLimits(length, 100, 100, 10, 100);

        ModelData model = Parse(obj, limits: limits);

        Assert.Equal(3, Assert.Single(model.Meshes).Positions.Length);
    }

    [Fact]
    public void RejectsLinesLongerThanTheConfiguredLimit()
    {
        var limits = new ObjImportLimits(1024, 100, 100, 10, maxLineLength: 8);

        ModelFormatException exception = ParseError(
            "v 0 0 0 0 0\n",
            limits: limits);

        Assert.Contains("line is longer", exception.Message);
        Assert.Contains("8 characters", exception.Message);
    }

    [Fact]
    public void RejectsAttributeStreamsLargerThanTheConfiguredLimit()
    {
        var limits = new ObjImportLimits(1024, maxVertexCount: 3, 100, 10, 100);

        ModelFormatException exception = ParseError(
            """
            v 0 0 0
            v 1 0 0
            v 0 1 0
            v 1 1 0
            f 1 2 3
            """,
            limits: limits);

        Assert.Contains("more than 3 position", exception.Message);
    }

    [Fact]
    public void RejectsMeshVerticesLargerThanTheConfiguredLimit()
    {
        var limits = new ObjImportLimits(1024, maxVertexCount: 4, 100, 10, 100);

        ModelFormatException exception = ParseError(
            """
            v 0 0 0
            v 1 0 0
            v 0 1 0
            vt 0 0
            vt 1 0
            f 1/1 2/1 3/1
            f 1/2 2/2 3/2
            """,
            limits: limits);

        Assert.Contains("mesh vertices", exception.Message);
        Assert.Contains("limit of 4", exception.Message);
    }

    [Fact]
    public void RejectsTrianglesLargerThanTheConfiguredLimit()
    {
        var limits = new ObjImportLimits(1024, 100, maxTriangleCount: 1, 10, 100);

        ModelFormatException exception = ParseError(
            """
            v 0 0 0
            v 1 0 0
            v 1 1 0
            v 0 1 0
            f 1 2 3 4
            """,
            limits: limits);

        Assert.Contains("limit of 1 triangles", exception.Message);
    }

    [Fact]
    public void RejectsSubsetsLargerThanTheConfiguredLimit()
    {
        var limits = new ObjImportLimits(1024, 100, 100, maxSubsetCount: 1, 100);

        ModelFormatException exception = ParseError(
            """
            v 0 0 0
            v 1 0 0
            v 0 1 0
            f 1 2 3
            usemtl red
            f 1 2 3
            """,
            limits: limits);

        Assert.Contains("more than 1 material subsets", exception.Message);
    }

    [Fact]
    public void RejectsInvalidUtf8InsteadOfReplacingBytes()
    {
        byte[] valid = Encoding.ASCII.GetBytes("v 0 0 0\nv 1 0 0\nv 0 1 0\nf 1 2 3\n");
        byte[] invalid = valid.Concat(new byte[] { 0xFF }).ToArray();

        ModelFormatException exception = ParseError(invalid);

        Assert.Contains("not valid UTF-8", exception.Message);
    }

    [Fact]
    public void TruncatedInputFailsPredictably()
    {
        byte[] data = Encoding.UTF8.GetBytes(
            "v 0 0 0\nv 1 0 0\nv 0 1 0\nvt 0 0\nf 1/1 2/1 3/1\n");

        for (int length = 0; length < data.Length; length++)
        {
            byte[] truncated = data.AsSpan(0, length).ToArray();

            try
            {
                ParseBytes(truncated);
            }
            catch (ModelFormatException)
            {
                // Expected for at least the prefixes that cut a token or face.
            }
        }
    }

    [Fact]
    public void RandomByteMutationsFailPredictably()
    {
        byte[] seed = Encoding.UTF8.GetBytes(
            "o mutated\nv 0 0 0\nv 1 0 0\nv 0 1 0\nvt 0 0\nvn 0 0 1\n" +
            "usemtl paint\nf 1/1/1 2/1/1 3/1/1\n");
        var random = new Random(0x4E594D);

        for (int iteration = 0; iteration < 200; iteration++)
        {
            byte[] mutated = (byte[])seed.Clone();
            int changes = random.Next(1, 4);

            for (int change = 0; change < changes; change++)
            {
                mutated[random.Next(mutated.Length)] = (byte)random.Next(256);
            }

            try
            {
                ParseBytes(mutated);
            }
            catch (ModelFormatException)
            {
                // Malformed mutations are expected to fail through the parser's
                // documented exception rather than an implementation exception.
            }
        }
    }

    private static ModelData Parse(
        string obj,
        string? sourceName = null,
        ObjImportLimits? limits = null)
    {
        return ParseBytes(Encoding.UTF8.GetBytes(obj), sourceName, limits);
    }

    private static ModelData ParseBytes(
        byte[] data,
        string? sourceName = null,
        ObjImportLimits? limits = null)
    {
        using var stream = new MemoryStream(data);
        return new ObjModelParser(sourceName, limits ?? ObjImportLimits.Default)
            .Parse(stream);
    }

    private static ModelFormatException ParseError(
        string obj,
        string? sourceName = null,
        ObjImportLimits? limits = null)
    {
        return ParseError(Encoding.UTF8.GetBytes(obj), sourceName, limits);
    }

    private static ModelFormatException ParseError(
        byte[] data,
        string? sourceName = null,
        ObjImportLimits? limits = null)
    {
        using var stream = new MemoryStream(data);
        return Assert.Throws<ModelFormatException>(
            () => new ObjModelParser(sourceName, limits ?? ObjImportLimits.Default)
                .Parse(stream));
    }
}
