using System.Numerics;
using Nymeria.Core.Animation;
using Nymeria.Core.Lighting;
using Nymeria.Core.Materials;
using Nymeria.Core.Models;
using Nymeria.Core.Scenes;

namespace HelloAnimation;

/// <summary>
/// Builds the example scene and owns its animation clips and players.
/// </summary>
/// <remarks>
/// The door and wiper meshes are authored with their local origin at the hinge
/// and base. Rotating the scene object's own transform therefore rotates the
/// geometry around that pivot, so no parent object or extra scene-graph system
/// is needed.
/// </remarks>
internal sealed class AnimationFixture
{
    private const float DoorBaseSpeed = 0.8f;
    private const float WiperBaseSpeed = 1.4f;
    private const float MoverBaseSpeed = 1.0f;
    private const float MinimumSpeedMultiplier = 0.25f;
    private const float MaximumSpeedMultiplier = 3.0f;

    private float _speedMultiplier = 1.0f;

    public AnimationFixture()
    {
        MeshData unitCube = CreateBoxMesh(
            new Vector3(-0.5f, -0.5f, -0.5f),
            new Vector3(0.5f, 0.5f, 0.5f));
        MeshData doorMesh = CreateBoxMesh(
            new Vector3(0.0f, 0.0f, -0.06f),
            new Vector3(1.2f, 2.0f, 0.06f));
        MeshData wiperMesh = CreateBoxMesh(
            new Vector3(-0.05f, 0.0f, -0.03f),
            new Vector3(0.05f, 1.4f, 0.03f));

        var groundMaterial = new Material(new Vector3(0.16f, 0.18f, 0.22f), null, "ground");
        var frameMaterial = new Material(new Vector3(0.36f, 0.30f, 0.24f), null, "frame");
        var doorMaterial = new Material(new Vector3(0.78f, 0.48f, 0.24f), null, "door");
        var wiperMaterial = new Material(new Vector3(0.24f, 0.26f, 0.32f), null, "wiper");
        var moverMaterial = new Material(new Vector3(0.22f, 0.68f, 0.62f), null, "mover");
        var pivotMaterial = new Material(new Vector3(0.95f, 0.82f, 0.30f), null, "pivot");

        Scene = new Scene
        {
            Lighting = new SceneLighting(
                new AmbientLight(Vector3.One, 0.35f),
                new DirectionalLight(
                    new Vector3(0.45f, 1.0f, 0.55f),
                    Vector3.One,
                    0.9f)),
        };

        // A thin slab under everything gives the motions a visual ground plane.
        AddStatic(
            unitCube,
            new Transform(
                new Vector3(0.0f, -0.05f, 0.0f),
                Quaternion.Identity,
                new Vector3(16.0f, 0.1f, 16.0f)),
            groundMaterial);

        // Door frame posts mark the hinge and the latch side without moving.
        AddStatic(
            unitCube,
            new Transform(
                new Vector3(-2.25f, 1.0f, 0.5f),
                Quaternion.Identity,
                new Vector3(0.1f, 2.1f, 0.2f)),
            frameMaterial);
        AddStatic(
            unitCube,
            new Transform(
                new Vector3(-1.0f, 1.0f, 0.5f),
                Quaternion.Identity,
                new Vector3(0.1f, 2.1f, 0.2f)),
            frameMaterial);

        // The pivot markers sit exactly on the local origins used by the
        // rotation tracks: the door hinge and the wiper base.
        AddStatic(
            unitCube,
            new Transform(
                new Vector3(-2.2f, 0.05f, 0.5f),
                Quaternion.Identity,
                new Vector3(0.12f, 0.12f, 0.12f)),
            pivotMaterial);
        AddStatic(
            unitCube,
            new Transform(
                new Vector3(1.8f, 0.25f, 0.5f),
                Quaternion.Identity,
                new Vector3(0.12f, 0.12f, 0.12f)),
            pivotMaterial);

        var doorObject = new SceneObject(
            doorMesh,
            new Transform(
                new Vector3(-2.2f, 0.0f, 0.5f),
                Quaternion.Identity,
                Vector3.One),
            doorMaterial);
        var wiperObject = new SceneObject(
            wiperMesh,
            new Transform(
                new Vector3(1.8f, 0.2f, 0.5f),
                Quaternion.Identity,
                Vector3.One),
            wiperMaterial);
        var moverObject = new SceneObject(
            unitCube,
            new Transform(
                new Vector3(0.0f, 1.2f, -2.6f),
                Quaternion.CreateFromYawPitchRoll(0.4f, 0.2f, 0.0f),
                new Vector3(0.7f, 0.7f, 0.7f)),
            moverMaterial);

        Scene.Add(doorObject);
        Scene.Add(wiperObject);
        Scene.Add(moverObject);

        var doorClip = new TransformAnimationClip(
            durationSeconds: 4.0f,
            rotation: new QuaternionTrack(
            [
                new QuaternionKeyframe(0.0f, Quaternion.Identity),
                new QuaternionKeyframe(
                    1.5f,
                    Quaternion.CreateFromAxisAngle(Vector3.UnitY, 1.9f)),
                new QuaternionKeyframe(
                    2.6f,
                    Quaternion.CreateFromAxisAngle(Vector3.UnitY, 1.9f)),
                new QuaternionKeyframe(4.0f, Quaternion.Identity),
            ]),
            name: "door");

        var wiperClip = new TransformAnimationClip(
            durationSeconds: 1.6f,
            rotation: new QuaternionTrack(
            [
                new QuaternionKeyframe(
                    0.0f,
                    Quaternion.CreateFromAxisAngle(Vector3.UnitZ, -0.65f)),
                new QuaternionKeyframe(
                    0.8f,
                    Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0.65f)),
                new QuaternionKeyframe(
                    1.6f,
                    Quaternion.CreateFromAxisAngle(Vector3.UnitZ, -0.65f)),
            ]),
            name: "wiper");

