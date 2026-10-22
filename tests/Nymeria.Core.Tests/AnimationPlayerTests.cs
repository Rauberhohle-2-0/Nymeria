using System.Numerics;
using Nymeria.Core.Animation;
using Nymeria.Core.Models;
using Nymeria.Core.Scenes;

namespace Nymeria.Core.Tests;

public sealed class AnimationPlayerTests
{
    private const float Tolerance = 1e-5f;

    [Fact]
    public void Play_AppliesStartPoseAndUpdateAdvances()
    {
        var transform = new Transform();
        var player = new AnimationPlayer(CreateTranslationClip(2.0f, 10.0f), transform);

        player.Play();

        Assert.Equal(AnimationPlaybackState.Playing, player.State);
        AssertVectorNear(Vector3.Zero, transform.Position);

        player.Update(0.5f);

        Assert.Equal(0.5f, player.Time, 5);
        AssertVectorNear(new Vector3(2.5f, 0.0f, 0.0f), transform.Position);
    }

    [Fact]
    public void Pause_Resume_And_Stop_FollowTheDocumentedTransitions()
    {
        var transform = new Transform();
        var player = new AnimationPlayer(CreateTranslationClip(2.0f, 10.0f), transform);

        Assert.Throws<InvalidOperationException>(player.Pause);
        Assert.Throws<InvalidOperationException>(player.Resume);

        player.Play();
        player.Update(0.5f);
        player.Pause();

        Assert.Equal(AnimationPlaybackState.Paused, player.State);
        Assert.Throws<InvalidOperationException>(player.Play);

        player.Update(3.0f);

        Assert.Equal(0.5f, player.Time, 5);
        AssertVectorNear(new Vector3(2.5f, 0.0f, 0.0f), transform.Position);

        player.Resume();
        player.Update(0.25f);

        Assert.Equal(0.75f, player.Time, 5);
        AssertVectorNear(new Vector3(3.75f, 0.0f, 0.0f), transform.Position);

        player.Stop();
        player.Stop();

        Assert.Equal(AnimationPlaybackState.Stopped, player.State);
        Assert.Equal(0.0f, player.Time);
        AssertVectorNear(Vector3.Zero, transform.Position);
    }

    [Fact]
    public void NonLoopingPlayback_CompletesAtTheDuration()
    {
        var transform = new Transform();
        var player = new AnimationPlayer(CreateTranslationClip(1.0f, 10.0f), transform);

        player.Play();
        player.Update(5.0f);

        Assert.True(player.IsCompleted);
        Assert.Equal(1.0f, player.Time);
        AssertVectorNear(new Vector3(10.0f, 0.0f, 0.0f), transform.Position);

        player.Update(1.0f);

        Assert.Equal(1.0f, player.Time);

        player.Restart();

        Assert.True(player.IsPlaying);
        Assert.Equal(0.0f, player.Time);
        AssertVectorNear(Vector3.Zero, transform.Position);
    }

    [Fact]
    public void LoopingPlayback_WrapsWithOneModuloAndNeverCompletes()
    {
        var transform = new Transform();
        var player = new AnimationPlayer(CreateTranslationClip(1.0f, 10.0f), transform)
        {
            IsLooping = true,
        };

        player.Play();
        player.Update(0.25f);
        player.Update(2.5f);

        Assert.Equal(0.75f, player.Time, 5);
        Assert.True(player.IsPlaying);
        AssertVectorNear(new Vector3(7.5f, 0.0f, 0.0f), transform.Position);

        player.Update(10_000.25f);

        Assert.Equal(0.0f, player.Time, 5);
        Assert.True(player.IsPlaying);
        AssertVectorNear(Vector3.Zero, transform.Position);
    }

    [Fact]
    public void DeltaPartitioning_IsDeterministic()
    {
        var whole = new Transform();
        var parted = new Transform();
        var wholePlayer = new AnimationPlayer(CreateTranslationClip(2.0f, 8.0f), whole);
        var partedPlayer = new AnimationPlayer(CreateTranslationClip(2.0f, 8.0f), parted);

        wholePlayer.Play();
        partedPlayer.Play();
        wholePlayer.Update(1.0f);

        for (int index = 0; index < 4; index++)
        {
            partedPlayer.Update(0.25f);
        }

        Assert.Equal(wholePlayer.Time, partedPlayer.Time, 5);
        AssertVectorNear(whole.Position, parted.Position);
    }

    [Fact]
    public void ZeroSpeed_FreezesTimeWithoutCompleting()
    {
        var transform = new Transform();
        var player = new AnimationPlayer(CreateTranslationClip(1.0f, 10.0f), transform)
        {
            Speed = 0.0f,
        };

        player.Play();
        player.Update(0.5f);

        Assert.True(player.IsPlaying);
        Assert.Equal(0.0f, player.Time);
        AssertVectorNear(Vector3.Zero, transform.Position);
    }

