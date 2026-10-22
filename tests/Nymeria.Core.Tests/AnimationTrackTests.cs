using System.Numerics;
using Nymeria.Core.Animation;

namespace Nymeria.Core.Tests;

public sealed class AnimationTrackTests
{
    private const float Tolerance = 1e-5f;

    [Fact]
    public void Vector3Track_ClampsAndInterpolates()
    {
        var track = new Vector3Track(
        [
            new Vector3Keyframe(0.0f, Vector3.Zero),
            new Vector3Keyframe(1.0f, new Vector3(10.0f, 0.0f, 0.0f)),
            new Vector3Keyframe(3.0f, new Vector3(10.0f, 20.0f, 0.0f)),
        ]);

        AssertVectorNear(Vector3.Zero, track.Evaluate(0.0f));
        AssertVectorNear(new Vector3(5.0f, 0.0f, 0.0f), track.Evaluate(0.5f));
        AssertVectorNear(new Vector3(10.0f, 0.0f, 0.0f), track.Evaluate(1.0f));
        AssertVectorNear(new Vector3(10.0f, 10.0f, 0.0f), track.Evaluate(2.0f));
        AssertVectorNear(new Vector3(10.0f, 20.0f, 0.0f), track.Evaluate(3.0f));
        AssertVectorNear(new Vector3(10.0f, 20.0f, 0.0f), track.Evaluate(50.0f));
    }

    [Fact]
    public void Vector3Track_BeforeFirstKeyframeReturnsFirstValue()
    {
        var track = new Vector3Track(
        [
            new Vector3Keyframe(2.0f, new Vector3(1.0f, 2.0f, 3.0f)),
        ]);

        AssertVectorNear(new Vector3(1.0f, 2.0f, 3.0f), track.Evaluate(0.0f));
        AssertVectorNear(new Vector3(1.0f, 2.0f, 3.0f), track.Evaluate(2.0f));
        AssertVectorNear(new Vector3(1.0f, 2.0f, 3.0f), track.Evaluate(99.0f));
        Assert.Equal(2.0f, track.EndTime, 6);
    }

    [Fact]
    public void Vector3Track_CopiesTheInput()
    {
        Vector3Keyframe[] keys =
        [
            new(0.0f, Vector3.Zero),
            new(1.0f, Vector3.One),
        ];
        var track = new Vector3Track(keys);

        keys[1] = new Vector3Keyframe(1.0f, new Vector3(9.0f, 9.0f, 9.0f));

        AssertVectorNear(Vector3.One, track.Evaluate(1.0f));
    }

    [Fact]
    public void Vector3Track_RejectsInvalidSequences()
    {
        Assert.Throws<ArgumentNullException>(() => new Vector3Track(null!));
        Assert.Throws<ArgumentException>(() => new Vector3Track([]));
        Assert.Throws<ArgumentException>(
            () => new Vector3Track(
            [
                new Vector3Keyframe(1.0f, Vector3.Zero),
                new Vector3Keyframe(1.0f, Vector3.One),
            ]));
        Assert.Throws<ArgumentException>(
            () => new Vector3Track(
            [
                new Vector3Keyframe(2.0f, Vector3.Zero),
                new Vector3Keyframe(1.0f, Vector3.One),
            ]));
        Assert.Throws<ArgumentException>(
            () => new Vector3Track([new Vector3Keyframe(-1.0f, Vector3.Zero)]));
        Assert.Throws<ArgumentException>(
            () => new Vector3Track([new Vector3Keyframe(float.NaN, Vector3.Zero)]));
        Assert.Throws<ArgumentException>(
            () => new Vector3Track(
                [new Vector3Keyframe(Vector3Track.MaxTimeSeconds + 1.0f, Vector3.Zero)]));
        Assert.Throws<ArgumentException>(
            () => new Vector3Track(
                [new Vector3Keyframe(0.0f, new Vector3(float.NaN, 0.0f, 0.0f))]));
        Assert.Throws<ArgumentException>(
            () => new Vector3Track(
                [new Vector3Keyframe(0.0f, new Vector3(0.0f, float.PositiveInfinity, 0.0f))]));
    }

    [Fact]
    public void Vector3Track_RejectsTooManyKeyframes()
    {
        Assert.Throws<ArgumentException>(
            () => new Vector3Track(GenerateKeys(Vector3Track.MaxKeyframeCount + 1)));
    }

    [Fact]
    public void Vector3Track_EvaluateRejectsInvalidTimes()
    {
        var track = new Vector3Track([new Vector3Keyframe(0.0f, Vector3.Zero)]);

        Assert.Throws<ArgumentOutOfRangeException>(() => track.Evaluate(-1.0f));
        Assert.Throws<ArgumentOutOfRangeException>(() => track.Evaluate(float.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => track.Evaluate(float.PositiveInfinity));
    }

    [Fact]
    public void QuaternionTrack_InterpolatesTheShortestPath()
    {
        var track = new QuaternionTrack(
        [
            new QuaternionKeyframe(0.0f, Quaternion.Identity),
            new QuaternionKeyframe(
                2.0f,
                Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2.0f)),
        ]);

        Quaternion quarter = track.Evaluate(1.0f);
        Quaternion expected = Quaternion.CreateFromAxisAngle(
            Vector3.UnitY,
            MathF.PI / 4.0f);

        AssertQuaternionNear(expected, quarter);
        Assert.Equal(1.0f, quarter.Length(), 5);
    }

