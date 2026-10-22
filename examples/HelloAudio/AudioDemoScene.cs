using System.Numerics;
using Nymeria.Core.Materials;
using Nymeria.Core.Models;
using Nymeria.Core.Scenes;

namespace HelloAudio;

/// <summary>
/// Builds the visual reference for the example: a ground plane and one marker
/// per spatial source.
/// </summary>
internal sealed class AudioDemoScene
{
    public static readonly Vector3 SourceAPosition = new(-5.0f, 1.0f, -2.0f);

    public static readonly Vector3 SourceBPosition = new(5.0f, 1.0f, -4.0f);

    public AudioDemoScene()
    {
        MeshData cube = CreateCubeMesh();

        var groundMaterial = new Material(
            new Vector3(0.14f, 0.16f, 0.20f),
            null,
            "ground");
        var sourceAMaterial = new Material(
            new Vector3(0.95f, 0.36f, 0.24f),
            null,
            "source A");
        var sourceBMaterial = new Material(
            new Vector3(0.26f, 0.62f, 0.95f),
            null,
            "source B");

        Scene = new Scene();
        Scene.Add(new SceneObject(
            cube,
            new Transform(
                new Vector3(0.0f, -0.06f, 0.0f),
                Quaternion.Identity,
                new Vector3(30.0f, 0.05f, 30.0f)),
            groundMaterial));
        Scene.Add(new SceneObject(
            cube,
            new Transform(
                SourceAPosition,
                Quaternion.Identity,
                new Vector3(0.9f, 0.9f, 0.9f)),
            sourceAMaterial));
        Scene.Add(new SceneObject(
            cube,
            new Transform(
                SourceBPosition,
                Quaternion.Identity,
                new Vector3(0.9f, 0.9f, 0.9f)),
            sourceBMaterial));
    }

    public Scene Scene { get; }

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
}
