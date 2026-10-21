using System.Numerics;
using Nymeria.Core.Lod;
using Nymeria.Core.Materials;
using Nymeria.Core.Models;
using Nymeria.Core.Scenes;

namespace HelloBenchmark;

/// <summary>
/// Builds the deterministic benchmark scene: three shared detail variants of the
/// same cube, four shared untextured materials, and a reproducible instance
/// count. The renderer's LOD toggle decides whether the variants are used.
/// </summary>
internal static class BenchmarkScene
{
    private const float HalfExtent = 1.0f;
    private const float FirstLodThreshold = 18.0f;
    private const float SecondLodThreshold = 45.0f;
    private const float MaximumRenderDistance = 90.0f;
    private const float LodHysteresis = 0.5f;

    public static (Scene Scene, int MaterialCount, int HighTriangles, int MediumTriangles, int LowTriangles) Create(int instanceCount)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(instanceCount, 1);

        MeshData high = CreateCubeMesh(divisions: 8);
        MeshData medium = CreateCubeMesh(divisions: 3);
        MeshData low = CreateCubeMesh(divisions: 1);
        var lod = new MeshLod(
            [
                new MeshLodLevel(high, 0.0f),
                new MeshLodLevel(medium, FirstLodThreshold),
                new MeshLodLevel(low, SecondLodThreshold),
            ],
            maximumRenderDistance: MaximumRenderDistance,
            hysteresis: LodHysteresis);
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
                high,
                lod,
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
                    high,
                    lod,
                    materials[i % materials.Length],
                    new Vector3(x, y, z),
                    yawRadians: (column * 0.37f) + (row * 0.19f),
                    scale: new Vector3(scaleX, scaleY, scaleZ)));
            }
        }

        return (
            scene,
            materials.Length,
            high.Indices.Length / 3,
            medium.Indices.Length / 3,
            low.Indices.Length / 3);
    }

    private static SceneObject CreateObject(
        MeshData mesh,
        MeshLod lod,
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
            material,
            lod);
    }

    private static MeshData CreateCubeMesh(int divisions)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(divisions, 1);

        (Vector3 Normal, Vector3 Right, Vector3 Up)[] faces =
        [
            (Vector3.UnitZ, Vector3.UnitX, Vector3.UnitY),
            (-Vector3.UnitZ, -Vector3.UnitX, Vector3.UnitY),
            (Vector3.UnitX, -Vector3.UnitZ, Vector3.UnitY),
            (-Vector3.UnitX, Vector3.UnitZ, Vector3.UnitY),
            (Vector3.UnitY, Vector3.UnitX, -Vector3.UnitZ),
            (-Vector3.UnitY, Vector3.UnitX, Vector3.UnitZ),
        ];

        int quadsPerFace = divisions * divisions;
        int verticesPerFace = quadsPerFace * 4;
        var positions = new Vector3[faces.Length * verticesPerFace];
        var normals = new Vector3[faces.Length * verticesPerFace];
        var indices = new uint[faces.Length * quadsPerFace * 6];
        int vertexWrite = 0;
        int indexWrite = 0;

        foreach ((Vector3 normal, Vector3 right, Vector3 up) in faces)
        {
            for (int row = 0; row < divisions; row++)
            {
                float v0 = ((row * 2.0f) / divisions) - 1.0f;
                float v1 = (((row + 1) * 2.0f) / divisions) - 1.0f;

                for (int column = 0; column < divisions; column++)
                {
                    float u0 = ((column * 2.0f) / divisions) - 1.0f;
                    float u1 = (((column + 1) * 2.0f) / divisions) - 1.0f;
                    uint baseIndex = (uint)vertexWrite;

                    positions[vertexWrite] = normal + (right * u0) + (up * v0);
                    normals[vertexWrite] = normal;
                    vertexWrite++;
                    positions[vertexWrite] = normal + (right * u1) + (up * v0);
                    normals[vertexWrite] = normal;
                    vertexWrite++;
                    positions[vertexWrite] = normal + (right * u1) + (up * v1);
                    normals[vertexWrite] = normal;
                    vertexWrite++;
                    positions[vertexWrite] = normal + (right * u0) + (up * v1);
                    normals[vertexWrite] = normal;
                    vertexWrite++;

                    indices[indexWrite++] = baseIndex;
                    indices[indexWrite++] = baseIndex + 1;
                    indices[indexWrite++] = baseIndex + 2;
                    indices[indexWrite++] = baseIndex;
                    indices[indexWrite++] = baseIndex + 2;
                    indices[indexWrite++] = baseIndex + 3;
                }
            }
        }

        return new MeshData(
            positions,
            normals,
            ReadOnlyMemory<Vector2>.Empty,
            indices);
    }
}
