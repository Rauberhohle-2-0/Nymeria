using System.Numerics;
using System.Text;
using Nymeria.Core.Models;
using Nymeria.Formats.DirectX;

namespace Nymeria.Formats.DirectX.Tests;

public sealed class XModelParserTests
{
    private const string TriMesh = """
        Mesh Tri {
          3;
          0;0;0;,
          1;0;0;,
          0;1;0;;
          1;
          3;0,1,2;;
        }
        """;

    private const string QuadMesh = """
        Mesh Quad {
          4;
          0;0;0;,
          1;0;0;,
          1;1;0;,
          0;1;0;;
          1;
          4;0,1,2,3;;
        }
        """;

    [Fact]
    public void ParsesTrianglePositionsAndWinding()
    {
        ModelData model = Parse(Doc(TriMesh));

        Assert.Equal("Tri", model.Name);
        MeshData mesh = Assert.Single(model.Meshes);
        Assert.Equal(
            new[] { Vector3.Zero, new Vector3(1, 0, 0), new Vector3(0, 1, 0) },
            mesh.Positions.ToArray());
        Assert.True(mesh.Normals.IsEmpty);
        Assert.True(mesh.TextureCoordinates.IsEmpty);
        Assert.Equal(new uint[] { 0, 2, 1 }, mesh.Indices.ToArray());

        MeshSubset subset = Assert.Single(mesh.Subsets);
        Assert.Equal(0, subset.IndexOffset);
        Assert.Equal(3, subset.IndexCount);
        Assert.Null(subset.MaterialName);
    }

    [Fact]
    public void ConvertsLeftHandedZToTheNymeriaFrame()
    {
        ModelData model = Parse(Doc("""
            Mesh Tri {
              3;
              0;0;1;,
              1;0;-2;,
              0;1;3;;
              1;
              3;0,1,2;;
            }
            """));

        MeshData mesh = Assert.Single(model.Meshes);
        Assert.Equal(
            new[]
            {
                new Vector3(0, 0, -1),
                new Vector3(1, 0, 2),
                new Vector3(0, 1, -3),
            },
            mesh.Positions.ToArray());
    }

    [Fact]
    public void FanTriangulatesPolygonsAndPreservesOrder()
    {
        ModelData model = Parse(Doc(QuadMesh));

        MeshData mesh = Assert.Single(model.Meshes);
        Assert.Equal(4, mesh.Positions.Length);
        Assert.Equal(
            new uint[] { 0, 2, 1, 0, 3, 2 },
            mesh.Indices.ToArray());
    }

    [Fact]
    public void ReadsNormalsAndTextureCoordinatesWithoutFlippingV()
    {
        ModelData model = Parse(Doc("""
            Mesh Tri {
              3;
              0;0;0;,
              1;0;0;,
              0;1;0;;
              1;
              3;0,1,2;;
              MeshNormals {
                3;
                0;0;1;,
                0;0;2;,
                0;0;3;;
                1;
                3;0,1,2;;
              }
              MeshTextureCoords {
                3;
                0;0;,
                1;0;,
                0.25;0.75;;
              }
            }
            """));

        MeshData mesh = Assert.Single(model.Meshes);
        Assert.Equal(
            new[]
            {
                new Vector3(0, 0, -1),
                new Vector3(0, 0, -1),
                new Vector3(0, 0, -1),
            },
            mesh.Normals.ToArray());
        Assert.Equal(
            new[]
            {
                new Vector2(0, 0),
                new Vector2(1, 0),
                new Vector2(0.25f, 0.75f),
            },
            mesh.TextureCoordinates.ToArray());
    }

    [Fact]
    public void AllowsIndependentNormalIndices()
    {
        ModelData model = Parse(Doc("""
            Mesh Tri {
              3;
              0;0;0;,
              1;0;0;,
              0;1;0;;
              1;
              3;0,1,2;;
              MeshNormals {
                2;
                0;0;1;,
                1;0;0;;
                1;
                3;0,0,1;;
              }
            }
            """));

        MeshData mesh = Assert.Single(model.Meshes);
        Assert.Equal(3, mesh.Positions.Length);
        Assert.Equal(
            new[]
            {
                new Vector3(0, 0, -1),
                new Vector3(0, 0, -1),
                new Vector3(1, 0, 0),
            },
            mesh.Normals.ToArray());
    }

