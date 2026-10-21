using System.Numerics;
using Nymeria.Core.Collision;
using Nymeria.Core.Materials;
using Nymeria.Core.Models;
using Nymeria.Core.Scenes;

namespace HelloCollision;

/// <summary>
/// Builds the collision demo's deterministic scene and advances its animation.
/// </summary>
/// <remarks>
/// Every body uses a render mesh and a separate collision primitive chosen by the
/// example. Boxes use the same local bounds as the cube mesh; spheres use a unit
/// sphere collider. The player box is moved by the input loop and is not touched
/// by <see cref="UpdateAnimation"/>.
/// </remarks>
internal sealed class CollisionFixture
{
    public const float WorldHalfExtent = 10.0f;
    public const float PlayerSpeed = 8.0f;

    private static readonly BoundingBox UnitBox = new(
        new Vector3(-1.0f, -1.0f, -1.0f),
        new Vector3(1.0f, 1.0f, 1.0f));

    private static readonly BoundingSphere UnitSphere =
        new(Vector3.Zero, 1.0f);

    private readonly List<CollisionBody> _animatedBodies = new();
    private readonly CollisionBody _orbitBox;
    private readonly CollisionBody _orbitSphere;
    private readonly CollisionBody _bobBox;

    public CollisionFixture(Scene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);

        MeshData cube = CreateCubeMesh();
        MeshData sphere = CreateSphereMesh(segments: 20, rings: 10);
        Material[] materials = CreateMaterials();

        CollisionBody player = AddBox(
            scene,
            cube,
            materials[0],
            position: new Vector3(0.0f, 1.2f, -6.0f),
            scale: new Vector3(1.2f, 1.2f, 1.2f),
            label: "player");
        Player = player.SceneObject;

        AddBox(
            scene,
            cube,
            materials[1],
            position: new Vector3(0.0f, 1.5f, 0.0f),
            scale: new Vector3(1.8f, 1.5f, 1.8f),
            label: "center box");

        AddSphere(
            scene,
            sphere,
            materials[2],
            position: new Vector3(4.5f, 1.5f, 1.0f),
            scale: new Vector3(1.5f, 1.5f, 1.5f),
            label: "static sphere");

        _orbitBox = AddBox(
            scene,
            cube,
            materials[3],
            position: new Vector3(6.0f, 1.4f, 0.0f),
            scale: new Vector3(1.3f, 1.0f, 0.8f),
            label: "orbit box");
        _animatedBodies.Add(_orbitBox);

        _orbitSphere = AddSphere(
            scene,
            sphere,
            materials[4],
            position: new Vector3(-6.0f, 1.4f, 0.0f),
            scale: new Vector3(1.2f),
            label: "orbit sphere");
        _animatedBodies.Add(_orbitSphere);

        _bobBox = AddBox(
            scene,
            cube,
            materials[5],
            position: new Vector3(-4.0f, 1.0f, 3.0f),
            scale: new Vector3(0.7f, 1.8f, 0.7f),
            label: "bobbing box");
        _animatedBodies.Add(_bobBox);