    [Fact]
    public void UpdateAndSpeed_RejectInvalidValues()
    {
        var player = new AnimationPlayer(
            CreateTranslationClip(1.0f, 1.0f),
            new Transform());
        player.Play();

        Assert.Throws<ArgumentOutOfRangeException>(() => player.Update(-0.1f));
        Assert.Throws<ArgumentOutOfRangeException>(() => player.Update(float.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => player.Update(float.PositiveInfinity));
        Assert.Throws<ArgumentOutOfRangeException>(() => player.Speed = -1.0f);
        Assert.Throws<ArgumentOutOfRangeException>(() => player.Speed = float.NaN);
    }

    [Fact]
    public void UpdateWhileStoppedPausedOrCompleted_DoesNotChangeTheTarget()
    {
        var transform = new Transform();
        var player = new AnimationPlayer(CreateTranslationClip(1.0f, 10.0f), transform);

        player.Update(1.0f);
        AssertVectorNear(Vector3.Zero, transform.Position);

        player.Play();
        player.Update(0.5f);
        player.Pause();
        transform.Position = new Vector3(99.0f, 0.0f, 0.0f);
        player.Update(0.5f);

        AssertVectorNear(new Vector3(99.0f, 0.0f, 0.0f), transform.Position);

        player.Resume();
        player.Update(1.0f);
        Assert.True(player.IsCompleted);
        transform.Position = new Vector3(42.0f, 0.0f, 0.0f);
        player.Update(1.0f);

        AssertVectorNear(new Vector3(42.0f, 0.0f, 0.0f), transform.Position);
    }

    [Fact]
    public void ZeroDurationClip_LoopsAtZeroOrCompletesImmediately()
    {
        var looping = new AnimationPlayer(CreateZeroDurationClip(), new Transform())
        {
            IsLooping = true,
        };
        looping.Play();
        looping.Update(10.0f);

        Assert.True(looping.IsPlaying);
        Assert.Equal(0.0f, looping.Time);

        var completing = new AnimationPlayer(CreateZeroDurationClip(), new Transform());
        completing.Play();
        completing.Update(0.0f);

        Assert.True(completing.IsCompleted);
        Assert.Equal(0.0f, completing.Time);
    }

    [Fact]
    public void TwoPlayersOnOneTransform_LastUpdateWins()
    {
        var transform = new Transform();
        var first = new AnimationPlayer(CreateTranslationClip(1.0f, 1.0f), transform);
        var second = new AnimationPlayer(CreateTranslationClip(1.0f, 5.0f), transform);

        first.Play();
        second.Play();
        first.Update(0.5f);
        second.Update(0.25f);

        AssertVectorNear(new Vector3(1.25f, 0.0f, 0.0f), transform.Position);

        first.Update(0.25f);

        AssertVectorNear(new Vector3(0.75f, 0.0f, 0.0f), transform.Position);
    }

    [Fact]
    public void SceneObjectTarget_IsBoundByReferenceAndSurvivesRemoval()
    {
        var scene = new Scene();
        var sceneObject = new SceneObject(CreateTriangleMesh());
        scene.Add(sceneObject);

        var player = new AnimationPlayer(CreateTranslationClip(1.0f, 4.0f), sceneObject);

        Assert.Same(sceneObject, player.TargetObject);
        Assert.Same(sceneObject.Transform, player.TargetTransform);

        player.Play();
        player.Update(0.5f);

        AssertVectorNear(new Vector3(2.0f, 0.0f, 0.0f), sceneObject.Transform.Position);

        Assert.True(scene.Remove(sceneObject));
        player.Update(0.25f);

        AssertVectorNear(new Vector3(3.0f, 0.0f, 0.0f), sceneObject.Transform.Position);
        Assert.Equal(0, scene.Count);
    }

    [Fact]
    public void ModelMatrixReflectsTheSampledTransform()
    {
        var transform = new Transform();
        var player = new AnimationPlayer(CreateTranslationClip(1.0f, 2.0f), transform);

        player.Play();
        player.Update(0.5f);

        Matrix4x4 model = transform.CreateModelMatrix();

        Assert.Equal(1.0f, model.M41, 5);
        Assert.Equal(0.0f, model.M42, 5);
        Assert.Equal(0.0f, model.M43, 5);
    }

    [Fact]
    public void Constructor_RejectsNullArguments()
    {
        var clip = CreateTranslationClip(1.0f, 1.0f);

        Assert.Throws<ArgumentNullException>(() => new AnimationPlayer(null!, new Transform()));
        Assert.Throws<ArgumentNullException>(() => new AnimationPlayer(clip, (Transform)null!));
        Assert.Throws<ArgumentNullException>(
            () => new AnimationPlayer(clip, (SceneObject)null!));
    }

    private static TransformAnimationClip CreateTranslationClip(
        float durationSeconds,
        float distance) =>
        new(
            durationSeconds,
            translation: new Vector3Track(
            [
                new Vector3Keyframe(0.0f, Vector3.Zero),
                new Vector3Keyframe(durationSeconds, new Vector3(distance, 0.0f, 0.0f)),
            ]));

    private static TransformAnimationClip CreateZeroDurationClip() =>
        new(
            0.0f,
            scale: new Vector3Track([
                new Vector3Keyframe(0.0f, Vector3.One),
            ]));

    private static MeshData CreateTriangleMesh() =>
        new(
            new Vector3[] { Vector3.Zero, Vector3.UnitX, Vector3.UnitY },
            new Vector3[] { Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ },
            new Vector2[] { Vector2.Zero, Vector2.UnitX, Vector2.UnitY },
            new uint[] { 0u, 1u, 2u });

    private static void AssertVectorNear(Vector3 expected, Vector3 actual)
    {
        Assert.InRange(MathF.Abs(expected.X - actual.X), 0.0f, Tolerance);
        Assert.InRange(MathF.Abs(expected.Y - actual.Y), 0.0f, Tolerance);
        Assert.InRange(MathF.Abs(expected.Z - actual.Z), 0.0f, Tolerance);
    }
}
