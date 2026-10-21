using System.Numerics;
using System.Text;
using Nymeria.Core.Models;

namespace Nymeria.Formats.O3d.Tests;

public sealed class O3dModelParserTests
{
    private static readonly TestVertex[] TriangleVertices =
    [
        new(new Vector3(0, 0, 0), new Vector3(0, 0, 1), new Vector2(0, 0)),
        new(new Vector3(1, 0, 0), new Vector3(0, 0, 1), new Vector2(1, 0)),
        new(new Vector3(0, 1, 0), new Vector3(0, 0, 1), new Vector2(0, 1)),
    ];

    [Fact]
    public void ParsesStaticTriangleWithKnownValues()
    {
        byte[] data = new O3dTestFileBuilder(3, 0)
            .Vertices(TriangleVertices)
            .Triangles(new TestTriangle(0, 1, 2))
            .Materials("paint")
            .IdentityMatrix()
            .Build();

        ModelData model = Parse(data, "triangle.o3d");

        Assert.Equal("triangle.o3d", model.Name);
        MeshData mesh = Assert.Single(model.Meshes);
        Assert.Equal(TriangleVertices.Select(v => v.Position).ToArray(), mesh.Positions.ToArray());
        Assert.Equal(
            new[]
            {
                new Vector3(0, 0, -1),
                new Vector3(0, 0, -1),
                new Vector3(0, 0, -1),
            },
            mesh.Normals.ToArray());
        Assert.Equal(
            TriangleVertices.Select(v => v.TextureCoordinate).ToArray(),
            mesh.TextureCoordinates.ToArray());

        // O3D's Direct3D frame is left-handed. Negating Z reflects the mesh, so
        // the winding is reversed to keep counter-clockwise front faces.
        Assert.Equal(new uint[] { 0, 2, 1 }, mesh.Indices.ToArray());

        MeshSubset subset = Assert.Single(mesh.Subsets);
        Assert.Equal(new MeshSubset(0, 3, "paint"), subset);
    }

    [Theory]
    [InlineData((byte)1, (byte)0)]
    [InlineData((byte)2, (byte)0)]
    [InlineData((byte)3, (byte)0)]
    [InlineData((byte)3, (byte)1)]
    [InlineData((byte)3, (byte)2)]
    [InlineData((byte)3, (byte)3)]
    [InlineData((byte)4, (byte)0)]
    [InlineData((byte)4, (byte)1)]
    [InlineData((byte)4, (byte)2)]
    [InlineData((byte)4, (byte)3)]
    [InlineData((byte)5, (byte)0)]
    [InlineData((byte)5, (byte)1)]
    [InlineData((byte)5, (byte)2)]
    [InlineData((byte)5, (byte)3)]
    [InlineData((byte)6, (byte)0)]
    [InlineData((byte)6, (byte)1)]
    [InlineData((byte)6, (byte)2)]
    [InlineData((byte)6, (byte)3)]
    [InlineData((byte)7, (byte)0)]
    [InlineData((byte)7, (byte)1)]
    [InlineData((byte)7, (byte)2)]
    [InlineData((byte)7, (byte)3)]
    public void ParsesEverySupportedVersionAndFlagCombination(
        byte version,
        byte flags)
    {
        byte[] data = new O3dTestFileBuilder(version, flags)
            .Vertices(TriangleVertices)
            .Triangles(new TestTriangle(0, 1, 2))
            .Materials("paint")
            .IdentityMatrix()
            .Build();

        MeshData mesh = Assert.Single(Parse(data).Meshes);

        Assert.Equal(3, mesh.Positions.Length);
        Assert.Equal(new uint[] { 0, 2, 1 }, mesh.Indices.ToArray());
        Assert.Equal("paint", Assert.Single(mesh.Subsets).MaterialName);
    }

    [Fact]
    public void ReadsWideTriangleIndicesWhenFlagZeroIsSet()
    {
        byte[] data = new O3dTestFileBuilder(3, 1)
            .Vertices(TriangleVertices)
            .Triangles(new TestTriangle(0, 1, 2))
            .Build();

        MeshData mesh = Assert.Single(Parse(data).Meshes);

        Assert.Equal(new uint[] { 0, 2, 1 }, mesh.Indices.ToArray());
    }