    [Fact]
    public void ReusesCornersThatSharePositionAndNormal()
    {
        ModelData model = Parse(Doc("""
            Mesh Quad {
              4;
              0;0;0;,
              1;0;0;,
              0;1;0;,
              1;1;0;;
              2;
              3;0,1,2;,
              3;0,2,3;;
            }
            """));

        MeshData mesh = Assert.Single(model.Meshes);
        Assert.Equal(4, mesh.Positions.Length);
        Assert.Equal(new uint[] { 0, 2, 1, 0, 3, 2 }, mesh.Indices.ToArray());
    }

    [Fact]
    public void ParsesATopLevelMeshWithoutAFrame()
    {
        ModelData model = Parse(Doc(TriMesh), "standalone.x");

        Assert.Equal("Tri", model.Name);
        Assert.Single(model.Meshes);
    }

    [Fact]
    public void UsesTheSourceNameWhenNoObjectIsNamed()
    {
        ModelData model = Parse(
            Doc("""
                Mesh {
                  3;
                  0;0;0;,
                  1;0;0;,
                  0;1;0;;
                  1;
                  3;0,1,2;;
                }
                """),
            "unnamed.x");

        Assert.Equal("unnamed.x", model.Name);
    }

    [Fact]
    public void BakesTheFrameTransformIntoPositions()
    {
        ModelData model = Parse(Doc("""
            Frame Root {
              FrameTransformMatrix {
                1,0,0,0,
                0,1,0,0,
                0,0,1,0,
                1,2,3,1;;
              }
              Mesh Tri {
                1;
                0;0;0;;
                1;
                3;0,0,0;;
              }
            }
            """));

        MeshData mesh = Assert.Single(model.Meshes);
        Assert.Equal(new Vector3(1, 2, -3), mesh.Positions.Span[0]);
    }

    [Fact]
    public void BakesNestedFrameTransformsInParentOrder()
    {
        ModelData model = Parse(Doc("""
            Frame Root {
              FrameTransformMatrix {
                1,0,0,0,
                0,1,0,0,
                0,0,1,0,
                1,0,0,1;;
              }
              Frame Child {
                FrameTransformMatrix {
                  1,0,0,0,
                  0,1,0,0,
                  0,0,1,0,
                  0,2,0,1;;
                }
                Mesh Tri {
                  1;
                  0;0;0;;
                  1;
                  3;0,0,0;;
                }
              }
            }
            """));

        MeshData mesh = Assert.Single(model.Meshes);
        Assert.Equal(new Vector3(1, 2, 0), mesh.Positions.Span[0]);
    }

    [Fact]
    public void TransformsNormalsWithTheInverseTranspose()
    {
        ModelData model = Parse(Doc("""
            Frame Root {
              FrameTransformMatrix {
                2,0,0,0,
                0,1,0,0,
                0,0,1,0,
                0,0,0,1;;
              }
              Mesh Tri {
                3;
                0;0;0;,
                1;0;0;,
                0;1;0;;
                1;
                3;0,1,2;;
                MeshNormals {
                  3;
                  1;1;0;,
                  1;1;0;,
                  1;1;0;;
                  1;
                  3;0,1,2;;
                }
              }
            }
            """));

        MeshData mesh = Assert.Single(model.Meshes);
        Vector3 expected = Vector3.Normalize(new Vector3(0.5f, 1.0f, 0.0f));
        Assert.Equal(expected.X, mesh.Normals.Span[0].X, 5);
        Assert.Equal(expected.Y, mesh.Normals.Span[0].Y, 5);
        Assert.Equal(0.0f, mesh.Normals.Span[0].Z, 5);
    }

    [Fact]
    public void KeepsSourceWindingWhenTheTransformMirrorsTheMesh()
    {
        ModelData model = Parse(Doc("""
            Frame Root {
              FrameTransformMatrix {
                -1,0,0,0,
                0,1,0,0,
                0,0,1,0,
                0,0,0,1;;
              }
              Mesh Tri {
                3;
                0;0;0;,
                1;0;0;,
                0;1;0;;
                1;
                3;0,1,2;;
              }
            }
            """));

        MeshData mesh = Assert.Single(model.Meshes);
        Assert.Equal(new uint[] { 0, 1, 2 }, mesh.Indices.ToArray());
        Assert.Equal(new Vector3(0, 0, 0), mesh.Positions.Span[0]);
        Assert.Equal(new Vector3(-1, 0, 0), mesh.Positions.Span[1]);
    }

