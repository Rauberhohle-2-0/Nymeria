using System.Numerics;
using Nymeria.Core.Lighting;
using Nymeria.Core.Scenes;

namespace Nymeria.Core.Tests;

public sealed class LightingTests
{
    [Fact]
    public void AmbientLight_DefaultIsDimWhite()
    {
        AmbientLight light = AmbientLight.Default;

        Assert.Equal(Vector3.One, light.Color);
        Assert.Equal(0.1f, light.Intensity);
        Assert.Equal(new Vector3(0.1f, 0.1f, 0.1f), light.EffectiveColor);
    }

    [Fact]
    public void AmbientLight_MultipliesColorByIntensity()
    {
        var light = new AmbientLight(new Vector3(0.2f, 0.4f, 0.6f), 0.5f);

        Assert.Equal(new Vector3(0.1f, 0.2f, 0.3f), light.EffectiveColor);
    }

    [Fact]
    public void AmbientLight_AcceptsZeroIntensity()
    {
        var light = new AmbientLight(Vector3.One, 0.0f);

        Assert.Equal(Vector3.Zero, light.EffectiveColor);
    }

    [Theory]
    [InlineData(-0.01f, 0.0f, 0.0f)]
    [InlineData(0.0f, 1.01f, 0.0f)]
    [InlineData(0.0f, 0.0f, float.NaN)]
    [InlineData(float.PositiveInfinity, 0.0f, 0.0f)]
    public void AmbientLight_RejectsInvalidColor(float x, float y, float z)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new AmbientLight(new Vector3(x, y, z), 1.0f));
    }

    [Theory]
    [InlineData(-0.01f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(AmbientLight.MaxIntensity + 1.0f)]
    public void AmbientLight_RejectsInvalidIntensity(float intensity)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new AmbientLight(Vector3.One, intensity));
    }

    [Fact]
    public void DirectionalLight_NormalizesTravelDirection()
    {
        var light = new DirectionalLight(
            new Vector3(0.0f, 0.0f, -4.0f),
            new Vector3(1.0f, 0.8f, 0.6f),
            2.0f);

        AssertVectorNear(new Vector3(0.0f, 0.0f, -1.0f), light.Direction);
        AssertVectorNear(new Vector3(0.0f, 0.0f, 1.0f), light.DirectionToLight);
        Assert.Equal(new Vector3(2.0f, 1.6f, 1.2f), light.EffectiveColor);
    }

    [Fact]
    public void DirectionalLight_DefaultTravelsDownwardFromTheFrontRight()
    {
        DirectionalLight light = DirectionalLight.Default;

        Assert.Equal(1.0f, light.Direction.Length(), 5);
        Assert.True(light.DirectionToLight.Y > 0.0f);
        Assert.True(light.DirectionToLight.Z > 0.0f);
    }

    [Theory]
    [InlineData(0.0f, 0.0f, 0.0f)]
    [InlineData(float.NaN, 0.0f, 0.0f)]
    [InlineData(0.0f, float.PositiveInfinity, 0.0f)]
    [InlineData(0.0f, 0.0f, float.NegativeInfinity)]
    public void DirectionalLight_RejectsInvalidDirection(float x, float y, float z)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new DirectionalLight(new Vector3(x, y, z), Vector3.One, 1.0f));
    }

    [Theory]
    [InlineData(-0.01f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(DirectionalLight.MaxIntensity + 1.0f)]
    public void DirectionalLight_RejectsInvalidIntensity(float intensity)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new DirectionalLight(Vector3.UnitZ, Vector3.One, intensity));
    }

    [Fact]
    public void SceneLighting_RequiresAmbientLight()
    {
        Assert.Throws<ArgumentNullException>(
            () => new SceneLighting(null!));
    }

    [Fact]
    public void SceneLighting_SupportsAmbientOnly()
    {
        var lighting = new SceneLighting(AmbientLight.Default);

        Assert.Null(lighting.Directional);
        Assert.False(lighting.IsUnlit);
    }

    [Fact]
    public void SceneLighting_ReportsZeroContributionAsUnlit()
    {
        var lighting = new SceneLighting(
            new AmbientLight(Vector3.One, 0.0f),
            new DirectionalLight(Vector3.UnitZ, Vector3.One, 0.0f));

        Assert.True(lighting.IsUnlit);
    }

    [Fact]
    public void Scene_LightingDefaultsToNullAndCanBeReplacedAndCleared()
    {
        var scene = new Scene();
        var first = new SceneLighting(AmbientLight.Default);
        var second = new SceneLighting(AmbientLight.Default, DirectionalLight.Default);

        Assert.Null(scene.Lighting);

        scene.Lighting = first;
        Assert.Same(first, scene.Lighting);

        scene.Lighting = second;
        Assert.Same(second, scene.Lighting);

        scene.Lighting = null;
        Assert.Null(scene.Lighting);
    }

    private static void AssertVectorNear(Vector3 expected, Vector3 actual)
    {
        Assert.InRange(MathF.Abs(expected.X - actual.X), 0.0f, 1e-5f);
        Assert.InRange(MathF.Abs(expected.Y - actual.Y), 0.0f, 1e-5f);
        Assert.InRange(MathF.Abs(expected.Z - actual.Z), 0.0f, 1e-5f);
    }
}