    [Fact]
    public void GroupsConsecutiveTrianglesByMaterial()
    {
        byte[] data = new O3dTestFileBuilder(3, 0)
            .Vertices(TriangleVertices)
            .Triangles(
                new TestTriangle(0, 1, 2, 0),
                new TestTriangle(0, 1, 2, 0),
                new TestTriangle(0, 1, 2, 1),
                new TestTriangle(0, 1, 2, 1))
            .Materials("first", "second")
            .Build();

        MeshData mesh = Assert.Single(Parse(data).Meshes);

        Assert.Equal(2, mesh.Subsets.Count);
        Assert.Equal(new MeshSubset(0, 6, "first"), mesh.Subsets[0]);
        Assert.Equal(new MeshSubset(6, 6, "second"), mesh.Subsets[1]);
    }

    [Fact]
    public void SplitsSubsetsWhenTheMaterialChangesBack()
    {
        byte[] data = new O3dTestFileBuilder(3, 0)
            .Vertices(TriangleVertices)
            .Triangles(
                new TestTriangle(0, 1, 2, 0),
                new TestTriangle(0, 1, 2, 1),
                new TestTriangle(0, 1, 2, 0))
            .Materials("first", "second")
            .Build();

        MeshData mesh = Assert.Single(Parse(data).Meshes);

        Assert.Equal(3, mesh.Subsets.Count);
        Assert.Equal(new MeshSubset(0, 3, "first"), mesh.Subsets[0]);
        Assert.Equal(new MeshSubset(3, 3, "second"), mesh.Subsets[1]);
        Assert.Equal(new MeshSubset(6, 3, "first"), mesh.Subsets[2]);
    }

    [Fact]
    public void DecodesWindows1252MaterialNames()
    {
        byte[] data = new O3dTestFileBuilder(3, 0)
            .Vertices(TriangleVertices)
            .Triangles(new TestTriangle(0, 1, 2))
            .MaterialBytes([0x54, 0xFC, 0x72, 0x2E, 0x78])
            .Build();

        MeshData mesh = Assert.Single(Parse(data).Meshes);

        Assert.Equal("Tür.x", Assert.Single(mesh.Subsets).MaterialName);
    }

    [Fact]
    public void TreatsAnEmptyMaterialNameAsNoLabel()
    {
        byte[] data = new O3dTestFileBuilder(3, 0)
            .Vertices(TriangleVertices)
            .Triangles(new TestTriangle(0, 1, 2))
            .Materials("")
            .Build();

        MeshData mesh = Assert.Single(Parse(data).Meshes);

        Assert.Null(Assert.Single(mesh.Subsets).MaterialName);
    }

    [Fact]
    public void IgnoresThePivotMatrixForGeometry()
    {
        byte[] data = new O3dTestFileBuilder(3, 0)
            .Vertices(
                new TestVertex(
                    new Vector3(1, 2, 3),
                    new Vector3(0, 0, 1),
                    new Vector2(0.25f, 0.75f)),
                new TestVertex(
                    new Vector3(4, 5, 6),
                    new Vector3(0, 1, 0),
                    new Vector2(0, 1)),
                new TestVertex(
                    new Vector3(7, 8, 9),
                    new Vector3(1, 0, 0),
                    new Vector2(1, 0)))
            .Triangles(new TestTriangle(0, 1, 2))
            .Matrix(
                1, 0, 0, 100,
                0, 1, 0, 200,
                0, 0, 1, 300,
                0, 0, 0, 1)
            .Build();

        MeshData mesh = Assert.Single(Parse(data).Meshes);

        Assert.Equal(new Vector3(1, 2, -3), mesh.Positions.Span[0]);
        Assert.Equal(new Vector3(4, 5, -6), mesh.Positions.Span[1]);
        Assert.Equal(new Vector3(7, 8, -9), mesh.Positions.Span[2]);
        Assert.Equal(new Vector3(0, 0, -1), mesh.Normals.Span[0]);
        Assert.Equal(new Vector3(0, 1, 0), mesh.Normals.Span[1]);
        Assert.Equal(new Vector3(1, 0, 0), mesh.Normals.Span[2]);
        Assert.Equal(new Vector2(0.25f, 0.75f), mesh.TextureCoordinates.Span[0]);
    }

