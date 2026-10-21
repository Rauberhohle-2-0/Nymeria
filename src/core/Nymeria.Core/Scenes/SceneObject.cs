using Nymeria.Core.Materials;
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
/// A scene object does not own or dispose its mesh or material. Both are
/// managed data references with no unmanaged resources. GPU buffers, textures,
/// and samplers are created and released by the renderer, never by the scene or
/// a scene object.
/// </para>
/// <para>
/// The constructor copies the supplied transform so objects do not
/// accidentally share mutable state. Mutate <see cref="Transform"/> on this
/// object instead. Materials are immutable and may be shared by reference.
/// </para>
/// </remarks>
public sealed class SceneObject
{
    private Material _material;

    /// <summary>
    /// Creates a scene object for the supplied mesh.
    /// </summary>
    /// <param name="mesh">The CPU mesh to reference. It is not copied or owned.</param>
    /// <param name="transform">
    /// The initial transform, or null for an identity transform. The values are
    /// copied into a new <see cref="Scenes.Transform"/>.
    /// </param>
    /// <param name="material">
    /// The initial material, or null for <see cref="Material.Default"/>. The
    /// material is referenced, not copied.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="mesh"/> is null.</exception>
    public SceneObject(
        MeshData mesh,
        Transform? transform = null,
        Material? material = null)
    {
        Mesh = mesh ?? throw new ArgumentNullException(nameof(mesh));
        Transform = transform is null
            ? new Transform()
            : new Transform(transform.Position, transform.Rotation, transform.Scale);
        _material = material ?? Material.Default;
    }

    /// <summary>
    /// Gets the shared CPU mesh referenced by this object.
    /// </summary>
    public MeshData Mesh { get; }

    /// <summary>
    /// Gets this object's own transform.
    /// </summary>
    public Transform Transform { get; }

    /// <summary>
    /// Gets or sets the material assigned to this object.
    /// </summary>
    /// <remarks>
    /// The material is shared by reference and is not copied. Several objects
    /// may use the same material without duplicating its texture or GPU upload.
    /// </remarks>
    /// <exception cref="ArgumentNullException">The value is null.</exception>
    public Material Material
    {
        get => _material;
        set => _material = value ?? throw new ArgumentNullException(nameof(value));
    }
}