    [Fact]
    public void GroupsFacesByMaterialAndPreservesNames()
    {
        ModelData model = Parse(Doc("""
            Mesh Quad {
              4;
              0;0;0;,
              1;0;0;,
              1;1;0;,
              0;1;0;;
              2;
              4;0,1,2,3;,
              4;0,1,2,3;;
              MeshMaterialList {
                2;
                2;
                0,1;;
                Material red {
                  1;0;0;1;;
                  0;
                  0;0;0;;
                  0;0;0;;
                }
                Material blue {
                  0;0;1;1;;
                  0;
                  0;0;0;;
                  0;0;0;;
                }
              }
            }
            """));

        MeshData mesh = Assert.Single(model.Meshes);
        Assert.Equal(2, mesh.Subsets.Count);
        Assert.Equal(new MeshSubset(0, 6, "red"), mesh.Subsets[0]);
        Assert.Equal(new MeshSubset(6, 6, "blue"), mesh.Subsets[1]);
    }

    [Fact]
    public void MergesConsecutiveFacesWithTheSameMaterial()
    {
        ModelData model = Parse(Doc("""
            Mesh Quad {
              4;
              0;0;0;,
              1;0;0;,
              1;1;0;,
              0;1;0;;
              3;
              4;0,1,2,3;,
              4;0,1,2,3;,
              4;0,1,2,3;;
              MeshMaterialList {
                1;
                3;
                0,0,0;;
                Material only {
                  1;1;1;1;;
                  0;
                  0;0;0;;
                  0;0;0;;
                }
              }
            }
            """));

        MeshSubset subset = Assert.Single(Assert.Single(model.Meshes).Subsets);
        Assert.Equal(0, subset.IndexOffset);
        Assert.Equal(18, subset.IndexCount);
        Assert.Equal("only", subset.MaterialName);
    }

    [Fact]
    public void ValidatesAndIgnoresTextureFilenames()
    {
        ModelData model = Parse(Doc("""
            Mesh Tri {
              3;
              0;0;0;,
              1;0;0;,
              0;1;0;;
              1;
              3;0,1,2;;
              MeshMaterialList {
                1;
                1;
                0;;
                Material textured {
                  1;1;1;1;;
                  0;
                  0;0;0;;
                  0;0;0;;
                  TextureFilename {
                    "../../outside.bmp";
                  }
                }
              }
            }
            """));

        MeshSubset subset = Assert.Single(Assert.Single(model.Meshes).Subsets);
        Assert.Equal("textured", subset.MaterialName);
    }

    [Fact]
    public void ParsesMultipleMeshesIntoDistinctMeshData()
    {
        ModelData model = Parse(Doc(TriMesh + "\n" + QuadMesh));

        Assert.Equal(2, model.Meshes.Count);
        Assert.Equal(3, model.Meshes[0].Positions.Length);
        Assert.Equal(4, model.Meshes[1].Positions.Length);
    }

    [Fact]
    public void SkipsTopLevelTemplateDeclarations()
    {
        ModelData model = Parse(Doc("""
            template Mesh {
              <3D82AB44-62DA-11cf-AB39-0020AF71E433>
              DWORD nVertices;
              array Vector vertices[nVertices];
            }
            """ + "\n" + TriMesh));

        Assert.Single(model.Meshes);
    }

    [Fact]
    public void AcceptsLineCommentsAroundTokens()
    {
        ModelData model = Parse(
            "\uFEFFxof 0303txt 0032 // header comment\n" +
            "// mesh starts\n" +
            "Mesh Tri {\n" +
            "  3; // count\n" +
            "  0;0;0;, 1;0;0;, 0;1;0;;\n" +
            "  1;\n" +
            "  3;0,1,2;;\n" +
            "}\n");

        Assert.Single(model.Meshes);
    }

    [Theory]
    [InlineData("0302", "0032")]
    [InlineData("0303", "0064")]
    [InlineData("0302", "0064")]
    public void AcceptsSupportedHeaderVariants(string version, string precision)
    {
        ModelData model = Parse(
            $"xof {version}txt {precision}\n" + TriMesh);

        Assert.Single(model.Meshes);
    }

    [Fact]
    public void ParsesTheCheckedInCubeFixture()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Assets", "cube.x");

        using FileStream stream = File.OpenRead(path);
        ModelData model = new XModelParser(path).Parse(stream);

        Assert.Equal("Root", model.Name);
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