    [Fact]
    public void AcceptsSectionsInAnyOrder()
    {
        byte[] data = new O3dTestFileBuilder(3, 0)
            .Triangles(new TestTriangle(0, 1, 2))
            .Vertices(TriangleVertices)
            .Materials("paint")
            .Build();

        MeshData mesh = Assert.Single(Parse(data).Meshes);

        Assert.Equal(new uint[] { 0, 2, 1 }, mesh.Indices.ToArray());
        Assert.Equal("paint", Assert.Single(mesh.Subsets).MaterialName);
    }

    [Fact]
    public void AcceptsAnEmptyMaterialsSectionWhenTrianglesUseMaterialZero()
    {
        byte[] data = new O3dTestFileBuilder(3, 0)
            .Vertices(TriangleVertices)
            .Triangles(new TestTriangle(0, 1, 2, 0))
            .Materials()
            .Build();

        MeshData mesh = Assert.Single(Parse(data).Meshes);

        Assert.Null(Assert.Single(mesh.Subsets).MaterialName);
    }

    [Fact]
    public void RejectsWrongSignature()
    {
        ModelFormatException exception = ParseError([0x84, 0x18, 3, 0]);

        Assert.Contains("signature", exception.Message);
        Assert.Contains("84 18", exception.Message);
    }

    [Theory]
    [InlineData(new byte[] { 0x84 })]
    [InlineData(new byte[] { 0x84, 0x19 })]
    [InlineData(new byte[] { 0x84, 0x19, 3 })]
    [InlineData(new byte[] { 0x84, 0x19, 4, 0, 0xFF, 0xFF })]
    public void RejectsTruncatedHeader(byte[] data)
    {
        ModelFormatException exception = ParseError(data);

        Assert.Contains("unexpected end of file", exception.Message);
    }

    [Theory]
    [InlineData((byte)0)]
    [InlineData((byte)8)]
    [InlineData((byte)255)]
    public void RejectsUnsupportedVersions(byte version)
    {
        byte[] data = new O3dTestFileBuilder(version).Build();
        ModelFormatException exception = ParseError(data);

        Assert.Contains($"version {version}", exception.Message);
        Assert.Contains("not supported", exception.Message);
    }

    [Theory]
    [InlineData((byte)0x04)]
    [InlineData((byte)0x08)]
    [InlineData((byte)0x80)]
    [InlineData((byte)0xFF)]
    public void RejectsUnknownFlagBits(byte flags)
    {
        byte[] data = new O3dTestFileBuilder(3, flags).Build();
        ModelFormatException exception = ParseError(data);

        Assert.Contains("flags", exception.Message);
        Assert.Contains("unsupported bits", exception.Message);
    }

    [Theory]
    [InlineData((uint)0)]
    [InlineData((uint)1)]
    [InlineData((uint)12345)]
    [InlineData((uint)0xDEADBEEF)]
    public void RejectsScrambledVertexKeys(uint key)
    {
        byte[] data = new O3dTestFileBuilder(5, 0, key).Build();
        ModelFormatException exception = ParseError(data);

        Assert.Contains("scrambled", exception.Message);
        Assert.Contains($"0x{key:X8}", exception.Message);
    }

    [Fact]
    public void RejectsBonesSection()
    {
        byte[] data = new O3dTestFileBuilder(3, 0)
            .Vertices(TriangleVertices)
            .Triangles(new TestTriangle(0, 1, 2))
            .Materials("paint")
            .BonesSection()
            .Build();

        ModelFormatException exception = ParseError(data);

        Assert.Contains("bones", exception.Message);
        Assert.Contains("not supported", exception.Message);
    }

