using System.Numerics;
using Nymeria.Core.Lod;
using Nymeria.Core.Models;
using Nymeria.Core.Scenes;

namespace Nymeria.Core.Streaming;

/// <summary>
/// Immutable metadata and content source for one spatially bounded scene chunk.
/// </summary>
/// <remarks>
/// <para>
/// A definition names a chunk, supplies its world-space bounds, and owns a
/// callback that creates the chunk's scene objects when the chunk is loaded.
/// The callback runs only on the streaming update's thread and may create
/// objects that share meshes, materials, and textures with other chunks. The
/// callback owns any application resources it creates; the streamer only owns
/// the resulting scene-object membership while the chunk is loaded.
/// </para>
/// <para>
/// An optional release callback runs after the chunk's objects have been
/// removed from the scene. It exists for application-owned, chunk-exclusive
/// resources such as disposable CPU data or a renderer-specific cache entry. It
/// must not dispose a mesh, material, or texture that another active chunk or
/// external scene object still references.
/// </para>
/// <para>
/// Bounds are the chunk's conservative world-space extent. They determine
/// streaming demand; they do not replace the per-object bounds used by frustum
/// culling or LOD. Overlapping chunk bounds are allowed and are treated as
/// independent chunks. Empty content is valid and produces a loaded chunk with
/// no scene objects.
/// </para>
/// <para>
/// This type is platform- and GPU-independent and performs no file I/O. A
/// callback that loads external data is application code and must apply its own
/// path, format, size, and failure checks. See <c>docs/STREAMING.md</c>.
/// </para>
/// </remarks>
public sealed class SceneChunkDefinition
{
    /// <summary>
    /// The maximum accepted identifier length.
    /// </summary>
    public const int MaxIdLength = 128;

    private readonly Func<IReadOnlyList<SceneObject>> _loadContent;
    private readonly Action<IReadOnlyList<SceneObject>>? _releaseContent;

    /// <summary>
    /// Creates a chunk definition.
    /// </summary>
    /// <param name="id">
    /// A unique, non-empty identifier of at most
    /// <see cref="MaxIdLength"/> characters. It may not contain control
    /// characters or leading or trailing whitespace. Identifiers are compared
    /// ordinally.
    /// </param>
    /// <param name="bounds">
    /// The conservative world-space bounds used for streaming selection. A
    /// degenerate point is accepted.
    /// </param>
    /// <param name="loadContent">
    /// Creates the chunk's scene objects. It is invoked on the streaming update
    /// thread and must return a non-null list containing no null entries. The
    /// returned object references must not already belong to the scene or
    /// another loaded chunk. The returned list must not be mutated after it is
    /// returned; the streamer passes the same list to the release callback.
    /// </param>
    /// <param name="releaseContent">
    /// An optional callback invoked after the content has been removed from the
    /// scene. It receives the list returned by <paramref name="loadContent"/>.
    /// It must not dispose resources shared with another active chunk.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="id"/> or <paramref name="loadContent"/> is null.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// The identifier is empty, whitespace-padded, too long, or contains control
    /// characters.
    /// </exception>
    public SceneChunkDefinition(
        string id,
        BoundingBox bounds,
        Func<IReadOnlyList<SceneObject>> loadContent,
        Action<IReadOnlyList<SceneObject>>? releaseContent = null)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(loadContent);

        if (id.Length == 0 || string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException(
                "A chunk identifier must not be empty or whitespace.",
                nameof(id));
        }

        if (id.Length > MaxIdLength)
        {
            throw new ArgumentException(
                $"A chunk identifier cannot exceed {MaxIdLength} characters.",
                nameof(id));
        }

        if (id[0] != id.TrimStart()[0] || id[^1] != id.TrimEnd()[^1])
        {
            throw new ArgumentException(
                "A chunk identifier must not have leading or trailing whitespace.",
                nameof(id));
        }

        for (int index = 0; index < id.Length; index++)
        {
            if (char.IsControl(id[index]))
            {
                throw new ArgumentException(
                    "A chunk identifier must not contain control characters.",
                    nameof(id));
            }
        }

        Id = id;
        Bounds = bounds;
        _loadContent = loadContent;
        _releaseContent = releaseContent;
    }

    /// <summary>
    /// Gets the unique chunk identifier.
    /// </summary>
    public string Id { get; }

    /// <summary>
    /// Gets the conservative world-space bounds used for streaming selection.
    /// </summary>
    public BoundingBox Bounds { get; }

    /// <summary>
    /// Gets the distance from a world-space point to the closest point of the
    /// chunk bounds.
    /// </summary>
    /// <remarks>
    /// A point inside the bounds is at distance zero. The calculation reuses
    /// <see cref="LodMath.DistanceToBounds"/> and fails safe to zero for invalid
    /// input.
    /// </remarks>
    public float DistanceTo(Vector3 point)
    {
        return LodMath.DistanceToBounds(point, Bounds);
    }

    internal IReadOnlyList<SceneObject> LoadContent()
    {
        return _loadContent();
    }

    internal void ReleaseContent(IReadOnlyList<SceneObject> content)
    {
        _releaseContent?.Invoke(content);
    }
}
