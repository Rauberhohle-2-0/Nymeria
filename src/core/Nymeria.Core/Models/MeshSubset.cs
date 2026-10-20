namespace Nymeria.Core.Models;

/// <summary>
/// Describes one draw range within a mesh's triangle index buffer.
/// </summary>
/// <param name="IndexOffset">
/// The zero-based offset of the first index in the mesh's index buffer.
/// </param>
/// <param name="IndexCount">
/// The number of indices in the range. It must be a multiple of three.
/// </param>
/// <param name="MaterialName">
/// An optional, opaque material name from the source format. The model data
/// does not contain material properties or resolve this name.
/// </param>
/// <remarks>
/// <see cref="MeshData"/> validates these ranges when it is constructed.
/// </remarks>
public readonly record struct MeshSubset(
    int IndexOffset,
    int IndexCount,
    string? MaterialName = null);