    [Fact]
    public void RejectsUnknownSectionTags()
    {
        byte[] data = new O3dTestFileBuilder(3, 0)
            .Vertices(TriangleVertices)
            .Triangles(new TestTriangle(0, 1, 2))
            .U8(0x33)
            .Build();

        ModelFormatException exception = ParseError(data);

        Assert.Contains("section tag", exception.Message);
        Assert.Contains("0x33", exception.Message);
    }

    [Fact]
    public void RejectsDuplicateVerticesSections()
    {
        byte[] data = new O3dTestFileBuilder(3, 0)
            .Vertices(TriangleVertices)
            .Vertices(TriangleVertices)
            .Build();

        Assert.Contains("more than one vertices", ParseError(data).Message);
    }

    [Fact]
    public void RejectsDuplicateTrianglesSections()
    {
        byte[] data = new O3dTestFileBuilder(3, 0)
            .Vertices(TriangleVertices)
            .Triangles(new TestTriangle(0, 1, 2))
            .Triangles(new TestTriangle(0, 1, 2))
            .Build();

        Assert.Contains("more than one triangles", ParseError(data).Message);
    }

    [Fact]
    public void RejectsDuplicateMaterialsSections()
    {
        byte[] data = new O3dTestFileBuilder(3, 0)
            .Vertices(TriangleVertices)
            .Triangles(new TestTriangle(0, 1, 2))
            .Materials("one")
            .Materials("two")
            .Build();

        Assert.Contains("more than one materials", ParseError(data).Message);
    }

    [Fact]
    public void RejectsDuplicateMatrixSections()
    {
        byte[] data = new O3dTestFileBuilder(3, 0)
            .Vertices(TriangleVertices)
            .Triangles(new TestTriangle(0, 1, 2))
            .IdentityMatrix()
            .IdentityMatrix()
            .Build();

        Assert.Contains("more than one pivot-matrix", ParseError(data).Message);
    }

    [Fact]
    public void RejectsMissingVerticesSection()
    {
        byte[] data = new O3dTestFileBuilder(3, 0).Build();

        Assert.Contains("no vertices section", ParseError(data).Message);
    }

    [Fact]
    public void RejectsMissingTrianglesSection()
    {
        byte[] data = new O3dTestFileBuilder(3, 0)
            .Vertices(TriangleVertices)
            .Build();

        Assert.Contains("no triangles section", ParseError(data).Message);
    }

    [Fact]
    public void RejectsEmptyVerticesSection()
    {
        byte[] data = new O3dTestFileBuilder(3, 0)
            .Vertices()
            .Triangles(new TestTriangle(0, 1, 2))
            .Build();

        Assert.Contains("vertices section is empty", ParseError(data).Message);
    }

    [Fact]
    public void RejectsEmptyTrianglesSection()
    {
        byte[] data = new O3dTestFileBuilder(3, 0)
            .Vertices(TriangleVertices)
            .Triangles()
            .Build();

        Assert.Contains("triangles section is empty", ParseError(data).Message);
    }

    [Fact]
    public void RejectsOversizedVertexCountBeforeAllocating()
    {
        byte[] data = new O3dTestFileBuilder(3, 0)
            .U8(0x17)
            .U32(uint.MaxValue)
            .Build();

        ModelFormatException exception = ParseError(data);

        Assert.Contains("vertex count", exception.Message);
        Assert.Contains("exceeds", exception.Message);
    }

    [Fact]
    public void RejectsOversizedTriangleCountBeforeAllocating()
    {
        byte[] data = new O3dTestFileBuilder(3, 0)
            .U8(0x49)
            .U32(uint.MaxValue)
            .Build();

        ModelFormatException exception = ParseError(data);

        Assert.Contains("triangle count", exception.Message);
        Assert.Contains("exceeds", exception.Message);
    }

