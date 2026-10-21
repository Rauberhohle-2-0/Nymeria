using System.Numerics;
using Nymeria.Core.Materials;
using Nymeria.Core.Models;
using Nymeria.Core.Scenes;
using Nymeria.Core.Streaming;

namespace HelloStreaming;

/// <summary>
/// Builds the deterministic five-by-five chunk world used by the demo.
/// </summary>
/// <remarks>
/// Every chunk shares one cube mesh and six materials. The tapered marker mesh is
/// rebuilt for each load so it is genuinely chunk-exclusive and the renderer's
/// resource pruning can release its GPU buffers after unload.
/// </remarks>
internal static class StreamingFixture
{
    public const int ChunkCountPerAxis = 5;
    public const float ChunkSize = 40.0f;
    public const float HalfChunkSize = ChunkSize / 2.0f;

    private const float MarkerHalfExtent = 1.35f;
    private const float MarkerTopHalfExtent = 0.45f;

    public static IReadOnlyList<SceneChunkDefinition> CreateChunks(
        MeshData sharedCube,
        Material[] sharedMaterials)
    {
        var chunks = new List<SceneChunkDefinition>(
            ChunkCountPerAxis * ChunkCountPerAxis);
        int first = -(ChunkCountPerAxis / 2);
        int last = first + ChunkCountPerAxis - 1;

        for (int chunkZ = first; chunkZ <= last; chunkZ++)
        {
            for (int chunkX = first; chunkX <= last; chunkX++)
            {
                int capturedX = chunkX;
                int capturedZ = chunkZ;
                float centerX = chunkX * ChunkSize;
                float centerZ = chunkZ * ChunkSize;
                string id = $"chunk-{chunkX + 2}-{chunkZ + 2}";
                var bounds = new BoundingBox(
                    new Vector3(
                        centerX - HalfChunkSize,
                        -0.5f,
                        centerZ - HalfChunkSize),
                    new Vector3(
                        centerX + HalfChunkSize,
                        12.0f,
                        centerZ + HalfChunkSize));

                chunks.Add(new SceneChunkDefinition(
                    id,
                    bounds,
                    () => CreateChunkObjects(
                        capturedX,
                        capturedZ,
                        sharedCube,
                        sharedMaterials)));
            }
        }

        return chunks;
    }

    public static Vector3 ChunkCenter(int chunkX, int chunkZ)
    {
        return new Vector3(chunkX * ChunkSize, 0.0f, chunkZ * ChunkSize);
    }

    private static IReadOnlyList<SceneObject> CreateChunkObjects(
        int chunkX,
        int chunkZ,
        MeshData sharedCube,
        Material[] sharedMaterials)
    {
        Vector3 center = ChunkCenter(chunkX, chunkZ);
        int chunkSeed = Math.Abs((chunkX * 17) + (chunkZ * 31));
        var objects = new List<SceneObject>(11);

        // A shared cube, stretched into a floor tile. It is the same MeshData
        // reference in every chunk, so the renderer uploads it once.
        objects.Add(new SceneObject(
            sharedCube,
            new Transform(
                center + new Vector3(0.0f, -0.1f, 0.0f),
                Quaternion.Identity,
                new Vector3(HalfChunkSize - 0.2f, 0.1f, HalfChunkSize - 0.2f)),
            sharedMaterials[chunkSeed % sharedMaterials.Length]));

        // Nine repeated cubes exercise shared geometry and materials inside one
        // chunk. Their positions stay well inside the chunk bounds.
        for (int row = 0; row < 3; row++)
        {
            for (int column = 0; column < 3; column++)
            {
                int index = (row * 3) + column;
                float x = center.X + ((column - 1) * 10.0f);
                float z = center.Z + ((row - 1) * 10.0f);
                float scale = 0.9f + ((index + chunkSeed) % 4) * 0.22f;
                float yaw = ((index + chunkSeed) % 7) * 0.18f;

                objects.Add(new SceneObject(
                    sharedCube,
                    new Transform(
                        new Vector3(x, scale, z),
                        Quaternion.CreateFromYawPitchRoll(yaw, 0.0f, 0.0f),
                        new Vector3(scale, scale, scale)),
                    sharedMaterials[(index + chunkSeed) % sharedMaterials.Length]));
            }
        }

        // The marker is intentionally unique per load. Its CPU data becomes
        // unreachable after unload, and PruneUnusedResources releases its GPU
        // buffers because no remaining object references this mesh.
        float markerHeight = 2.4f + ((chunkSeed % 4) * 0.7f);
        MeshData markerMesh = CreateMarkerMesh(markerHeight);
        objects.Add(new SceneObject(
            markerMesh,
            new Transform(
                center + new Vector3(9.0f, 0.0f, -9.0f),
                Quaternion.CreateFromYawPitchRoll(chunkSeed * 0.11f, 0.0f, 0.0f),
                Vector3.One),
            sharedMaterials[(chunkSeed + 3) % sharedMaterials.Length]));

        return objects;
    }

