using System.Numerics;
using Nymeria.Core.Materials;
using Nymeria.Core.Models;
using Nymeria.Core.Scenes;
using Nymeria.Core.Streaming;

namespace HelloPicking;

/// <summary>
/// Builds the deterministic streamed world used by the picking demo.
/// </summary>
/// <remarks>
/// Every chunk shares one cube mesh and six materials, so unloading a chunk
/// never needs a renderer prune. Each chunk owns three ordinary
/// <see cref="SceneObject"/> instances, and one separate ghost box stays in the
/// scene while chunks load and unload. Labels are example presentation only and
/// are removed with the chunk content.
/// </remarks>
internal sealed class PickingFixture
{
    public const int ChunkCountPerAxis = 3;
    public const float ChunkSize = 26.0f;
    public const float HalfChunkSize = ChunkSize / 2.0f;

    private readonly MeshData _mesh;
    private readonly Material[] _materials;
    private readonly Material _ghostMaterial;
    private readonly Dictionary<SceneObject, string> _labels =
        new(ReferenceEqualityComparer.Instance);

    public PickingFixture()
    {
        _mesh = CreateSharedCubeMesh();
        _materials =
        [
            new Material(new Vector3(0.90f, 0.32f, 0.22f), null, "warm red"),
            new Material(new Vector3(0.22f, 0.55f, 0.92f), null, "blue"),
            new Material(new Vector3(0.30f, 0.80f, 0.42f), null, "green"),
            new Material(new Vector3(0.92f, 0.76f, 0.24f), null, "yellow"),
            new Material(new Vector3(0.72f, 0.38f, 0.88f), null, "violet"),
            new Material(new Vector3(0.32f, 0.82f, 0.84f), null, "cyan"),
        ];
        _ghostMaterial = new Material(new Vector3(0.30f, 0.32f, 0.36f), null, "ghost");
        HighlightMaterial = new Material(new Vector3(1.0f, 0.84f, 0.16f), null, "selection");
    }

    public Material HighlightMaterial { get; }

    public IReadOnlyList<SceneChunkDefinition> CreateChunks()
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
                string id = $"chunk-{chunkX + 1}-{chunkZ + 1}";
                var bounds = new BoundingBox(
                    new Vector3(
                        centerX - HalfChunkSize,
                        -1.0f,
                        centerZ - HalfChunkSize),
                    new Vector3(
                        centerX + HalfChunkSize,
                        12.0f,
                        centerZ + HalfChunkSize));

                chunks.Add(new SceneChunkDefinition(
                    id,
                    bounds,
                    () => CreateChunkObjects(capturedX, capturedZ),
                    content =>
                    {
                        foreach (SceneObject sceneObject in content)
                        {
                            _labels.Remove(sceneObject);
                        }
                    }));
            }
        }

        return chunks;
    }

    /// <summary>
    /// Creates the static, non-pickable box in front of the starting camera.
    /// The example toggles <see cref="SceneObject.IsPickable"/> at runtime.
    /// </summary>
    public SceneObject CreateGhost()
    {
        var ghost = new SceneObject(
            _mesh,
            new Transform(
                new Vector3(0.0f, 2.0f, 15.0f),
                Quaternion.Identity,
                new Vector3(3.5f, 2.0f, 0.5f)),
            _ghostMaterial)
        {
            IsPickable = false,
        };

        _labels.Add(ghost, "ghost box");
        return ghost;
    }

    public string? GetLabel(SceneObject sceneObject)
    {
        return _labels.TryGetValue(sceneObject, out string? label)
            ? label
            : null;
    }

    private IReadOnlyList<SceneObject> CreateChunkObjects(int chunkX, int chunkZ)
    {
        var center = new Vector3(chunkX * ChunkSize, 0.0f, chunkZ * ChunkSize);
        int seed = Math.Abs((chunkX * 13) + (chunkZ * 29));
        var objects = new SceneObject[3];

        // A rotated, non-uniformly scaled slab. Its world bounds are the
        // transformed box, so a pick near a corner demonstrates that the query
        // does not test the local mesh bounds.
        objects[0] = new SceneObject(
            _mesh,
            new Transform(
                center + new Vector3(-5.5f, 1.4f, -4.0f),
                Quaternion.CreateFromYawPitchRoll(seed * 0.17f, 0.0f, 0.0f),
                new Vector3(2.2f, 0.8f, 1.4f)),
            _materials[seed % _materials.Length]);
        _labels.Add(objects[0], $"chunk {chunkX + 1},{chunkZ + 1} slab");

        objects[1] = new SceneObject(
            _mesh,
            new Transform(
                center + new Vector3(4.5f, 1.2f, 3.5f),
                Quaternion.Identity,
                new Vector3(1.1f, 1.1f, 1.1f)),
            _materials[(seed + 2) % _materials.Length]);
        _labels.Add(objects[1], $"chunk {chunkX + 1},{chunkZ + 1} cube");

        // A tall, thin box at a different depth. Several of these share the
        // same screen region, so a pick demonstrates nearest-hit selection.
        objects[2] = new SceneObject(
            _mesh,
            new Transform(
                center + new Vector3(0.5f, 4.2f, -9.0f),
                Quaternion.Identity,
                new Vector3(0.6f, 2.4f, 0.6f)),
            _materials[(seed + 4) % _materials.Length]);
        _labels.Add(objects[2], $"chunk {chunkX + 1},{chunkZ + 1} tower");

        return objects;
    }

    /// <summary>
    /// Creates the example's original procedural cube. It has positions, face
    /// normals, texture coordinates, and six face subsets, but the picking demo
    /// uses untextured materials.
    /// </summary>
    private static MeshData CreateSharedCubeMesh()
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