    [Fact]
    public void RejectsVertexCountBeyondTheFile()
    {
        byte[] data = new O3dTestFileBuilder(3, 0)
            .U8(0x17)
            .U32(1000)
            .Vertex(TriangleVertices[0])
            .Build();

        ModelFormatException exception = ParseError(data);

        Assert.Contains("unexpected end of file", exception.Message);
        Assert.Contains("vertex records", exception.Message);
    }

    [Fact]
    public void RejectsTriangleCountBeyondTheFile()
    {
        byte[] data = new O3dTestFileBuilder(3, 0)
            .U8(0x49)
            .U32(1000)
            .U16(0)
            .U16(1)
            .U16(2)
            .U16(0)
            .Build();

        ModelFormatException exception = ParseError(data);

        Assert.Contains("unexpected end of file", exception.Message);
        Assert.Contains("triangle records", exception.Message);
    }

    [Fact]
    public void RejectsTruncatedVertexRecords()
    {
        byte[] data = new O3dTestFileBuilder(3, 0)
            .U8(0x17)
            .U32(3)
            .Vertex(TriangleVertices[0])
            .Vertex(TriangleVertices[1])
            .Build();

        ModelFormatException exception = ParseError(data);

        Assert.Contains("unexpected end of file", exception.Message);
        Assert.Contains("vertex records", exception.Message);
    }

    [Fact]
    public void RejectsTruncatedTriangleRecords()
    {
        byte[] data = new O3dTestFileBuilder(3, 0)
            .U8(0x49)
            .U32(2)
            .U16(0)
            .U16(1)
            .U16(2)
            .U16(0)
            .Build();

        ModelFormatException exception = ParseError(data);

        Assert.Contains("unexpected end of file", exception.Message);
        Assert.Contains("triangle records", exception.Message);
    }

    [Fact]
    public void RejectsTruncatedMaterialRecords()
    {
        byte[] data = new O3dTestFileBuilder(3, 0)
            .U8(0x26)
            .U16(1)
            .Build();

        ModelFormatException exception = ParseError(data);

        Assert.Contains("unexpected end of file", exception.Message);
        Assert.Contains("material records", exception.Message);
    }

    [Fact]
    public void RejectsTruncatedMaterialNames()
    {
        byte[] data = new O3dTestFileBuilder(3, 0)
            .U8(0x26)
            .U16(1)
            .MaterialBodyPrefix()
            .U8(10)
            .Raw(0x41, 0x42)
            .Build();

        ModelFormatException exception = ParseError(data);

        Assert.Contains("unexpected end of file", exception.Message);
        Assert.Contains("material name", exception.Message);
    }

    [Fact]
    public void RejectsTruncatedPivotMatrix()
    {
        byte[] data = new O3dTestFileBuilder(3, 0)
            .U8(0x79)
            .F32(1)
            .F32(0)
            .F32(0)
            .F32(0)
            .Build();

        ModelFormatException exception = ParseError(data);

        Assert.Contains("unexpected end of file", exception.Message);
        Assert.Contains("pivot matrix", exception.Message);
    }

    [Fact]
    public void RejectsInputLargerThanTheSupportedLimit()
    {
        using var stream = new OversizedLengthStream(O3dModelParser.MaxInputBytes + 1L);

        ModelFormatException exception = Assert.Throws<ModelFormatException>(
            () => new O3dModelParser().Parse(stream));

        Assert.Contains("larger than the supported limit", exception.Message);
        Assert.Contains($"{O3dModelParser.MaxInputBytes} bytes", exception.Message);
    }

    [Fact]
    public void RejectsMoreThanTheSupportedNumberOfSubsets()
    {
        int triangleCount = O3dModelParser.MaxSubsetCount + 1;
        var triangles = new TestTriangle[triangleCount];

        for (int i = 0; i < triangles.Length; i++)
        {
            triangles[i] = new TestTriangle(0, 1, 2, (ushort)(i % 2));
        }

        byte[] data = new O3dTestFileBuilder(3, 0)
            .Vertices(TriangleVertices)
            .Triangles(triangles)
            .Materials("first", "second")
            .Build();

        ModelFormatException exception = ParseError(data);

        Assert.Contains("material subsets", exception.Message);
        Assert.Contains($"{O3dModelParser.MaxSubsetCount}", exception.Message);
    }

