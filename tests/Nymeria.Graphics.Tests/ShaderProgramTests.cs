using Nymeria.Graphics;
using SDL3;

namespace Nymeria.Graphics.Tests;

/// <summary>
/// Tests the shader-format-to-asset mapping without a device, a GPU, or a native
/// library, so they run on every CI platform.
/// </summary>
public sealed class ShaderProgramTests
{
    private const string Prefix = "Example.Shaders.Family";

    [Fact]
    public void Dxil_SelectsOneResourcePerStage()
    {
        ShaderProgram program = ShaderProgram.Select(Prefix, SDL.GPUShaderFormat.DXIL);

        Assert.Equal(SDL.GPUShaderFormat.DXIL, program.Format);
        Assert.Equal(SDL.GPUShaderFormat.DXIL, program.Vertex.Format);
        Assert.Equal(SDL.GPUShaderFormat.DXIL, program.Fragment.Format);
        Assert.Equal(Prefix + ".vert.dxil", program.Vertex.ResourceName);
        Assert.Equal(Prefix + ".frag.dxil", program.Fragment.ResourceName);
        Assert.Equal("VSMain", program.Vertex.EntryPoint);
        Assert.Equal("PSMain", program.Fragment.EntryPoint);
    }

    [Fact]
    public void Msl_UsesOneSourceResourceForBothStages()
    {
        ShaderProgram program = ShaderProgram.Select(Prefix, SDL.GPUShaderFormat.MSL);

        Assert.Equal(SDL.GPUShaderFormat.MSL, program.Format);
        Assert.Equal(Prefix + ".metal", program.Vertex.ResourceName);
        Assert.Equal(Prefix + ".metal", program.Fragment.ResourceName);
        Assert.Equal("vertex_main", program.Vertex.EntryPoint);
        Assert.Equal("fragment_main", program.Fragment.EntryPoint);
    }

    [Fact]
    public void Dxil_DoesNotReferenceMetalAssets()
    {
        ShaderProgram program = ShaderProgram.Select(Prefix, SDL.GPUShaderFormat.DXIL);

        Assert.DoesNotContain(".metal", program.Vertex.ResourceName, StringComparison.Ordinal);
        Assert.DoesNotContain(".metal", program.Fragment.ResourceName, StringComparison.Ordinal);
        Assert.DoesNotContain("vertex_main", program.Vertex.EntryPoint, StringComparison.Ordinal);
        Assert.DoesNotContain("fragment_main", program.Fragment.EntryPoint, StringComparison.Ordinal);
    }

    [Fact]
    public void Msl_DoesNotReferenceDxilAssets()
    {
        ShaderProgram program = ShaderProgram.Select(Prefix, SDL.GPUShaderFormat.MSL);

        Assert.DoesNotContain(".dxil", program.Vertex.ResourceName, StringComparison.Ordinal);
        Assert.DoesNotContain(".dxil", program.Fragment.ResourceName, StringComparison.Ordinal);
        Assert.DoesNotContain("VSMain", program.Vertex.EntryPoint, StringComparison.Ordinal);
        Assert.DoesNotContain("PSMain", program.Fragment.EntryPoint, StringComparison.Ordinal);
    }

    [Fact]
    public void StageResources_AreDistinctForDxil_AndSharedForMsl()
    {
        ShaderProgram dxil = ShaderProgram.Select(Prefix, SDL.GPUShaderFormat.DXIL);
        ShaderProgram msl = ShaderProgram.Select(Prefix, SDL.GPUShaderFormat.MSL);

        Assert.NotEqual(dxil.Vertex.ResourceName, dxil.Fragment.ResourceName);
        Assert.Equal(msl.Vertex.ResourceName, msl.Fragment.ResourceName);
    }

    [Theory]
    [InlineData(SDL.GPUShaderFormat.SPIRV)]
    [InlineData(SDL.GPUShaderFormat.MetalLib)]
    [InlineData(SDL.GPUShaderFormat.DXBC)]
    [InlineData(SDL.GPUShaderFormat.Invalid)]
    public void UnsupportedFormat_ReportsTheFormatAndFamily(SDL.GPUShaderFormat format)
    {
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => ShaderProgram.Select(Prefix, format));

        Assert.Contains(Prefix, exception.Message, StringComparison.Ordinal);
        Assert.Contains("DXIL", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Metal Shading Language", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(SDL.GPUShaderFormat.SPIRV, "SPIR-V")]
    [InlineData(SDL.GPUShaderFormat.MetalLib, "metallib")]
    [InlineData(SDL.GPUShaderFormat.DXBC, "DXBC")]
    public void UnsupportedFormat_NamesTheRejectedFormat(SDL.GPUShaderFormat format, string name)
    {
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => ShaderProgram.Select(Prefix, format));

        Assert.Contains(name, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EmptyPrefix_IsRejected()
    {
        Assert.Throws<ArgumentException>(() => ShaderProgram.Select(string.Empty, SDL.GPUShaderFormat.DXIL));
    }

    [Fact]
    public void ReadBytes_ReportsMissingResourcesByFormatAndName()
    {
        ShaderAsset asset = ShaderProgram
            .Select("Example.Shaders.Missing", SDL.GPUShaderFormat.DXIL)
            .Vertex;

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => asset.ReadBytes(typeof(ShaderProgram).Assembly));

        Assert.Contains("DXIL", exception.Message, StringComparison.Ordinal);
        Assert.Contains(asset.ResourceName, exception.Message, StringComparison.Ordinal);
        Assert.Contains("Nymeria.Graphics", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(SDL.GPUShaderFormat.DXIL, "DXIL")]
    [InlineData(SDL.GPUShaderFormat.MSL, "MSL")]
    public void ReadBytes_NamesTheAssetFormat(SDL.GPUShaderFormat format, string expected)
    {
        ShaderAsset asset = ShaderProgram.Select("Example.Shaders.Missing", format).Fragment;

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => asset.ReadBytes(typeof(ShaderProgram).Assembly));

        Assert.Contains(expected, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadBytes_RequiresAnAssembly()
    {
        ShaderAsset asset = ShaderProgram.Select(Prefix, SDL.GPUShaderFormat.DXIL).Vertex;

        Assert.Throws<ArgumentNullException>(() => asset.ReadBytes(null!));
    }
}
