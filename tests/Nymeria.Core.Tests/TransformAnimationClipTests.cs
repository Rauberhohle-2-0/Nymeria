using System.Numerics;
using Nymeria.Core.Animation;
using Nymeria.Core.Scenes;

namespace Nymeria.Core.Tests;

public sealed class TransformAnimationClipTests
{
    private const float Tolerance = 1e-5f;

    [Fact]
    public void Apply_WritesOnlyAnimatedChannels()
    {
        var clip = new TransformAnimationClip(
            durationSeconds: 1.0f,
            translation: new Vector3Track(
            [
                new Vector3Keyframe(0.0f, Vector3.Zero),
                new Vector3Keyframe(1.0f, new Vector3(4.0f, 0.0f, 0.0f)),
            ]));
        var transform = new Transform(
            new Vector3(0.0f, 5.0f, 0.0f),
            Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0.3f),
            new Vector3(2.0f, 2.0f, 2.0f));

        clip.Apply(0.5f, transform);

        AssertVectorNear(new Vector3(2.0f, 0.0f, 0.0f), transform.Position);
        AssertQuaternionNear(
            Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0.3f),
            transform.Rotation);
        AssertVectorNear(new Vector3(2.0f, 2.0f, 2.0f), transform.Scale);
    }

    [Fact]
    public void Apply_WritesEveryAnimatedChannel()
    {
        var clip = new TransformAnimationClip(
            durationSeconds: 2.0f,
            translation: new Vector3Track([
                new Vector3Keyframe(0.0f, new Vector3(1.0f, 0.0f, 0.0f)),
            ]),
            rotation: new QuaternionTrack([
                new QuaternionKeyframe(0.0f, Quaternion.Identity),
            ]),
            scale: new Vector3Track([
                new Vector3Keyframe(0.0f, new Vector3(2.0f, 3.0f, 4.0f)),
            ]));
        var transform = new Transform();

        clip.Apply(1.0f, transform);

        AssertVectorNear(new Vector3(1.0f, 0.0f, 0.0f), transform.Position);
        AssertQuaternionNear(Quaternion.Identity, transform.Rotation);
        AssertVectorNear(new Vector3(2.0f, 3.0f, 4.0f), transform.Scale);
    }

    [Fact]
    public void Sample_ClampsEachTrackToItsOwnEndpoints()
    {
        var clip = new TransformAnimationClip(
            durationSeconds: 4.0f,
            translation: new Vector3Track(
            [
                new Vector3Keyframe(1.0f, Vector3.Zero),
                new Vector3Keyframe(2.0f, new Vector3(10.0f, 0.0f, 0.0f)),
            ]),
            scale: new Vector3Track([
                new Vector3Keyframe(0.0f, Vector3.One),
                new Vector3Keyframe(4.0f, new Vector3(3.0f, 3.0f, 3.0f)),
            ]));

        TransformPose start = clip.Sample(0.0f);
        TransformPose middle = clip.Sample(1.5f);
        TransformPose end = clip.Sample(4.0f);

        AssertVectorNear(Vector3.Zero, start.Translation!.Value);
        AssertVectorNear(Vector3.One, start.Scale!.Value);
        AssertVectorNear(new Vector3(5.0f, 0.0f, 0.0f), middle.Translation!.Value);
        AssertVectorNear(new Vector3(10.0f, 0.0f, 0.0f), end.Translation!.Value);
        AssertVectorNear(new Vector3(3.0f, 3.0f, 3.0f), end.Scale!.Value);
        Assert.False(start.HasRotation);
    }

    [Fact]
    public void Sample_ExactKeyframesReturnKeyValues()
    {
        var clip = new TransformAnimationClip(
            durationSeconds: 2.0f,
            scale: new Vector3Track(
            [
                new Vector3Keyframe(0.0f, Vector3.One),
                new Vector3Keyframe(0.5f, new Vector3(2.0f, 1.0f, 1.0f)),
                new Vector3Keyframe(2.0f, Vector3.One),
            ]));

        AssertVectorNear(new Vector3(2.0f, 1.0f, 1.0f), clip.Sample(0.5f).Scale!.Value);
    }

    [Fact]
    public void Constructor_RequiresANonEmptyAnimatedChannelSet()
    {
        Assert.Throws<ArgumentException>(() => new TransformAnimationClip(1.0f));
    }

    [Theory]
    [InlineData(-1.0f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(TransformAnimationClip.MaxDurationSeconds + 1.0f)]
    public void Constructor_RejectsInvalidDurations(float duration)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new TransformAnimationClip(
                duration,
                rotation: new QuaternionTrack([
                    new QuaternionKeyframe(0.0f, Quaternion.Identity),
                ])));
    }

    [Fact]
    public void Constructor_RejectsTracksBeyondTheDuration()
    {
        var translation = new Vector3Track([
            new Vector3Keyframe(0.0f, Vector3.Zero),
            new Vector3Keyframe(2.0f, Vector3.One),
        ]);

        Assert.Throws<ArgumentException>(
            () => new TransformAnimationClip(1.0f, translation: translation));
    }

    [Fact]
    public void ZeroDurationClip_SamplesOnlyTimeZero()
    {
        var clip = new TransformAnimationClip(
            durationSeconds: 0.0f,
            rotation: new QuaternionTrack([
                new QuaternionKeyframe(
                    0.0f,
                    Quaternion.CreateFromAxisAngle(Vector3.UnitX, 0.5f)),
            ]));

        TransformPose pose = clip.Sample(0.0f);

        Assert.True(pose.HasRotation);
        Assert.Throws<ArgumentOutOfRangeException>(() => clip.Sample(0.001f));
    }

    [Fact]
    public void ZeroDurationClip_RejectsLaterKeyframes()
    {
        var rotation = new QuaternionTrack([
            new QuaternionKeyframe(0.0f, Quaternion.Identity),
            new QuaternionKeyframe(1.0f, Quaternion.Identity),
        ]);

        Assert.Throws<ArgumentException>(
            () => new TransformAnimationClip(0.0f, rotation: rotation));
    }

    [Fact]
    public void Sample_RejectsTimesOutsideTheClip()
    {
        var clip = new TransformAnimationClip(
            1.0f,
            scale: new Vector3Track([new Vector3Keyframe(0.0f, Vector3.One)]));

        Assert.Throws<ArgumentOutOfRangeException>(() => clip.Sample(-0.1f));
        Assert.Throws<ArgumentOutOfRangeException>(() => clip.Sample(float.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => clip.Sample(1.1f));
    }

    [Fact]
    public void Apply_RejectsANullTarget()
    {
        var clip = new TransformAnimationClip(
            1.0f,
            scale: new Vector3Track([new Vector3Keyframe(0.0f, Vector3.One)]));

        Assert.Throws<ArgumentNullException>(() => clip.Apply(0.0f, null!));
    }

    [Fact]
    public void Name_IsOptionalAndBlankNamesBecomeNull()
    {
        var unnamed = new TransformAnimationClip(
            1.0f,
            scale: new Vector3Track([new Vector3Keyframe(0.0f, Vector3.One)]),
            name: "  ");
        var named = new TransformAnimationClip(
            1.0f,
            scale: new Vector3Track([new Vector3Keyframe(0.0f, Vector3.One)]),
            name: "door");

        Assert.Null(unnamed.Name);
        Assert.Equal("door", named.Name);
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
