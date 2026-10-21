using Nymeria.Core.Models;

namespace Nymeria.Core.Scenes;

/// <summary>
/// One renderable instance in a <see cref="Scene"/>.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Mesh"/> is a shared reference to validated CPU mesh data. The
/// object does not copy the geometry. Multiple objects may reference the same
/// <see cref="MeshData"/> instance.
/// </para>
/// <para>
/// A scene object does not own or dispose its mesh. <see cref="MeshData"/> has
/// no unmanaged resources. GPU buffers are created and released by the
/// renderer, never by the scene or a scene object.
/// </para>
/// <para>
/// The constructor copies the supplied transform so objects do not
/// accidentally share mutable state. Mutate <see cref="Transform"/> on this
/// object instead.
/// </para>
/// </remarks>
public sealed class SceneObject
{
    /// <summary>
    /// Creates a scene object for the supplied mesh.
    /// </summary>
    /// <param name="mesh">The CPU mesh to reference. It is not copied or owned.</param>
    /// <param name="transform">
    /// The initial transform, or null for an identity transform. The values are
    /// copied into a new <see cref="Scenes.Transform"/>.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="mesh"/> is null.</exception>
    public SceneObject(MeshData mesh, Transform? transform = null)
    {
        Mesh = mesh ?? throw new ArgumentNullException(nameof(mesh));
        Transform = transform is null
            ? new Transform()
            : new Transform(transform.Position, transform.Rotation, transform.Scale);
    }

    /// <summary>
    /// Gets the shared CPU mesh referenced by this object.
    /// </summary>
    public MeshData Mesh { get; }

    /// <summary>
    /// Gets this object's own transform.
    /// </summary>
    public Transform Transform { get; }
}