    [Fact]
    public void RejectsOutOfRangeTriangleIndices()
    {
        byte[] data = new O3dTestFileBuilder(3, 0)
            .Vertices(TriangleVertices)
            .Triangles(new TestTriangle(0, 1, 3))
            .Build();

        ModelFormatException exception = ParseError(data);

        Assert.Contains("triangle 0", exception.Message);
        Assert.Contains("references vertex 3", exception.Message);
    }

    [Fact]
    public void RejectsOutOfRangeWideTriangleIndices()
    {
        byte[] data = new O3dTestFileBuilder(3, 1)
            .Vertices(TriangleVertices)
            .Triangles(new TestTriangle(0, 1, uint.MaxValue))
            .Build();

        ModelFormatException exception = ParseError(data);

        Assert.Contains("triangle 0", exception.Message);
        Assert.Contains($"references vertex {uint.MaxValue}", exception.Message);
    }

    [Fact]
    public void RejectsMaterialIndexWithoutMaterials()
    {
        byte[] data = new O3dTestFileBuilder(3, 0)
            .Vertices(TriangleVertices)
            .Triangles(new TestTriangle(0, 1, 2, 1))
            .Build();

        ModelFormatException exception = ParseError(data);

        Assert.Contains("references material 1", exception.Message);
        Assert.Contains("no materials section", exception.Message);
    }

