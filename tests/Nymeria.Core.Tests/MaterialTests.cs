using System.Numerics;
using Nymeria.Core.Materials;

namespace Nymeria.Core.Tests;

public sealed class MaterialTests
{
    [Fact]
    public void Default_IsSharedUntexturedWhite()
    {
        Material material = Material.Default;

        Assert.Equal(Vector3.One, material.BaseColor);
        Assert.Null(material.BaseColorTexture);
        Assert.False(material.HasTexture);
        Assert.Equal("default", material.Name);
        Assert.Same(material, Material.Default);
    }

    [Fact]
    public void Constructor_ReferencesTextureWithoutCopying()
    {
        var texture = new TextureData(1, 1, new byte[] { 10, 20, 30, 255 });
        var material = new Material(new Vector3(0.5f, 0.25f, 0.75f), texture, " test ");

        Assert.Equal(new Vector3(0.5f, 0.25f, 0.75f), material.BaseColor);
        Assert.Same(texture, material.BaseColorTexture);
        Assert.True(material.HasTexture);
        Assert.Equal(" test ", material.Name);
    }

    [Theory]
    [MemberData(nameof(InvalidColors))]
    public void Constructor_RejectsInvalidBaseColor(Vector3 baseColor)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Material(baseColor));
    }

    public static TheoryData<Vector3> InvalidColors => new()
    {
        new Vector3(float.NaN, 0.0f, 0.0f),
        new Vector3(0.0f, float.PositiveInfinity, 0.0f),
        new Vector3(-0.01f, 0.0f, 0.0f),
        new Vector3(0.0f, 1.01f, 0.0f),
    };

    [Fact]
    public void Defaults_AreOpaqueAndUnmodified()
    {
        var material = new Material(Vector3.One);

        Assert.Equal(MaterialTransparency.Opaque, material.Transparency);
        Assert.Equal(Material.DefaultAlphaCutoff, material.AlphaCutoff);
        Assert.Equal(1.0f, material.Alpha);
        Assert.False(material.RequiresBlendOrdering);
        Assert.Equal(MaterialTransparency.Opaque, Material.Default.Transparency);
    }

    [Theory]
    [InlineData(MaterialTransparency.Opaque, false)]
    [InlineData(MaterialTransparency.Mask, false)]
    [InlineData(MaterialTransparency.Blend, true)]
    public void Transparency_SelectsBlendOrdering(
        MaterialTransparency transparency,
        bool requiresBlending)
    {
        var material = new Material(Vector3.One, transparency: transparency);

        Assert.Equal(transparency, material.Transparency);
        Assert.Equal(requiresBlending, material.RequiresBlendOrdering);
    }

    [Theory]
    [InlineData(0.0f)]
    [InlineData(0.5f)]
    [InlineData(1.0f)]
    public void Constructor_AcceptsBoundaryAlphaCutoffs(float cutoff)
    {
        var material = new Material(
            Vector3.One,
            transparency: MaterialTransparency.Mask,
            alphaCutoff: cutoff);

        Assert.Equal(cutoff, material.AlphaCutoff);
    }

    [Theory]
    [InlineData(-0.001f)]
    [InlineData(1.001f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void Constructor_RejectsInvalidAlphaCutoff(float cutoff)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Material(
            Vector3.One,
            transparency: MaterialTransparency.Mask,
            alphaCutoff: cutoff));
    }

    [Theory]
    [InlineData(0.0f)]
    [InlineData(0.25f)]
    [InlineData(1.0f)]
    public void Constructor_AcceptsBoundaryAlpha(float alpha)
    {
        var material = new Material(
            Vector3.One,
            transparency: MaterialTransparency.Blend,
            alpha: alpha);

        Assert.Equal(alpha, material.Alpha);
    }

    [Theory]
    [InlineData(-0.001f)]
    [InlineData(1.001f)]
    [InlineData(float.NaN)]
    [InlineData(float.NegativeInfinity)]
    public void Constructor_RejectsInvalidAlpha(float alpha)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Material(
            Vector3.One,
            transparency: MaterialTransparency.Blend,
            alpha: alpha));
    }

    [Fact]
    public void Constructor_RejectsAnUndefinedTransparencyMode()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Material(
            Vector3.One,
            transparency: (MaterialTransparency)3));
    }

    [Fact]
    public void Constructor_KeepsTextureAndTransparencyIndependent()
    {
        var texture = new TextureData(1, 1, new byte[] { 10, 20, 30, 64 });
        var material = new Material(
            Vector3.One,
            texture,
            "masked",
            MaterialTransparency.Mask,
            alphaCutoff: 0.25f,
            alpha: 0.75f);

        Assert.Same(texture, material.BaseColorTexture);
        Assert.True(material.HasTexture);
        Assert.Equal("masked", material.Name);
        Assert.Equal(MaterialTransparency.Mask, material.Transparency);
        Assert.Equal(0.25f, material.AlphaCutoff);
        Assert.Equal(0.75f, material.Alpha);
    }
}