        var moverClip = new TransformAnimationClip(
            durationSeconds: 3.0f,
            translation: new Vector3Track(
            [
                new Vector3Keyframe(0.0f, new Vector3(0.0f, 1.2f, -2.6f)),
                new Vector3Keyframe(1.5f, new Vector3(0.0f, 2.6f, -3.4f)),
                new Vector3Keyframe(3.0f, new Vector3(0.0f, 1.2f, -2.6f)),
            ]),
            scale: new Vector3Track(
            [
                new Vector3Keyframe(0.0f, new Vector3(0.7f, 0.7f, 0.7f)),
                new Vector3Keyframe(1.5f, new Vector3(1.5f, 1.5f, 1.5f)),
                new Vector3Keyframe(3.0f, new Vector3(0.7f, 0.7f, 0.7f)),
            ]),
            name: "mover");

        Door = new AnimationPlayer(doorClip, doorObject)
        {
            IsLooping = true,
            Speed = DoorBaseSpeed,
        };
        Wiper = new AnimationPlayer(wiperClip, wiperObject)
        {
            IsLooping = true,
            Speed = WiperBaseSpeed,
        };
        Mover = new AnimationPlayer(moverClip, moverObject)
        {
            IsLooping = true,
            Speed = MoverBaseSpeed,
        };

        Players = [Door, Wiper, Mover];