    [Fact]
    public void QuaternionTrack_FlippedEndpointStillTakesTheShortestPath()
    {
        Quaternion target = Quaternion.CreateFromAxisAngle(
            Vector3.UnitY,
            MathF.PI / 2.0f);
        Quaternion flipped = new(-target.X, -target.Y, -target.Z, -target.W);

        var track = new QuaternionTrack(
        [
            new QuaternionKeyframe(0.0f, Quaternion.Identity),
            new QuaternionKeyframe(2.0f, flipped),
        ]);

        Quaternion expected = Quaternion.CreateFromAxisAngle(
            Vector3.UnitY,
            MathF.PI / 4.0f);

        AssertQuaternionNear(expected, track.Evaluate(1.0f));
    }

    [Fact]
    public void QuaternionTrack_NormalizesKeysAndNearIdenticalOrientations()
    {
        var scaled = new QuaternionTrack(
            [new QuaternionKeyframe(0.0f, new Quaternion(0.0f, 0.0f, 0.0f, 2.0f))]);

        AssertQuaternionNear(Quaternion.Identity, scaled.Evaluate(0.0f));

        Quaternion near = Quaternion.CreateFromAxisAngle(Vector3.UnitX, 1e-5f);
        var close = new QuaternionTrack(
        [
            new QuaternionKeyframe(0.0f, Quaternion.Identity),
            new QuaternionKeyframe(1.0f, near),
        ]);

        Quaternion middle = close.Evaluate(0.5f);

        Assert.True(float.IsFinite(middle.X));
        Assert.True(float.IsFinite(middle.W));
        Assert.Equal(1.0f, middle.Length(), 5);
    }

    [Fact]
    public void QuaternionTrack_SingleKeyframeIsConstant()
    {
        Quaternion rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0.7f);
        var track = new QuaternionTrack([new QuaternionKeyframe(4.0f, rotation)]);

        AssertQuaternionNear(rotation, track.Evaluate(0.0f));
        AssertQuaternionNear(rotation, track.Evaluate(4.0f));
        AssertQuaternionNear(rotation, track.Evaluate(400.0f));
    }

    [Fact]
    public void QuaternionTrack_RejectsInvalidSequences()
    {
        Assert.Throws<ArgumentNullException>(() => new QuaternionTrack(null!));
        Assert.Throws<ArgumentException>(() => new QuaternionTrack([]));
        Assert.Throws<ArgumentException>(
            () => new QuaternionTrack(
            [
                new QuaternionKeyframe(1.0f, Quaternion.Identity),
                new QuaternionKeyframe(1.0f, Quaternion.Identity),
            ]));
        Assert.Throws<ArgumentException>(
            () => new QuaternionTrack(
                [new QuaternionKeyframe(0.0f, new Quaternion(0.0f, 0.0f, 0.0f, 0.0f))]));
        Assert.Throws<ArgumentException>(
            () => new QuaternionTrack(
                [new QuaternionKeyframe(0.0f, new Quaternion(float.NaN, 0.0f, 0.0f, 1.0f))]));
        Assert.Throws<ArgumentException>(
            () => new QuaternionTrack(
                [new QuaternionKeyframe(float.PositiveInfinity, Quaternion.Identity)]));
    }

    private static IEnumerable<Vector3Keyframe> GenerateKeys(int count)
    {
        for (int index = 0; index < count; index++)
        {
            yield return new Vector3Keyframe(index, Vector3.Zero);
        }
    }

    private static void AssertVectorNear(Vector3 expected, Vector3 actual)
    {
        Assert.InRange(MathF.Abs(expected.X - actual.X), 0.0f, Tolerance);
        Assert.InRange(MathF.Abs(expected.Y - actual.Y), 0.0f, Tolerance);
        Assert.InRange(MathF.Abs(expected.Z - actual.Z), 0.0f, Tolerance);
    }

    private static void AssertQuaternionNear(Quaternion expected, Quaternion actual)
    {
        float sign = Quaternion.Dot(expected, actual) < 0.0f ? -1.0f : 1.0f;

        Assert.InRange(MathF.Abs(expected.X - (actual.X * sign)), 0.0f, Tolerance);
        Assert.InRange(MathF.Abs(expected.Y - (actual.Y * sign)), 0.0f, Tolerance);
        Assert.InRange(MathF.Abs(expected.Z - (actual.Z * sign)), 0.0f, Tolerance);
        Assert.InRange(MathF.Abs(expected.W - (actual.W * sign)), 0.0f, Tolerance);
    }
}
