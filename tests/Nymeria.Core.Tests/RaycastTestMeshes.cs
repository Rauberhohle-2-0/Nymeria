using System.Numerics;
using Nymeria.Core.Models;

namespace Nymeria.Core.Tests;

internal static class RaycastTestMeshes
{
    public static MeshData CreateBoxMesh(float halfExtent)
    {
        return CreateBoundsMesh(new Vector3(-halfExtent), new Vector3(halfExtent));
    }

    /// <summary>
    /// Creates a valid mesh whose model-space bounds are exactly the supplied
    /// corners. The two triangles are only present because MeshData requires a
    /// triangle list; picking tests use the bounds, not the surface.
    /// </summary>
    public static MeshData CreateBoundsMesh(Vector3 min, Vector3 max)
    {
        return new MeshData(
            new[]
            {
                min,
                max,
                new Vector3(max.X, min.Y, min.Z),
                new Vector3(min.X, max.Y, max.Z),
            },
            ReadOnlyMemory<Vector3>.Empty,
            ReadOnlyMemory<Vector2>.Empty,
            new uint[] { 0, 1, 2, 0, 1, 3 });
    }
}