        foreach (AnimationPlayer player in Players)
        {
            player.Play();
        }
    }

    public Scene Scene { get; }

    public AnimationPlayer Door { get; }

    public AnimationPlayer Wiper { get; }

    public AnimationPlayer Mover { get; }

    public IReadOnlyList<AnimationPlayer> Players { get; }

    public float SpeedMultiplier => _speedMultiplier;

    public bool IsLooping { get; private set; } = true;

    public bool IsPaused { get; private set; }

    public void Update(float deltaSeconds)
    {
        foreach (AnimationPlayer player in Players)
        {
            player.Update(deltaSeconds);
        }
    }

    /// <summary>Pauses every playing animation, or resumes every paused one.</summary>
    public bool TogglePause()
    {
        if (IsPaused)
        {
            foreach (AnimationPlayer player in Players)
            {
                if (player.State == AnimationPlaybackState.Paused)
                {
                    player.Resume();
                }
            }

            IsPaused = false;
        }
        else
        {
            foreach (AnimationPlayer player in Players)
            {
                if (player.State == AnimationPlaybackState.Playing)
                {
                    player.Pause();
                }
            }

            IsPaused = true;
        }

        return IsPaused;
    }

    /// <summary>Restarts every animation at time zero and resumes playback.</summary>
    public void RestartAll()
    {
        foreach (AnimationPlayer player in Players)
        {
            player.Restart();
        }

        IsPaused = false;
    }

    public bool ToggleLooping()
    {
        IsLooping = !IsLooping;

        foreach (AnimationPlayer player in Players)
        {
            player.IsLooping = IsLooping;
        }

        return IsLooping;
    }

    public float AdjustSpeed(float delta)
    {
        _speedMultiplier = Math.Clamp(
            _speedMultiplier + delta,
            MinimumSpeedMultiplier,
            MaximumSpeedMultiplier);

        Door.Speed = DoorBaseSpeed * _speedMultiplier;
        Wiper.Speed = WiperBaseSpeed * _speedMultiplier;
        Mover.Speed = MoverBaseSpeed * _speedMultiplier;

        return _speedMultiplier;
    }

    /// <summary>Gets the door's Y rotation in degrees from its current transform.</summary>
    public float DoorAngleDegrees =>
        RadiansToDegrees(2.0f * MathF.Atan2(Door.TargetTransform.Rotation.Y, Door.TargetTransform.Rotation.W));

    /// <summary>Gets the wiper's Z rotation in degrees from its current transform.</summary>
    public float WiperAngleDegrees =>
        RadiansToDegrees(2.0f * MathF.Atan2(Wiper.TargetTransform.Rotation.Z, Wiper.TargetTransform.Rotation.W));

    private void AddStatic(MeshData mesh, Transform transform, Material material) =>
        Scene.Add(new SceneObject(mesh, transform, material));

    private static float RadiansToDegrees(float radians) =>
        radians * (180.0f / MathF.PI);

    private static MeshData CreateBoxMesh(Vector3 min, Vector3 max)
    {
        Vector3[] positions =
        [
            new(min.X, min.Y, max.Z), new(max.X, min.Y, max.Z),
            new(max.X, max.Y, max.Z), new(min.X, max.Y, max.Z),

            new(max.X, min.Y, min.Z), new(min.X, min.Y, min.Z),
            new(min.X, max.Y, min.Z), new(max.X, max.Y, min.Z),

            new(max.X, min.Y, max.Z), new(max.X, min.Y, min.Z),
            new(max.X, max.Y, min.Z), new(max.X, max.Y, max.Z),

            new(min.X, min.Y, min.Z), new(min.X, min.Y, max.Z),
            new(min.X, max.Y, max.Z), new(min.X, max.Y, min.Z),

            new(min.X, max.Y, max.Z), new(max.X, max.Y, max.Z),
            new(max.X, max.Y, min.Z), new(min.X, max.Y, min.Z),

            new(min.X, min.Y, min.Z), new(max.X, min.Y, min.Z),
            new(max.X, min.Y, max.Z), new(min.X, min.Y, max.Z),
        ];

        Vector3[] normals =
        [
            Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ,
            -Vector3.UnitZ, -Vector3.UnitZ, -Vector3.UnitZ, -Vector3.UnitZ,
            Vector3.UnitX, Vector3.UnitX, Vector3.UnitX, Vector3.UnitX,
            -Vector3.UnitX, -Vector3.UnitX, -Vector3.UnitX, -Vector3.UnitX,
            Vector3.UnitY, Vector3.UnitY, Vector3.UnitY, Vector3.UnitY,
            -Vector3.UnitY, -Vector3.UnitY, -Vector3.UnitY, -Vector3.UnitY,
        ];

        Vector2[] textureCoordinates =
        [
            new(0.0f, 1.0f), new(1.0f, 1.0f), new(1.0f, 0.0f), new(0.0f, 0.0f),
            new(0.0f, 1.0f), new(1.0f, 1.0f), new(1.0f, 0.0f), new(0.0f, 0.0f),
            new(0.0f, 1.0f), new(1.0f, 1.0f), new(1.0f, 0.0f), new(0.0f, 0.0f),
            new(0.0f, 1.0f), new(1.0f, 1.0f), new(1.0f, 0.0f), new(0.0f, 0.0f),
            new(0.0f, 1.0f), new(1.0f, 1.0f), new(1.0f, 0.0f), new(0.0f, 0.0f),
            new(0.0f, 1.0f), new(1.0f, 1.0f), new(1.0f, 0.0f), new(0.0f, 0.0f),
        ];

        uint[] indices =
        [
             0,  1,  2,   0,  2,  3,
             4,  5,  6,   4,  6,  7,
             8,  9, 10,   8, 10, 11,
            12, 13, 14,  12, 14, 15,
            16, 17, 18,  16, 18, 19,
            20, 21, 22,  20, 22, 23,
        ];

        return new MeshData(
            new ReadOnlyMemory<Vector3>(positions),
            new ReadOnlyMemory<Vector3>(normals),
            new ReadOnlyMemory<Vector2>(textureCoordinates),
            new ReadOnlyMemory<uint>(indices));
    }
}
