using Nymeria.Core.Collision;
using Nymeria.Core.Lod;
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
/// <para>
/// An optional <see cref="Lod"/> configuration supplies explicit mesh variants
/// at increasing camera distances. It is a shared reference and is not owned.
/// The configuration's highest-detail level must be this object's
/// <see cref="Mesh"/>. Selection state is kept per object, so sharing one
/// configuration between objects does not share their current level.
/// </para>
/// </remarks>
public sealed class SceneObject
{
    private Material _material;
    private MeshLod? _lod;
    private int _currentLodLevel = -1;

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
    /// <param name="lod">
    /// The optional level-of-detail configuration, or null for none. It is
    /// referenced, not copied. Its highest-detail mesh must be
    /// <paramref name="mesh"/>.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="mesh"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="lod"/> is not null and its highest-detail mesh is not
    /// <paramref name="mesh"/>.
    /// </exception>
    public SceneObject(
        MeshData mesh,
        Transform? transform = null,
        Material? material = null,
        MeshLod? lod = null)
    {
        Mesh = mesh ?? throw new ArgumentNullException(nameof(mesh));
        Transform = transform is null
            ? new Transform()
            : new Transform(transform.Position, transform.Rotation, transform.Scale);
        _material = material ?? Material.Default;
        Lod = lod;
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
    /// Gets or sets the optional level-of-detail configuration for this object.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The default is <see langword="null"/>, which means the object always
    /// draws <see cref="Mesh"/> and is never distance-culled. Assigning a
    /// configuration opts this object into LOD selection and its optional
    /// maximum render distance. The configuration is shared by reference and is
    /// not copied; it is also not owned or disposed by the object.
    /// </para>
    /// <para>
    /// The configuration's <c>Levels[0]</c> mesh must be the same reference as
    /// <see cref="Mesh"/>, so a renderer can always fall back to the object's
    /// primary mesh. Assigning a configuration resets this object's current
    /// level, including when it is the same configuration instance again.
    /// Assigning null disables LOD and resets the selection state.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// The value is not null and its highest-detail mesh is not <see cref="Mesh"/>.
    /// </exception>
    public MeshLod? Lod
    {
        get => _lod;
        set
        {
            if (value is not null &&
                !ReferenceEquals(value.Levels[0].Mesh, Mesh))
            {
                throw new ArgumentException(
                    "The highest-detail LOD mesh must be the scene object's Mesh.",
                    nameof(value));
            }

            _lod = value;
            _currentLodLevel = -1;
        }
    }

    /// <summary>
    /// Gets or sets whether this object participates in scene raycast queries.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The default is <see langword="true"/>. Setting it to false makes the
    /// object ineligible for picking while leaving rendering, frustum culling,
    /// distance culling, and LOD selection unchanged. It is a logical
    /// interaction flag, not a visibility flag: an unpickable object is still
    /// drawn if it would otherwise be rendered.
    /// </para>
    /// <para>
    /// Frustum and distance culling are deliberately not considered by picking,
    /// so an object outside the current view can still be picked by a ray that
    /// reaches it. See <c>docs/PICKING.md</c>.
    /// </para>
    /// </remarks>
    public bool IsPickable { get; set; } = true;

    /// <summary>
    /// Gets or sets the optional collision shape for this object in local model
    /// space.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The default is <see langword="null"/>, which means the object does not
    /// participate in <see cref="SceneCollision"/> queries. Assigning a shape
    /// opts the object in without changing its mesh, material, LOD, pickability,
    /// or rendering. The shape is independent of the render mesh and is not
    /// derived from it; callers choose the bounds that suit their interaction.
    /// </para>
    /// <para>
    /// Collision reads the object's current <see cref="Transform.CreateModelMatrix"/>
    /// at query time. Frustum culling, distance culling, and the selected LOD
    /// level do not affect collision. Removing an object or unloading its chunk
    /// removes it from scene collision queries because collision traverses
    /// current scene membership.
    /// </para>
    /// </remarks>
    public CollisionShape? Collider { get; set; }

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

    /// <summary>
    /// Gets the level selected by the most recent LOD-aware visibility test, or
    /// a negative value when no level has been selected yet.
    /// </summary>
    internal int CurrentLodLevel => _currentLodLevel;

    internal void SetCurrentLodLevel(int level)
    {
        _currentLodLevel = level;
    }

    internal void ResetLodLevel()
    {
        _currentLodLevel = -1;
    }
}