    [Fact]
    public void RejectsOutOfRangeMaterialIndex()
    {
        byte[] data = new O3dTestFileBuilder(3, 0)
            .Vertices(TriangleVertices)
            .Triangles(new TestTriangle(0, 1, 2, 2))
            .Materials("only")
            .Build();

        ModelFormatException exception = ParseError(data);

        Assert.Contains("references material 2", exception.Message);
        Assert.Contains("only 1 material", exception.Message);
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    public void RejectsNonFiniteVertexData(float value)
    {
        byte[] positionData = new O3dTestFileBuilder(3, 0)
            .Vertices(
                new TestVertex(new Vector3(value, 0, 0), Vector3.UnitY, Vector2.Zero),
                TriangleVertices[1],
                TriangleVertices[2])
            .Triangles(new TestTriangle(0, 1, 2))
            .Build();

        Assert.Contains("non-finite position", ParseError(positionData).Message);

        byte[] normalData = new O3dTestFileBuilder(3, 0)
            .Vertices(
                new TestVertex(Vector3.Zero, new Vector3(value, 0, 0), Vector2.Zero),
                TriangleVertices[1],
                TriangleVertices[2])
            .Triangles(new TestTriangle(0, 1, 2))
            .Build();

        Assert.Contains("non-finite normal", ParseError(normalData).Message);

        byte[] uvData = new O3dTestFileBuilder(3, 0)
            .Vertices(
                new TestVertex(Vector3.Zero, Vector3.UnitY, new Vector2(value, 0)),
                TriangleVertices[1],
                TriangleVertices[2])
            .Triangles(new TestTriangle(0, 1, 2))
            .Build();

        Assert.Contains("non-finite texture coordinate", ParseError(uvData).Message);
    }

    [Fact]
    public void RejectsNonFinitePivotMatrix()
    {
        byte[] data = new O3dTestFileBuilder(3, 0)
            .Vertices(TriangleVertices)
            .Triangles(new TestTriangle(0, 1, 2))
            .Matrix(
                1, 0, 0, 0,
                0, 1, 0, 0,
                0, 0, 1, 0,
                0, 0, float.NaN, 1)
            .Build();

        Assert.Contains("pivot matrix", ParseError(data).Message);
        Assert.Contains("non-finite", ParseError(data).Message);
    }

    [Fact]
    public void IncludesSourceNameAndOffsetInDiagnostics()
    {
        byte[] data = new O3dTestFileBuilder(3, 0).Build();
        ModelFormatException exception = ParseError(data, "broken.o3d");

        Assert.Contains("broken.o3d", exception.Message);
        Assert.Contains("offset 0x", exception.Message);
        Assert.Contains("no vertices section", exception.Message);
    }

    [Fact]
    public void ParserDoesNotDisposeTheStreamOnSuccess()
    {
        using var stream = new MemoryStream(
            new O3dTestFileBuilder(3, 0)
                .Vertices(TriangleVertices)
                .Triangles(new TestTriangle(0, 1, 2))
                .Build());

        ModelData model = new O3dModelParser().Parse(stream);

        Assert.Single(model.Meshes);
        Assert.True(stream.CanRead);
    }

    [Fact]
    public void ParserDoesNotDisposeTheStreamOnFailure()
    {
        using var stream = new MemoryStream([0x84, 0x18]);

        Assert.Throws<ModelFormatException>(
            () => new O3dModelParser().Parse(stream));
        Assert.True(stream.CanRead);
    }

    [Fact]
    public void ParserReadsFromTheStreamsCurrentPosition()
    {
        byte[] prefix = Encoding.ASCII.GetBytes("ignored by the test");
        byte[] body = new O3dTestFileBuilder(3, 0)
            .Vertices(TriangleVertices)
            .Triangles(new TestTriangle(0, 1, 2))
            .Build();

        using var stream = new MemoryStream(prefix.Concat(body).ToArray());
        stream.Position = prefix.Length;

        ModelData model = new O3dModelParser().Parse(stream);

        Assert.Equal(3, Assert.Single(model.Meshes).Positions.Length);
    }

    [Fact]
    public void ParserAcceptsANonSeekableStream()
    {
        byte[] data = new O3dTestFileBuilder(3, 0)
            .Vertices(TriangleVertices)
            .Triangles(new TestTriangle(0, 1, 2))
            .Build();

        using var inner = new MemoryStream(data);
        using var stream = new NonSeekableReadStream(inner);

        ModelData model = new O3dModelParser().Parse(stream);

        Assert.Equal(3, Assert.Single(model.Meshes).Positions.Length);
    }

    [Fact]
    public void ParserRejectsNullStream()
    {
        Assert.Throws<ArgumentNullException>(
            () => new O3dModelParser().Parse(null!));
    }

    [Fact]
    public void ParserRejectsANonReadableStream()
    {
        using var stream = new MemoryStream();
        stream.Dispose();

        Assert.Throws<ArgumentException>(
            () => new O3dModelParser().Parse(stream));
    }

    [Fact]
    public void UsesO3dNameWhenTheSourceNameIsMissing()
    {
        byte[] data = new O3dTestFileBuilder(3, 0)
            .Vertices(TriangleVertices)
            .Triangles(new TestTriangle(0, 1, 2))
            .Build();

        Assert.Equal("O3D", Parse(data).Name);
    }

    [Fact]
    public void ParsesCheckedInHelloO3dFixture()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Assets", "cube.o3d");

        using FileStream stream = File.OpenRead(path);
        ModelData model = new O3dModelParser(Path.GetFileName(path)).Parse(stream);

        Assert.Equal("cube.o3d", model.Name);

        MeshData mesh = Assert.Single(model.Meshes);
        Assert.Equal(24, mesh.Positions.Length);
        Assert.Equal(24, mesh.Normals.Length);
        Assert.Equal(24, mesh.TextureCoordinates.Length);
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
    public void CheckedInFixtureMatchesTheSyntheticCube()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Assets", "cube.o3d");

        using FileStream stream = File.OpenRead(path);
        MeshData fixture = Assert.Single(
            new O3dModelParser(Path.GetFileName(path)).Parse(stream).Meshes);
        MeshData synthetic = Assert.Single(Parse(BuildSyntheticCube()).Meshes);

        Assert.Equal(synthetic.Positions.ToArray(), fixture.Positions.ToArray());
        Assert.Equal(synthetic.Normals.ToArray(), fixture.Normals.ToArray());
        Assert.Equal(
            synthetic.TextureCoordinates.ToArray(),
            fixture.TextureCoordinates.ToArray());
        Assert.Equal(synthetic.Indices.ToArray(), fixture.Indices.ToArray());
        Assert.Equal(synthetic.Subsets.ToArray(), fixture.Subsets.ToArray());
    }

