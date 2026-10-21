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
}
