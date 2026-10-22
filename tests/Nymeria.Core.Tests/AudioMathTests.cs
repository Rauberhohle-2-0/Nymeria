using System.Numerics;
using Nymeria.Core.Audio;

namespace Nymeria.Core.Tests;

public sealed class AudioMathTests
{
    private const float Tolerance = 1e-5f;

    [Theory]
    [InlineData(0.0f, 1.0f)]
    [InlineData(1.0f, 1.0f)]
    [InlineData(2.0f, 1.0f)]
    [InlineData(3.0f, 0.875f)]
    [InlineData(4.0f, 0.75f)]
    [InlineData(5.0f, 0.625f)]
    [InlineData(10.0f, 0.0f)]
    [InlineData(50.0f, 0.0f)]
    [InlineData(-1.0f, 1.0f)]
    public void DistanceAttenuation_ClampsToTheDocumentedRange(
        float distance,
        float expected)
    {
        float attenuation = AudioMath.DistanceAttenuation(distance, 2.0f, 10.0f);

        Assert.InRange(MathF.Abs(attenuation - expected), 0.0f, Tolerance);
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    public void DistanceAttenuation_NonFiniteDistanceIsSilent(float distance)
    {
        Assert.Equal(0.0f, AudioMath.DistanceAttenuation(distance, 1.0f, 10.0f));
    }

    [Fact]
    public void DistanceAttenuation_UsesDistanceBetweenPositions()
    {
        var listener = new Vector3(0.0f, 0.0f, 0.0f);

        Assert.Equal(
            1.0f,
            AudioMath.DistanceAttenuation(listener, listener, 2.0f, 10.0f));
        Assert.Equal(
            0.5f,
            AudioMath.DistanceAttenuation(
                listener,
                new Vector3(6.0f, 0.0f, 0.0f),
                2.0f,
                10.0f),
            5);
        Assert.Equal(
            0.0f,
            AudioMath.DistanceAttenuation(
                listener,
                new Vector3(0.0f, 0.0f, 10.0f),
                2.0f,
                10.0f));
    }

    [Fact]
    public void DistanceAttenuation_NonFinitePositionIsSilent()
    {
        Assert.Equal(
            0.0f,
            AudioMath.DistanceAttenuation(
                Vector3.Zero,
                new Vector3(float.NaN, 0.0f, 0.0f),
                1.0f,
                10.0f));
    }

    [Theory]
    [InlineData(-1.0f, 10.0f)]
    [InlineData(float.NaN, 10.0f)]
    [InlineData(1.0f, float.PositiveInfinity)]
    [InlineData(10.0f, 10.0f)]
    [InlineData(10.0f, 1.0f)]
    public void DistanceAttenuation_RejectsInvalidRange(float min, float max)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => AudioMath.DistanceAttenuation(1.0f, min, max));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => AudioMath.DistanceAttenuation(Vector3.Zero, Vector3.One, min, max));
    }

    [Fact]
    public void StereoPan_PlacesSourcesLeftRightAndCenter()
    {
        Vector3 listener = Vector3.Zero;
        Vector3 right = Vector3.UnitX;

        Assert.Equal(1.0f, AudioMath.StereoPan(listener, right, new Vector3(5.0f, 0.0f, 0.0f)), 5);
        Assert.Equal(-1.0f, AudioMath.StereoPan(listener, right, new Vector3(-5.0f, 0.0f, 0.0f)), 5);
        Assert.Equal(0.0f, AudioMath.StereoPan(listener, right, new Vector3(0.0f, 0.0f, -5.0f)), 5);
        Assert.Equal(0.0f, AudioMath.StereoPan(listener, right, new Vector3(0.0f, 5.0f, 0.0f)), 5);
    }

    [Fact]
    public void StereoPan_CoincidentSourceIsCentered()
    {
        Assert.Equal(
            0.0f,
            AudioMath.StereoPan(Vector3.Zero, Vector3.UnitX, Vector3.Zero));
    }

    [Fact]
    public void StereoPan_UsesRotatedListenerBasis()
    {
        // Looking down -X with +Y up makes -Z the listener's right side.
        var forward = new Vector3(-1.0f, 0.0f, 0.0f);
        Vector3 right = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitY));

        Assert.Equal(new Vector3(0.0f, 0.0f, -1.0f), right);
        Assert.Equal(
            1.0f,
            AudioMath.StereoPan(Vector3.Zero, right, new Vector3(0.0f, 0.0f, -5.0f)),
            5);
        Assert.Equal(
            -1.0f,
            AudioMath.StereoPan(Vector3.Zero, right, new Vector3(0.0f, 0.0f, 5.0f)),
            5);
    }

    [Fact]
    public void StereoPan_NormalizesAFullLengthAxis()
    {
        Assert.Equal(
            1.0f,
            AudioMath.StereoPan(
                Vector3.Zero,
                new Vector3(2.0f, 0.0f, 0.0f),
                new Vector3(1.0f, 0.0f, 0.0f)),
            5);
    }

    [Fact]
    public void StereoPan_DegenerateOrNonFiniteBasisIsCentered()
    {
        Assert.Equal(
            0.0f,
            AudioMath.StereoPan(Vector3.Zero, Vector3.Zero, Vector3.UnitX));
        Assert.Equal(
            0.0f,
            AudioMath.StereoPan(
                Vector3.Zero,
                new Vector3(float.NaN, 0.0f, 0.0f),
                Vector3.UnitX));
        Assert.Equal(
            0.0f,
            AudioMath.StereoPan(
                new Vector3(float.PositiveInfinity, 0.0f, 0.0f),
                Vector3.UnitX,
                Vector3.UnitX));
    }

    [Fact]
    public void EqualPowerGains_FollowThePanDirection()
    {
        (float left, float right) = AudioMath.EqualPowerGains(-1.0f);
        Assert.Equal(1.0f, left, 5);
        Assert.Equal(0.0f, right, 5);

        (left, right) = AudioMath.EqualPowerGains(0.0f);
        Assert.Equal(MathF.Sqrt(0.5f), left, 5);
        Assert.Equal(MathF.Sqrt(0.5f), right, 5);

        (left, right) = AudioMath.EqualPowerGains(1.0f);
        Assert.Equal(0.0f, left, 5);
        Assert.Equal(1.0f, right, 5);
    }

    [Fact]
    public void EqualPowerGains_ClampsAndCentersInvalidInput()
    {
        (float left, float right) = AudioMath.EqualPowerGains(-4.0f);
        Assert.Equal(1.0f, left, 5);
        Assert.Equal(0.0f, right, 5);

        (left, right) = AudioMath.EqualPowerGains(4.0f);
        Assert.Equal(0.0f, left, 5);
        Assert.Equal(1.0f, right, 5);

        (left, right) = AudioMath.EqualPowerGains(float.NaN);
        Assert.Equal(MathF.Sqrt(0.5f), left, 5);
        Assert.Equal(MathF.Sqrt(0.5f), right, 5);
    }
}