    private static byte[] BuildSyntheticCube()
    {
        // Original Nymeria test data: the same 2 x 2 x 2 cube as the HelloOBJ
        // fixture, expressed in O3D's frame and winding. The four corners of each
        // face follow the OBJ fixture's order.
        (Vector3 Normal, Vector3[] Corners)[] faces =
        [
            (new Vector3(0, 0, 1), [new(-1, -1, 1), new(1, -1, 1), new(1, 1, 1), new(-1, 1, 1)]),
            (new Vector3(0, 0, -1), [new(1, -1, -1), new(-1, -1, -1), new(-1, 1, -1), new(1, 1, -1)]),
            (new Vector3(1, 0, 0), [new(1, -1, 1), new(1, -1, -1), new(1, 1, -1), new(1, 1, 1)]),
            (new Vector3(-1, 0, 0), [new(-1, -1, -1), new(-1, -1, 1), new(-1, 1, 1), new(-1, 1, -1)]),
            (new Vector3(0, 1, 0), [new(-1, 1, 1), new(1, 1, 1), new(1, 1, -1), new(-1, 1, -1)]),
            (new Vector3(0, -1, 0), [new(-1, -1, -1), new(1, -1, -1), new(1, -1, 1), new(-1, -1, 1)]),
        ];
        Vector2[] cornerUvs =
        [
            new(0, 0),
            new(1, 0),
            new(1, 1),
            new(0, 1),
        ];
        string[] names = ["front", "back", "right", "left", "top", "bottom"];

        var vertices = new List<TestVertex>();
        var triangles = new List<TestTriangle>();

        for (int face = 0; face < faces.Length; face++)
        {
            (Vector3 normal, Vector3[] corners) = faces[face];
            int first = vertices.Count;

            for (int corner = 0; corner < 4; corner++)
            {
                vertices.Add(ToO3d(corners[corner], normal, cornerUvs[corner]));
            }

            triangles.Add(new TestTriangle(
                (uint)first,
                (uint)first + 2,
                (uint)first + 1,
                (ushort)face));
            triangles.Add(new TestTriangle(
                (uint)first,
                (uint)first + 3,
                (uint)first + 2,
                (ushort)face));
        }

        return new O3dTestFileBuilder(3, 0)
            .Vertices(vertices.ToArray())
            .Triangles(triangles.ToArray())
            .Materials(names)
            .IdentityMatrix()
            .Build();
    }

    private static TestVertex ToO3d(
        Vector3 position,
        Vector3 normal,
        Vector2 textureCoordinate)
    {
        return new TestVertex(
            new Vector3(position.X, position.Y, -position.Z),
            new Vector3(normal.X, normal.Y, -normal.Z),
            textureCoordinate);
    }

    private static ModelData Parse(byte[] data, string? sourceName = null)
    {
        using var stream = new MemoryStream(data);
        return new O3dModelParser(sourceName).Parse(stream);
    }

    private static ModelFormatException ParseError(
        byte[] data,
        string? sourceName = null)
    {
        using var stream = new MemoryStream(data);
        return Assert.Throws<ModelFormatException>(
            () => new O3dModelParser(sourceName).Parse(stream));
    }

    private sealed class OversizedLengthStream(long length) : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => true;

        public override bool CanWrite => false;

        public override long Length => length;

        public override long Position { get; set; }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return 0;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            Position = origin switch
            {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => Position + offset,
                SeekOrigin.End => Length + offset,
                _ => throw new ArgumentOutOfRangeException(nameof(origin)),
            };

            return Position;
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class NonSeekableReadStream(Stream inner) : Stream
    {
        public override bool CanRead => inner.CanRead;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return inner.Read(buffer, offset, count);
        }

        public override int Read(Span<byte> buffer)
        {
            return inner.Read(buffer);
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }
    }
}
