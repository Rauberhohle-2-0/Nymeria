using System.Numerics;
using Nymeria.Core.Materials;
using Nymeria.Core.Models;
using Nymeria.Core.Scenes;

namespace HelloBenchmark;

/// <summary>
/// Builds the deterministic benchmark scene: one shared cube mesh, four shared
/// untextured materials, and a reproducible instance count.
/// </summary>
internal static class BenchmarkScene
{
    private const float HalfExtent = 1.0f;

    private static readonly Vector3[] s_positions =
    {
        new(-HalfExtent, -HalfExtent,  HalfExtent),
        new( HalfExtent, -HalfExtent,  HalfExtent),
        new( HalfExtent,  HalfExtent,  HalfExtent),
        new(-HalfExtent,  HalfExtent,  HalfExtent),

        new( HalfExtent, -HalfExtent, -HalfExtent),
        new(-HalfExtent, -HalfExtent, -HalfExtent),
        new(-HalfExtent,  HalfExtent, -HalfExtent),
        new( HalfExtent,  HalfExtent, -HalfExtent),

        new( HalfExtent, -HalfExtent,  HalfExtent),
        new( HalfExtent, -HalfExtent, -HalfExtent),
        new( HalfExtent,  HalfExtent, -HalfExtent),
        new( HalfExtent,  HalfExtent,  HalfExtent),

        new(-HalfExtent, -HalfExtent, -HalfExtent),
        new(-HalfExtent, -HalfExtent,  HalfExtent),
        new(-HalfExtent,  HalfExtent,  HalfExtent),
        new(-HalfExtent,  HalfExtent, -HalfExtent),

        new(-HalfExtent,  HalfExtent,  HalfExtent),
        new( HalfExtent,  HalfExtent,  HalfExtent),
        new( HalfExtent,  HalfExtent, -HalfExtent),
        new(-HalfExtent,  HalfExtent, -HalfExtent),

        new(-HalfExtent, -HalfExtent, -HalfExtent),
        new( HalfExtent, -HalfExtent, -HalfExtent),
        new( HalfExtent, -HalfExtent,  HalfExtent),
        new(-HalfExtent, -HalfExtent,  HalfExtent),
    };

    private static readonly Vector3[] s_normals =
    {
        Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ,
        -Vector3.UnitZ, -Vector3.UnitZ, -Vector3.UnitZ, -Vector3.UnitZ,
        Vector3.UnitX, Vector3.UnitX, Vector3.UnitX, Vector3.UnitX,
        -Vector3.UnitX, -Vector3.UnitX, -Vector3.UnitX, -Vector3.UnitX,
        Vector3.UnitY, Vector3.UnitY, Vector3.UnitY, Vector3.UnitY,
        -Vector3.UnitY, -Vector3.UnitY, -Vector3.UnitY, -Vector3.UnitY,
    };

    private static readonly uint[] s_indices =
    {
         0,  1,  2,   0,  2,  3,
         4,  5,  6,   4,  6,  7,
         8,  9, 10,   8, 10, 11,
        12, 13, 14,  12, 14, 15,
        16, 17, 18,  16, 18, 19,
        20, 21, 22,  20, 22, 23,
    };

    public static (Scene Scene, int MaterialCount) Create(int instanceCount)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(instanceCount, 1);

        MeshData mesh = CreateCubeMesh();
        Material[] materials =
        [
            new Material(new Vector3(0.90f, 0.32f, 0.22f), null, "benchmark red"),
            new Material(new Vector3(0.22f, 0.55f, 0.92f), null, "benchmark blue"),
            new Material(new Vector3(0.30f, 0.80f, 0.42f), null, "benchmark green"),
            new Material(new Vector3(0.92f, 0.76f, 0.24f), null, "benchmark yellow"),
        ];

        var scene = new Scene(instanceCount);

        // A fixed fraction sits behind the camera's initial -Z view. The rest
        // form a deterministic grid that grows away from the camera and past
        // the side and far planes as the instance count increases.
        int behindCount = Math.Max(1, instanceCount / 16);
        int gridCount = instanceCount - behindCount;

        for (int i = 0; i < behindCount; i++)
        {
            scene.Add(CreateObject(
                mesh,
                materials[i % materials.Length],
                new Vector3(
                    ((i % 16) - 7.5f) * 4.0f,
                    1.0f + (i % 2),
                    30.0f + ((i / 16) % 3) * 2.0f),
                yawRadians: i * 0.17f,
                scale: Vector3.One));
        }

        if (gridCount > 0)
        {
            int columns = (int)Math.Ceiling(Math.Sqrt(gridCount));

            for (int i = 0; i < gridCount; i++)
            {
                int column = i % columns;
                int row = i / columns;
                float x = (column - ((columns - 1) * 0.5f)) * 3.0f;
                float z = 5.0f - (row * 3.0f);
                float y = 0.5f + ((i % 3) * 0.8f);
                float scaleX = (i % 7 == 0) ? -1.1f : 1.1f;
                float scaleY = 0.8f + ((i % 4) * 0.15f);
                float scaleZ = 1.0f + ((i % 3) * 0.2f);

                scene.Add(CreateObject(
                    mesh,
                    materials[i % materials.Length],
                    new Vector3(x, y, z),
                    yawRadians: (column * 0.37f) + (row * 0.19f),
                    scale: new Vector3(scaleX, scaleY, scaleZ)));
            }
        }

        return (scene, materials.Length);
    }

    private static SceneObject CreateObject(
        MeshData mesh,
        Material material,
        Vector3 position,
        float yawRadians,
        Vector3 scale)
    {
        return new SceneObject(
            mesh,
            new Transform(
                position,
                Quaternion.CreateFromYawPitchRoll(yawRadians, 0.0f, 0.0f),
                scale),
            material);
    }

    private static MeshData CreateCubeMesh()
    {
        return new MeshData(
            s_positions,
            s_normals,
            ReadOnlyMemory<Vector2>.Empty,
            s_indices);
    }
}
