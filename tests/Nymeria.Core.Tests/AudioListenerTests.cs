using System.Numerics;
using Nymeria.Core.Audio;

namespace Nymeria.Core.Tests;

public sealed class AudioListenerTests
{
    private const float Tolerance = 1e-5f;

    [Fact]
    public void DefaultListener_MatchesCameraConventions()
    {
        var listener = new AudioListener();

        Assert.Equal(Vector3.Zero, listener.Position);
        Assert.Equal(-Vector3.UnitZ, listener.Forward);
        Assert.Equal(Vector3.UnitY, listener.Up);
        Assert.Equal(Vector3.UnitX, listener.Right);
    }

    [Fact]
    public void ForwardAndUp_AreNormalized()
    {
        var listener = new AudioListener
        {
            Forward = new Vector3(0.0f, 0.0f, -8.0f),
            Up = new Vector3(0.0f, 3.0f, 0.0f),
        };

        Assert.Equal(-Vector3.UnitZ, listener.Forward);
        Assert.Equal(Vector3.UnitY, listener.Up);
    }

    [Fact]
    public void Right_UsesRotatedOrientation()
    {
        var listener = new AudioListener
        {
            Forward = new Vector3(-1.0f, 0.0f, 0.0f),
        };

        Assert.InRange(
            MathF.Abs(listener.Right.X - 0.0f),
            0.0f,
            Tolerance);
        Assert.InRange(
            MathF.Abs(listener.Right.Y - 0.0f),
            0.0f,
            Tolerance);
        Assert.InRange(
            MathF.Abs(listener.Right.Z - -1.0f),
            0.0f,
            Tolerance);
    }

    [Fact]
    public void Right_IsZeroWhenForwardAndUpAreParallel()
    {
        var listener = new AudioListener
        {
            Forward = Vector3.UnitY,
            Up = Vector3.UnitY,
        };

        Assert.Equal(Vector3.Zero, listener.Right);
    }

    [Fact]
    public void Position_RejectsNonFiniteValues()
    {
        var listener = new AudioListener();

        Assert.Throws<ArgumentException>(
            () => listener.Position = new Vector3(float.NaN, 0.0f, 0.0f));
        Assert.Throws<ArgumentException>(
            () => listener.Position = new Vector3(0.0f, float.PositiveInfinity, 0.0f));
    }

    [Fact]
    public void Directions_RejectZeroAndNonFiniteValues()
    {
        var listener = new AudioListener();

        Assert.Throws<ArgumentException>(() => listener.Forward = Vector3.Zero);
        Assert.Throws<ArgumentException>(() => listener.Up = Vector3.Zero);
        Assert.Throws<ArgumentException>(
            () => listener.Forward = new Vector3(float.NaN, 0.0f, 0.0f));
        Assert.Throws<ArgumentException>(
            () => listener.Up = new Vector3(0.0f, float.PositiveInfinity, 0.0f));
    }

    [Fact]
    public void PositionAndOrientationCanBeUpdatedRepeatedly()
    {
        var listener = new AudioListener();

        for (int index = 0; index < 1000; index++)
        {
            listener.Position = new Vector3(index, 0.0f, -index);
            listener.Forward = Vector3.Normalize(new Vector3(index, 1.0f, -1.0f));
            listener.Up = Vector3.UnitY;
        }

        Assert.True(float.IsFinite(listener.Position.X));
        Assert.True(float.IsFinite(listener.Forward.X));
        Assert.True(float.IsFinite(listener.Right.X));
    }
}