        Assert.Equal(new Vector3(-1, 1, 1), mesh.Positions.Span[0]);
        Assert.Equal(new Vector3(0, 0, 1), mesh.Normals.Span[0]);
        Assert.Equal(Vector2.Zero, mesh.TextureCoordinates.Span[0]);
        Assert.Equal(new uint[] { 0, 2, 1 }, mesh.Indices.ToArray()[..3]);
    }

    [Theory]
    [InlineData("bin", "Binary .x files")]
    [InlineData("tzip", "Compressed .x files")]
    [InlineData("bzip", "Compressed .x files")]
    public void RejectsBinaryAndCompressedHeaders(string format, string expected)
    {
        byte[] header = Encoding.ASCII.GetBytes($"xof 0303{format} 0032\n");
        byte[] body = [0x00, 0xFF, 0xFE, 0x01];

        ModelFormatException exception = ParseError(header.Concat(body).ToArray());

        Assert.Contains(expected, exception.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a model")]
    [InlineData("xof 0303txt 0032")]
    public void RejectsInputWithoutAMesh(string text)
    {
        ModelFormatException exception = ParseError(text);

        Assert.True(
            exception.Message.Contains("Mesh", StringComparison.Ordinal) ||
            exception.Message.Contains("header", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("xof 0301txt 0032\n", "version")]
    [InlineData("xof 0304txt 0032\n", "version")]
    [InlineData("xof 0303txt 0016\n", "precision")]
    [InlineData("xof 0303bin 0032\n", "Binary")]
    [InlineData("xof 0303foo 0032\n", "format type")]
    public void RejectsUnsupportedHeaders(string header, string expected)
    {
        ModelFormatException exception = ParseError(
            header + TriMesh);

        Assert.Contains(expected, exception.Message);
    }

    [Fact]
    public void RejectsAnEmptyMesh()
    {
        ModelFormatException exception = ParseError(Doc("""
            Mesh Empty {
              0;
              0;
            }
            """));

        Assert.Contains("no vertices", exception.Message);
    }

    [Fact]
    public void RejectsAMeshWithoutFaces()
    {
        ModelFormatException exception = ParseError(Doc("""
            Mesh Empty {
              3;
              0;0;0;,
              1;0;0;,
              0;1;0;;
              0;
            }
            """));

        Assert.Contains("no faces", exception.Message);
    }

    [Fact]
    public void RejectsFacesWithFewerThanThreeVertices()
    {
        ModelFormatException exception = ParseError(Doc("""
            Mesh Tri {
              3;
              0;0;0;,
              1;0;0;,
              0;1;0;;
              1;
              2;0,1;;
            }
            """));

        Assert.Contains("requires at least three", exception.Message);
    }

    [Fact]
    public void RejectsOutOfRangeFaceIndices()
    {
        ModelFormatException exception = ParseError(Doc("""
            Mesh Tri {
              3;
              0;0;0;,
              1;0;0;,
              0;1;0;;
              1;
              3;0,1,3;;
            }
            """));

        Assert.Contains("valid indices are 0 through 2", exception.Message);
    }

    [Fact]
    public void RejectsNegativeCounts()
    {
        ModelFormatException exception = ParseError(Doc("""
            Mesh Tri {
              -1;
            }
            """));

        Assert.Contains("cannot be negative", exception.Message);
    }

    [Theory]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("-Infinity")]
    [InlineData("1e999")]
    [InlineData("nope")]
    public void RejectsNonFiniteOrMalformedNumbers(string value)
    {
        string body = TriMesh.Replace("0;0;0;", value + ";0;0;");

        ModelFormatException exception = ParseError(Doc(body));

        Assert.Contains("finite invariant-culture number", exception.Message);
    }

    [Fact]
    public void RejectsUnclosedFramesAndMeshes()
    {
        Assert.Contains(
            "not closed",
            ParseError("xof 0303txt 0032\nFrame Root {\n").Message);
        string unclosedMesh = Doc(TriMesh[..TriMesh.LastIndexOf('}')]);
        Assert.Contains(
            "not closed",
            ParseError(unclosedMesh).Message);
    }

    [Fact]
    public void RejectsAnUnclosedTemplateDeclaration()
    {
        ModelFormatException exception = ParseError(
            "xof 0303txt 0032\ntemplate Mesh {\n  <3D82AB44-62DA-11cf-AB39-0020AF71E433>\n");

        Assert.Contains("not closed", exception.Message);
    }

    [Fact]
    public void RejectsUnknownTopLevelObjects()
    {
        ModelFormatException exception = ParseError(
            "xof 0303txt 0032\nBogus Thing { }\n");

        Assert.Contains("top-level object 'Bogus'", exception.Message);
    }

    [Fact]
    public void RejectsAnimationAndSkinningExplicitly()
    {
        Assert.Contains(
            "animation data",
            ParseError("xof 0303txt 0032\nAnimationSet { }\n").Message);
        Assert.Contains(
            "animation data",
            ParseError(Doc("""
                Frame Root {
                  Animation { }
                }
                """)).Message);
        Assert.Contains(
            "skinning data",
            ParseError(Doc("""
                Frame Root {
                  Mesh Tri {
                    3;
                    0;0;0;,
                    1;0;0;,
                    0;1;0;;
                    1;
                    3;0,1,2;;
                    SkinWeights { }
                  }
                }
                """)).Message);
    }

    [Theory]
    [InlineData("MeshVertexColors")]
    [InlineData("VertexDuplicationIndices")]
    [InlineData("DeclData")]
    [InlineData("FVFData")]
    public void RejectsUnsupportedMeshElements(string element)
    {
        string body = TriMesh.Replace(
            "  1;\n  3;0,1,2;;\n}",
            $"  1;\n  3;0,1,2;;\n  {element} {{ }}\n}}");

        ModelFormatException exception = ParseError(Doc(body));

        Assert.Contains(element, exception.Message);
        Assert.Contains("not supported", exception.Message);
    }

    [Fact]
    public void RejectsDuplicateMeshElements()
    {
        string body = TriMesh.Replace(
            "  3;0,1,2;;\n}",
            """
              3;0,1,2;;
              MeshNormals {
                3;
                0;0;1;,
                0;0;1;,
                0;0;1;;
                1;
                3;0,1,2;;
              }
              MeshNormals {
                3;
                0;0;1;,
                0;0;1;,
                0;0;1;;
                1;
                3;0,1,2;;
              }
            }
            """);

        Assert.Contains(
            "more than one MeshNormals",
            ParseError(Doc(body)).Message);
    }

    [Fact]
    public void RejectsMismatchedNormalsTopology()
    {
        Assert.Contains(
            "describes 2 face",
            ParseError(Doc("""
                Mesh Tri {
                  3;
                  0;0;0;,
                  1;0;0;,
                  0;1;0;;
                  1;
                  3;0,1,2;;
                  MeshNormals {
                    3;
                    0;0;1;,
                    0;0;1;,
                    0;0;1;;
                    2;
                    3;0,1,2;,
                    3;0,1,2;;
                  }
                }
                """)).Message);

        Assert.Contains(
            "corner(s)",
            ParseError(Doc("""
                Mesh Quad {
                  4;
                  0;0;0;,
                  1;0;0;,
                  1;1;0;,
                  0;1;0;;
                  1;
                  4;0,1,2,3;;
                  MeshNormals {
                    4;
                    0;0;1;,
                    0;0;1;,
                    0;0;1;,
                    0;0;1;;
                    1;
                    3;0,1,2;;
                  }
                }
                """)).Message);
    }

    [Fact]
    public void RejectsMismatchedTextureCoordinateCount()
    {
        ModelFormatException exception = ParseError(Doc("""
            Mesh Tri {
              3;
              0;0;0;,
              1;0;0;,
              0;1;0;;
              1;
              3;0,1,2;;
              MeshTextureCoords {
                2;
                0;0;,
                1;0;;
              }
            }
            """));

        Assert.Contains("2 coordinate(s)", exception.Message);
        Assert.Contains("3 vertices", exception.Message);
    }

    [Fact]
    public void RejectsMismatchedMaterialListsAndIndices()
    {
        Assert.Contains(
            "describes 2 face",
            ParseError(Doc("""
                Mesh Tri {
                  3;
                  0;0;0;,
                  1;0;0;,
                  0;1;0;;
                  1;
                  3;0,1,2;;
                  MeshMaterialList {
                    1;
                    2;
                    0,0;;
                    Material only {
                      1;1;1;1;;
                      0;
                      0;0;0;;
                      0;0;0;;
                    }
                  }
                }
                """)).Message);

        Assert.Contains(
            "valid indices are 0 through 0",
            ParseError(Doc("""
                Mesh Tri {
                  3;
                  0;0;0;,
                  1;0;0;,
                  0;1;0;;
                  1;
                  3;0,1,2;;
                  MeshMaterialList {
                    1;
                    1;
                    1;;
                    Material only {
                      1;1;1;1;;
                      0;
                      0;0;0;;
                      0;0;0;;
                    }
                  }
                }
                """)).Message);
    }

    [Fact]
    public void RejectsAnEmptyMaterialList()
    {
        ModelFormatException exception = ParseError(Doc("""
            Mesh Tri {
              3;
              0;0;0;,
              1;0;0;,
              0;1;0;;
              1;
              3;0,1,2;;
              MeshMaterialList {
                0;
                1;
                0;;
              }
            }
            """));

        Assert.Contains("declares no materials", exception.Message);
    }

    [Fact]
    public void RejectsUnsupportedMaterialProperties()
    {
        ModelFormatException exception = ParseError(Doc("""
            Mesh Tri {
              3;
              0;0;0;,
              1;0;0;,
              0;1;0;;
              1;
              3;0,1,2;;
              MeshMaterialList {
                1;
                1;
                0;;
                Material only {
                  1;1;1;1;;
                  0;
                  0;0;0;;
                  0;0;0;;
                  Power { }
                }
              }
            }
            """));

        Assert.Contains("material property", exception.Message);
        Assert.Contains("not supported", exception.Message);
    }

    [Fact]
    public void RejectsNonAffineAndDuplicateTransformMatrices()
    {
        Assert.Contains(
            "not an affine transform",
            ParseError(Doc("""
                Frame Root {
                  FrameTransformMatrix {
                    1,0,0,0,
                    0,1,0,0,
                    0,0,1,0,
                    0,0,0,2;;
                  }
                }
                """)).Message);

        Assert.Contains(
            "more than one FrameTransformMatrix",
            ParseError(Doc("""
                Frame Root {
                  FrameTransformMatrix {
                    1,0,0,0,
                    0,1,0,0,
                    0,0,1,0,
                    0,0,0,1;;
                  }
                  FrameTransformMatrix {
                    1,0,0,0,
                    0,1,0,0,
                    0,0,1,0,
                    0,0,0,1;;
                  }
                }
                """)).Message);
    }

    [Fact]
    public void RejectsATransformMatrixAfterGeometry()
    {
        ModelFormatException exception = ParseError(Doc("""
            Frame Root {
              Mesh Tri {
                3;
                0;0;0;,
                1;0;0;,
                0;1;0;;
                1;
                3;0,1,2;;
              }
              FrameTransformMatrix {
                1,0,0,0,
                0,1,0,0,
                0,0,1,0,
                0,0,0,1;;
              }
            }
            """));

        Assert.Contains("must precede", exception.Message);
    }

    [Fact]
    public void RejectsASingularTransformWhenNormalsArePresent()
    {
        ModelFormatException exception = ParseError(Doc("""
            Frame Root {
              FrameTransformMatrix {
                0,0,0,0,
                0,0,0,0,
                0,0,0,0,
                0,0,0,1;;
              }
              Mesh Tri {
                3;
                0;0;0;,
                1;0;0;,
                0;1;0;;
                1;
                3;0,1,2;;
                MeshNormals {
                  3;
                  0;0;1;,
                  0;0;1;,
                  0;0;1;;
                  1;
                  3;0,1,2;;
                }
              }
            }
            """));

        Assert.Contains("singular", exception.Message);
    }

    [Fact]
    public void ATransformedPositionThatOverflowsIsRejected()
    {
        ModelFormatException exception = ParseError(Doc("""
            Frame Root {
              FrameTransformMatrix {
                1e38,0,0,0,
                0,1,0,0,
                0,0,1,0,
                0,0,0,1;;
              }
              Mesh Tri {
                1;
                1e38;0;0;;
                1;
                3;0,0,0;;
              }
            }
            """));

        Assert.Contains("not finite", exception.Message);
    }

    [Fact]
    public void RejectsFramesNestedBeyondTheLimit()
    {
        XImportLimits limits = Limits(nestingDepth: 1);

        ModelFormatException exception = ParseError(
            Doc("""
                Frame Root {
                  Frame Child {
                  }
                }
                """),
            limits: limits);

        Assert.Contains("nesting exceeds", exception.Message);
    }

    [Fact]
    public void RejectsMoreFramesThanTheLimit()
    {
        XImportLimits limits = Limits(frameCount: 1);

        ModelFormatException exception = ParseError(
            Doc("""
                Frame First { }
                Frame Second { }
                """),
            limits: limits);

        Assert.Contains("more than 1 frames", exception.Message);
    }

    [Fact]
    public void RejectsCountsAboveConfiguredLimits()
    {
        Assert.Contains(
            "limit of 2",
            ParseError(Doc(TriMesh), limits: Limits(vertexCount: 2)).Message);
        Assert.Contains(
            "limit of 1",
            ParseError(Doc(QuadMesh), limits: Limits(triangleCount: 1)).Message);
        Assert.Contains(
            "limit of 2",
            ParseError(Doc(TriMesh), limits: Limits(faceVertexCount: 2)).Message);

        XImportLimits faceIndexLimits = Limits(faceIndexCount: 2);
        Assert.Contains(
            "indices",
            ParseError(Doc(TriMesh), limits: faceIndexLimits).Message);
    }

    [Fact]
    public void RejectsSubsetsAboveTheConfiguredLimit()
    {
        string body = """
            Mesh Quad {
              4;
              0;0;0;,
              1;0;0;,
              1;1;0;,
              0;1;0;;
              2;
              4;0,1,2,3;,
              4;0,1,2,3;;
              MeshMaterialList {
                2;
                2;
                0,1;;
                Material red {
                  1;0;0;1;;
                  0;
                  0;0;0;;
                  0;0;0;;
                }
                Material blue {
                  0;0;1;1;;
                  0;
                  0;0;0;;
                  0;0;0;;
                }
              }
            }
            """;

        ModelFormatException exception = ParseError(
            Doc(body),
            limits: Limits(subsetCount: 1));

        Assert.Contains("limit of 1", exception.Message);
    }

    [Fact]
    public void RejectsLongTokens()
    {
        ModelFormatException exception = ParseError(
            Doc(TriMesh.Replace("Mesh Tri", "Mesh AReallyLongMeshName")),
            limits: Limits(tokenLength: 8));

        Assert.Contains("longer than the supported limit", exception.Message);
    }

    [Fact]
    public void RejectsTooManyTokens()
    {
        ModelFormatException exception = ParseError(
            Doc(TriMesh),
            limits: Limits(tokenCount: 10));

        Assert.Contains("more than 10 tokens", exception.Message);
    }

    [Fact]
    public void RejectsInputAboveTheConfiguredLimit()
    {
        string text = Doc(TriMesh);

        ModelFormatException exception = ParseError(
            text,
            limits: Limits(inputBytes: 32));

        Assert.Contains("limit of 32 bytes", exception.Message);
    }

    [Fact]
    public void RejectsInvalidUtf8InsteadOfReplacingBytes()
    {
        byte[] prefix = Encoding.UTF8.GetBytes("xof 0303txt 0032\n// ");
        byte[] suffix = Encoding.UTF8.GetBytes("\n" + TriMesh);
        byte[] data = prefix.Concat(new byte[] { 0xFF }).Concat(suffix).ToArray();

        ModelFormatException exception = ParseError(data);

        Assert.Contains("not valid UTF-8", exception.Message);
    }

    [Fact]
    public void RejectsNullAndUnreadableStreams()
    {
        Assert.Throws<ArgumentNullException>(() => new XModelParser().Parse(null!));
        Assert.Throws<ArgumentException>(
            () => new XModelParser().Parse(new UnreadableStream()));
    }

    [Fact]
    public void LeavesTheStreamOpenOnSuccessAndFailure()
    {
        using var success = new MemoryStream(Encoding.UTF8.GetBytes(Doc(TriMesh)));
        new XModelParser().Parse(success);
        Assert.True(success.CanRead);

        using var failure = new MemoryStream(Encoding.UTF8.GetBytes("xof 0303txt 0032"));
        Assert.Throws<ModelFormatException>(() => new XModelParser().Parse(failure));
        Assert.True(failure.CanRead);
    }

    [Fact]
    public void ReadsFromTheStreamsCurrentPosition()
    {
        byte[] body = Encoding.UTF8.GetBytes(Doc(TriMesh));
        byte[] prefixed = Encoding.UTF8.GetBytes("ignored prefix\n").Concat(body).ToArray();
        using var stream = new MemoryStream(prefixed);
        stream.Position = "ignored prefix\n".Length;

        ModelData model = new XModelParser().Parse(stream);

        Assert.Single(model.Meshes);
    }

    [Fact]
    public void AcceptsANonSeekableStream()
    {
        using var stream = new NonSeekableStream(
            Encoding.UTF8.GetBytes(Doc(TriMesh)));

        ModelData model = new XModelParser().Parse(stream);

        Assert.Single(model.Meshes);
    }

    [Fact]
    public void RejectsEveryTruncatedPrefixWithoutUnexpectedExceptions()
    {
        byte[] complete = Encoding.UTF8.GetBytes(Doc(TriMesh));

        for (int length = 0; length < complete.Length; length++)
        {
            try
            {
                new XModelParser().Parse(
                    new MemoryStream(complete.AsSpan(0, length).ToArray()));
            }
            catch (ModelFormatException)
            {
                // Either the header or the data is incomplete. No other
                // exception type is acceptable for untrusted input.
            }
        }
    }

    [Fact]
    public void RandomByteMutationsFailPredictably()
    {
        byte[] seed = Encoding.UTF8.GetBytes(Doc("""
            Frame Root {
              FrameTransformMatrix {
                1,0,0,0,
                0,1,0,0,
                0,0,1,0,
                0,0,0,1;;
              }
              Mesh Quad {
                4;
                0;0;0;,
                1;0;0;,
                1;1;0;,
                0;1;0;;
                1;
                4;0,1,2,3;;
                MeshNormals {
                  4;
                  0;0;1;,
                  0;0;1;,
                  0;0;1;,
                  0;0;1;;
                  1;
                  4;0,1,2,3;;
                }
                MeshTextureCoords {
                  4;
                  0;0;,
                  1;0;,
                  1;1;,
                  0;1;;
                }
                MeshMaterialList {
                  1;
                  1;
                  0;;
                  Material only {
                    1;1;1;1;;
                    0;
                    0;0;0;;
                    0;0;0;;
                  }
                }
              }
            }
            """));
        var random = new Random(0x4E594D);

        for (int iteration = 0; iteration < 300; iteration++)
        {
            byte[] mutated = (byte[])seed.Clone();
            int changes = random.Next(1, 4);

            for (int change = 0; change < changes; change++)
            {
                mutated[random.Next(mutated.Length)] = (byte)random.Next(256);
            }

            try
            {
                new XModelParser().Parse(new MemoryStream(mutated));
            }
            catch (ModelFormatException)
            {
                // Hostile or malformed mutations must fail through the documented
                // exception rather than an implementation exception.
            }
        }
    }

    private static string Doc(string body)
    {
        return "xof 0303txt 0032\n" + body;
    }

    private static ModelData Parse(
        string text,
        string? sourceName = null,
        XImportLimits? limits = null)
    {
        return ParseBytes(Encoding.UTF8.GetBytes(text), sourceName, limits);
    }

    private static ModelData ParseBytes(
        byte[] data,
        string? sourceName = null,
        XImportLimits? limits = null)
    {
        using var stream = new MemoryStream(data);
        return new XModelParser(sourceName, limits ?? XImportLimits.Default)
            .Parse(stream);
    }

    private static ModelFormatException ParseError(
        string text,
        string? sourceName = null,
        XImportLimits? limits = null)
    {
        return ParseError(Encoding.UTF8.GetBytes(text), sourceName, limits);
    }

    private static ModelFormatException ParseError(
        byte[] data,
        string? sourceName = null,
        XImportLimits? limits = null)
    {
        using var stream = new MemoryStream(data);
        return Assert.Throws<ModelFormatException>(
            () => new XModelParser(sourceName, limits ?? XImportLimits.Default)
                .Parse(stream));
    }

    private static XImportLimits Limits(
        int inputBytes = 1024 * 1024,
        int vertexCount = 10_000,
        int triangleCount = 10_000,
        int subsetCount = 1_000,
        int faceVertexCount = 10_000,
        int faceIndexCount = 30_000,
        int nestingDepth = 16,
        int frameCount = 1_000,
        int tokenLength = 10_000,
        long tokenCount = 1_000_000)
    {
        return new XImportLimits(
            inputBytes,
            vertexCount,
            triangleCount,
            subsetCount,
            faceVertexCount,
            faceIndexCount,
            nestingDepth,
            frameCount,
            tokenLength,
            tokenCount);
    }

    private sealed class NonSeekableStream : Stream
    {
        private readonly MemoryStream _inner;

        public NonSeekableStream(byte[] data)
        {
            _inner = new MemoryStream(data, writable: false);
        }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return _inner.Read(buffer, offset, count);
        }

        public override int Read(Span<byte> buffer)
        {
            return _inner.Read(buffer);
        }

        public override void Flush()
        {
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

    private sealed class UnreadableStream : MemoryStream
    {
        public override bool CanRead => false;
    }
}