        HighlightMaterial = new Material(
            new Vector3(1.0f, 0.84f, 0.16f),
            null,
            "collision");
    }

    public SceneObject Player { get; }

    public Material HighlightMaterial { get; }

    public IReadOnlyList<CollisionBody> AnimatedBodies => _animatedBodies;

    public void UpdateAnimation(double elapsedSeconds)
    {
        if (!double.IsFinite(elapsedSeconds) || elapsedSeconds < 0.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(elapsedSeconds),
                elapsedSeconds,
                "Animation time must be finite and nonnegative.");
        }

        float time = (float)elapsedSeconds;

        _orbitBox.SceneObject.Transform.Position = new Vector3(
            MathF.Sin(time * 0.65f) * 6.5f,
            1.4f,
            MathF.Cos(time * 0.65f) * 3.5f);
        _orbitBox.SceneObject.Transform.Rotation =
            Quaternion.CreateFromYawPitchRoll(time * 0.7f, time * 0.35f, 0.0f);

        _orbitSphere.SceneObject.Transform.Position = new Vector3(
            MathF.Cos(time * 0.9f) * 6.0f,
            1.4f + (MathF.Sin(time * 1.3f) * 0.5f),
            MathF.Sin(time * 0.9f) * 4.5f);

        _bobBox.SceneObject.Transform.Position = new Vector3(
            -4.0f,
            1.0f + (MathF.Sin(time * 1.4f) * 1.5f),
            3.0f);
    }

    public void ResetPlayer()
    {
        Player.Transform.Position = new Vector3(0.0f, 1.2f, -6.0f);
        Player.Transform.Rotation = Quaternion.Identity;
    }

    public void MovePlayer(Vector3 direction, float deltaSeconds)
    {
        if (!float.IsFinite(deltaSeconds) || deltaSeconds <= 0.0f)
        {
            return;
        }

        Vector3 position = Player.Transform.Position + (direction * PlayerSpeed * deltaSeconds);
        Player.Transform.Position = new Vector3(
            Math.Clamp(position.X, -WorldHalfExtent, WorldHalfExtent),
            position.Y,
            Math.Clamp(position.Z, -WorldHalfExtent, WorldHalfExtent));
    }

    private static CollisionBody AddBox(
        Scene scene,
        MeshData mesh,
        Material material,
        Vector3 position,
        Vector3 scale,
        string label)
    {
        var sceneObject = new SceneObject(
            mesh,
            new Transform(position, Quaternion.Identity, scale),
            material)
        {
            Collider = CollisionShape.FromBox(UnitBox),
        };
        scene.Add(sceneObject);
        return new CollisionBody(sceneObject, material, label);
    }

    private static CollisionBody AddSphere(
        Scene scene,
        MeshData mesh,
        Material material,
        Vector3 position,
        Vector3 scale,
        string label)
    {
        var sceneObject = new SceneObject(
            mesh,
            new Transform(position, Quaternion.Identity, scale),
            material)
        {
            Collider = CollisionShape.FromSphere(UnitSphere),
        };
        scene.Add(sceneObject);
        return new CollisionBody(sceneObject, material, label);
    }

    private static Material[] CreateMaterials()
    {
        return
        [
            new Material(new Vector3(0.20f, 0.80f, 0.95f), null, "player"),
            new Material(new Vector3(0.90f, 0.32f, 0.22f), null, "center box"),
            new Material(new Vector3(0.30f, 0.80f, 0.42f), null, "static sphere"),
            new Material(new Vector3(0.92f, 0.76f, 0.24f), null, "orbit box"),
            new Material(new Vector3(0.72f, 0.38f, 0.88f), null, "orbit sphere"),
            new Material(new Vector3(0.80f, 0.48f, 0.24f), null, "bobbing box"),
        ];
    }

    private static MeshData CreateCubeMesh()
    {
        const float half = 1.0f;

        Vector3[] positions =
        [
            new(-half, -half,  half),
            new( half, -half,  half),
            new( half,  half,  half),
            new(-half,  half,  half),

            new( half, -half, -half),
            new(-half, -half, -half),
            new(-half,  half, -half),
            new( half,  half, -half),

            new( half, -half,  half),
            new( half, -half, -half),
            new( half,  half, -half),
            new( half,  half,  half),

            new(-half, -half, -half),
            new(-half, -half,  half),
            new(-half,  half,  half),
            new(-half,  half, -half),

            new(-half,  half,  half),
            new( half,  half,  half),
            new( half,  half, -half),
            new(-half,  half, -half),

            new(-half, -half, -half),
            new( half, -half, -half),
            new( half, -half,  half),
            new(-half, -half,  half),
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

        return new MeshData(positions, normals, textureCoordinates, indices);
    }

    private static MeshData CreateSphereMesh(int segments, int rings)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(segments, 3);
        ArgumentOutOfRangeException.ThrowIfLessThan(rings, 2);

        int vertexCount = (rings + 1) * segments;
        var positions = new Vector3[vertexCount];
        var normals = new Vector3[vertexCount];
        var textureCoordinates = new Vector2[vertexCount];
        int vertex = 0;

        for (int ring = 0; ring <= rings; ring++)
        {
            float latitude = (-MathF.PI / 2.0f) + (MathF.PI * ring / rings);
            float y = MathF.Sin(latitude);
            float radius = MathF.Cos(latitude);
            float v = 1.0f - ((float)ring / rings);

            for (int segment = 0; segment < segments; segment++)
            {
                float longitude = MathF.Tau * segment / segments;
                var position = new Vector3(
                    MathF.Cos(longitude) * radius,
                    y,
                    MathF.Sin(longitude) * radius);
                positions[vertex] = position;
                normals[vertex] = position;
                textureCoordinates[vertex] = new Vector2(
                    (float)segment / segments,
                    v);
                vertex++;
            }
        }

        var indices = new uint[rings * segments * 6];
        int write = 0;

        for (int ring = 0; ring < rings; ring++)
        {
            for (int segment = 0; segment < segments; segment++)
            {
                uint topLeft = (uint)((ring * segments) + segment);
                uint topRight = (uint)((ring * segments) + ((segment + 1) % segments));
                uint bottomLeft = (uint)(((ring + 1) * segments) + segment);
                uint bottomRight = (uint)(((ring + 1) * segments) + ((segment + 1) % segments));

                indices[write++] = topLeft;
                indices[write++] = bottomLeft;
                indices[write++] = topRight;

                indices[write++] = topRight;
                indices[write++] = bottomLeft;
                indices[write++] = bottomRight;
            }
        }

        return new MeshData(positions, normals, textureCoordinates, indices);
    }
}

/// <summary>
/// One named example body and its original material.
/// </summary>
internal sealed class CollisionBody
{
    public CollisionBody(SceneObject sceneObject, Material baseMaterial, string label)
    {
        SceneObject = sceneObject;
        BaseMaterial = baseMaterial;
        Label = label;
    }

    public SceneObject SceneObject { get; }

    public Material BaseMaterial { get; }

    public string Label { get; }
}
