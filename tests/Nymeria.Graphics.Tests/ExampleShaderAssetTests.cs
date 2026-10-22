using System.Xml.Linq;
using Nymeria.Graphics;
using SDL3;

namespace Nymeria.Graphics.Tests;

/// <summary>
/// Tests that the example projects' shader build wiring matches the assets
/// <see cref="ShaderProgram"/> resolves, and that every HLSL source declares the
/// register spaces SDL3 documents for its backend.
/// </summary>
/// <remarks>
/// These tests read repository files instead of creating a GPU device, so they
/// run on every CI platform. They catch the two mistakes a Windows-only build
/// would otherwise be the first to report: a generated DXIL resource embedded
/// under a name no renderer looks for, and an example that embeds a shared shader
/// family without the DXIL assets for it.
/// </remarks>
public sealed class ExampleShaderAssetTests
{
    private const string CubePrefix = "HelloCube.Shaders.Cube";
    private const string ModelPrefix = "Nymeria.Examples.Shaders.Model";
    private const string MaterialPrefix = "Nymeria.Examples.Shaders.Material";

    [Theory]
    [InlineData(
        "examples/HelloCube/HelloCube.csproj",
        CubePrefix,
        "Shaders/Cube.hlsl",
        new[] { "register(b0, space1)" })]
    [InlineData(
        "examples/Shared/ModelShaders.targets",
        ModelPrefix,
        "Shaders/Model.hlsl",
        new[] { "register(b0, space1)" })]
    [InlineData(
        "examples/Shared/MaterialShaders.targets",
        MaterialPrefix,
        "Shaders/Material.hlsl",
        new[]
        {
            "register(b0, space1)",
            "register(b0, space3)",
            "register(t0, space2)",
            "register(s0, space2)",
        })]
    public void DxilBuildItemsMatchTheShaderProgramContract(
        string declarationFile,
        string shaderPrefix,
        string hlslFile,
        string[] requiredRegisters)
    {
        XDocument document = Load(declarationFile);
        ShaderProgram program = ShaderProgram.Select(shaderPrefix, SDL.GPUShaderFormat.DXIL);

        List<StageItem> stages = ReadDxilStages(document);
        Assert.Equal(2, stages.Count);

        StageItem vertex = Assert.Single(stages, stage => stage.Profile == "vs_6_0");
        StageItem fragment = Assert.Single(stages, stage => stage.Profile == "ps_6_0");

        Assert.Equal(program.Vertex.EntryPoint, vertex.EntryPoint);
        Assert.Equal(program.Fragment.EntryPoint, fragment.EntryPoint);

        List<EmbeddedItem> embedded = document.Descendants("EmbeddedResource")
            .Select(element => new EmbeddedItem(
                (string?)element.Attribute("Include") ?? string.Empty,
                (string?)element.Attribute("LogicalName") ?? string.Empty))
            .ToList();

        // Each stage's generated file must be embedded under the exact logical
        // name the renderer looks for, and must be the file that stage writes.
        EmbeddedItem vertexResource = Assert.Single(
            embedded,
            item => item.LogicalName == program.Vertex.ResourceName);
        EmbeddedItem fragmentResource = Assert.Single(
            embedded,
            item => item.LogicalName == program.Fragment.ResourceName);

        Assert.EndsWith(vertex.OutputName, vertexResource.Include, StringComparison.Ordinal);
        Assert.EndsWith(fragment.OutputName, fragmentResource.Include, StringComparison.Ordinal);

        // The HLSL item and the expected source are both relative to the
        // directory holding the declaration file.
        string declarationDirectory = Path.GetDirectoryName(
            Path.GetFullPath(Path.Combine(RepositoryRoot, declarationFile)))!;
        string expectedPath = Path.GetFullPath(Path.Combine(declarationDirectory, hlslFile));

        foreach (StageItem stage in stages)
        {
            string includePath = stage.Include
                .Replace("$(MSBuildThisFileDirectory)", string.Empty, StringComparison.Ordinal);

            string resolved = Path.GetFullPath(Path.Combine(declarationDirectory, includePath));

            Assert.Equal(expectedPath, resolved);
        }

        string hlsl = File.ReadAllText(expectedPath);

        Assert.Contains("VSMain(", hlsl, StringComparison.Ordinal);
        Assert.Contains("PSMain(", hlsl, StringComparison.Ordinal);
        Assert.Contains("SV_Position", hlsl, StringComparison.Ordinal);
        Assert.Contains("SV_Target", hlsl, StringComparison.Ordinal);

        foreach (string register in requiredRegisters)
        {
            Assert.Contains(register, hlsl, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void CubeExampleStillEmbedsTheMslSource()
    {
        XDocument document = Load("examples/HelloCube/HelloCube.csproj");

        List<string?> embeddedNames = document.Descendants("EmbeddedResource")
            .Select(element => (string?)element.Attribute("LogicalName"))
            .ToList();

        Assert.Contains(
            ShaderProgram.Select(CubePrefix, SDL.GPUShaderFormat.MSL).Vertex.ResourceName,
            embeddedNames);
    }

    [Fact]
    public void EveryExampleThatEmbedsASharedShaderImportsItsDxilAssets()
    {
        string examplesDirectory = Path.Combine(RepositoryRoot, "examples");

        int modelProjects = 0;
        int materialProjects = 0;

        foreach (string projectFile in Directory.EnumerateFiles(
                     examplesDirectory,
                     "*.csproj",
                     SearchOption.AllDirectories))
        {
            XDocument document = XDocument.Load(projectFile);

            List<string> includes = document.Descendants("EmbeddedResource")
                .Select(element => (string?)element.Attribute("Include") ?? string.Empty)
                .ToList();

            List<string> imports = document.Descendants("Import")
                .Select(element => (string?)element.Attribute("Project") ?? string.Empty)
                .ToList();

            if (includes.Any(include => include.EndsWith("Shaders/Model.metal", StringComparison.Ordinal)))
            {
                modelProjects++;
                Assert.Contains(
                    imports,
                    import => import.EndsWith("ModelShaders.targets", StringComparison.Ordinal));
            }

            if (includes.Any(include => include.EndsWith("Shaders/Material.metal", StringComparison.Ordinal)))
            {
                materialProjects++;
                Assert.Contains(
                    imports,
                    import => import.EndsWith("MaterialShaders.targets", StringComparison.Ordinal));
            }
        }

        // The scan must find the examples it is meant to guard, otherwise a
        // rename would silently turn this test into a no-op.
        Assert.Equal(4, modelProjects);
        Assert.Equal(10, materialProjects);
    }

    private static List<StageItem> ReadDxilStages(XDocument document)
    {
        return document.Descendants("NymeriaHlslShader")
            .Select(element => new StageItem(
                (string?)element.Attribute("Include") ?? string.Empty,
                (string?)element.Attribute("Profile") ?? string.Empty,
                (string?)element.Attribute("EntryPoint") ?? string.Empty,
                (string?)element.Attribute("OutputName") ?? string.Empty))
            .ToList();
    }

    private static XDocument Load(string relativePath)
    {
        string path = Path.Combine(RepositoryRoot, relativePath);
        Assert.True(File.Exists(path), $"Expected repository file '{relativePath}' to exist.");
        return XDocument.Load(path);
    }

    private static string RepositoryRoot { get; } = FindRepositoryRoot();

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Nymeria.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            $"Could not find the Nymeria repository root above '{AppContext.BaseDirectory}'.");
    }

    private sealed record StageItem(
        string Include,
        string Profile,
        string EntryPoint,
        string OutputName);

    private sealed record EmbeddedItem(string Include, string LogicalName);
}