    public static MeshData CreateSharedCubeMesh()
    {
        const float half = 1.0f;

        Vector3[] positions =
        [
            // Front (+Z)
            new(-half, -half,  half),
            new( half, -half,  half),
            new( half,  half,  half),
            new(-half,  half,  half),

            // Back (-Z)
            new( half, -half, -half),
            new(-half, -half, -half),
            new(-half,  half, -half),
            new( half,  half, -half),

            // Right (+X)
            new( half, -half,  half),
            new( half, -half, -half),
            new( half,  half, -half),
            new( half,  half,  half),

            // Left (-X)
            new(-half, -half, -half),
            new(-half, -half,  half),
            new(-half,  half,  half),
            new(-half,  half, -half),

            // Top (+Y)
            new(-half,  half,  half),
            new( half,  half,  half),
            new( half,  half, -half),
            new(-half,  half, -half),

            // Bottom (-Y)
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

    private static MeshData CreateMarkerMesh(float height)
    {
        var positions = new List<Vector3>(20);
        var normals = new List<Vector3>(20);
        var textureCoordinates = new List<Vector2>(20);
        var indices = new List<uint>(30);

        Vector3[] baseCorners =
        [
            new(-MarkerHalfExtent, 0.0f,  MarkerHalfExtent),
            new( MarkerHalfExtent, 0.0f,  MarkerHalfExtent),
            new( MarkerHalfExtent, 0.0f, -MarkerHalfExtent),
            new(-MarkerHalfExtent, 0.0f, -MarkerHalfExtent),
        ];
        Vector3[] topCorners =
        [
            new(-MarkerTopHalfExtent, height,  MarkerTopHalfExtent),
            new( MarkerTopHalfExtent, height,  MarkerTopHalfExtent),
            new( MarkerTopHalfExtent, height, -MarkerTopHalfExtent),
            new(-MarkerTopHalfExtent, height, -MarkerTopHalfExtent),
        ];
        Vector3[] sideNormals =
        [
            Vector3.UnitZ,
            Vector3.UnitX,
            -Vector3.UnitZ,
            -Vector3.UnitX,
        ];

        for (int side = 0; side < 4; side++)
        {
            int next = (side + 1) % 4;
            AddQuad(
                positions,
                normals,
                textureCoordinates,
                indices,
                baseCorners[side],
                baseCorners[next],
                topCorners[next],
                topCorners[side],
                sideNormals[side]);
        }

        // A flat top closes the tapered marker. Face orientation is not culled,
        // but the normal is still outward for lighting.
        AddQuad(
            positions,
            normals,
            textureCoordinates,
            indices,
            topCorners[0],
            topCorners[1],
            topCorners[2],
            topCorners[3],
            Vector3.UnitY);

        return new MeshData(
            positions.ToArray(),
            normals.ToArray(),
            textureCoordinates.ToArray(),
            indices.ToArray());
    }

    private static void AddQuad(
        List<Vector3> positions,
        List<Vector3> normals,
        List<Vector2> textureCoordinates,
        List<uint> indices,
        Vector3 a,
        Vector3 b,
        Vector3 c,
        Vector3 d,
        Vector3 normal)
    {
        uint start = (uint)positions.Count;
        positions.Add(a);
        positions.Add(b);
        positions.Add(c);
        positions.Add(d);
        normals.Add(normal);
        normals.Add(normal);
        normals.Add(normal);
        normals.Add(normal);
        textureCoordinates.Add(new Vector2(0.0f, 1.0f));
        textureCoordinates.Add(new Vector2(1.0f, 1.0f));
        textureCoordinates.Add(new Vector2(1.0f, 0.0f));
        textureCoordinates.Add(new Vector2(0.0f, 0.0f));
        indices.Add(start);
        indices.Add(start + 1);
        indices.Add(start + 2);
        indices.Add(start);
        indices.Add(start + 2);
        indices.Add(start + 3);
    }
}
