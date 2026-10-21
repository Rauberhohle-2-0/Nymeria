using System.Collections.ObjectModel;
using Nymeria.Core.Lighting;

namespace Nymeria.Core.Scenes;

/// <summary>
/// A deterministic collection of renderable scene objects.
/// </summary>
/// <remarks>
/// <para>
/// Objects are iterated in insertion order. Removing an object keeps the
/// relative order of the remaining objects. Adding the same object twice is
/// rejected, so one reference identifies at most one scene entry.
/// </para>
/// <para>
/// The scene is not thread-safe and owns no GPU resources or mesh data. It
/// references <see cref="SceneObject"/> instances only. A renderer owns any GPU
/// uploads and releases them when it is disposed.
/// </para>
/// </remarks>
public sealed class Scene
{
    /// <summary>
    /// The largest number of objects a scene accepts by default.
    /// </summary>
    /// <remarks>
    /// This is a practical application safeguard, not a scene-graph limit. It
    /// keeps accidental unbounded growth from turning into an unbounded draw
    /// loop or resource request.
    /// </remarks>
    public const int MaxObjectCount = 100_000;

    private readonly List<SceneObject> _objects = new();
    private readonly HashSet<SceneObject> _objectSet =
        new(ReferenceEqualityComparer.Instance);
    private readonly ReadOnlyCollection<SceneObject> _readOnlyObjects;
    private readonly int _maxObjectCount;

    /// <summary>
    /// Creates an empty scene with the default object limit.
    /// </summary>
    public Scene()
        : this(MaxObjectCount)
    {
    }

    /// <summary>
    /// Creates an empty scene with a lower object limit.
    /// </summary>
    /// <param name="maxObjectCount">
    /// The maximum number of objects, from 1 through <see cref="MaxObjectCount"/>.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="maxObjectCount"/> is outside the supported range.
    /// </exception>
    public Scene(int maxObjectCount)
    {
        if (maxObjectCount < 1 || maxObjectCount > MaxObjectCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxObjectCount),
                maxObjectCount,
                $"The scene object limit must be between 1 and {MaxObjectCount}.");
        }

        _maxObjectCount = maxObjectCount;
        _readOnlyObjects = _objects.AsReadOnly();
    }

    /// <summary>
    /// Gets the number of objects currently in the scene.
    /// </summary>
    public int Count => _objects.Count;

    /// <summary>
    /// Gets the objects in insertion order. The view is read-only and live.
    /// </summary>
    public IReadOnlyList<SceneObject> Objects => _readOnlyObjects;

    /// <summary>
    /// Gets or sets the optional lighting configuration for this scene.
    /// </summary>
    /// <remarks>
    /// The default is <see langword="null"/>, which means unlit. Renderers that
    /// support lighting treat a null value as an unlit surface and preserve the
    /// behavior of examples that predate lighting. Assigning a
    /// <see cref="SceneLighting"/> opts the scene into ambient and directional
    /// lighting. The lighting object is immutable; replace it to change a light
    /// at runtime. The scene only references it and owns no GPU resources.
    /// </remarks>
    public SceneLighting? Lighting { get; set; }

    /// <summary>
    /// Adds an object at the end of the scene.
    /// </summary>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="sceneObject"/> is null.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The object is already in this scene, or the scene is at its object limit.
    /// </exception>
    public void Add(SceneObject sceneObject)
    {
        ArgumentNullException.ThrowIfNull(sceneObject);

        if (_objectSet.Contains(sceneObject))
        {
            throw new InvalidOperationException(
                "The scene object is already in this scene.");
        }

        if (_objects.Count >= _maxObjectCount)
        {
            throw new InvalidOperationException(
                $"The scene cannot contain more than {_maxObjectCount} objects.");
        }

        _objects.Add(sceneObject);
        _objectSet.Add(sceneObject);
    }

    /// <summary>
    /// Removes an object while preserving the order of the remaining objects.
    /// </summary>
    /// <returns>True when the object was present and removed.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="sceneObject"/> is null.
    /// </exception>
    public bool Remove(SceneObject sceneObject)
    {
        ArgumentNullException.ThrowIfNull(sceneObject);

        if (!_objectSet.Remove(sceneObject))
        {
            return false;
        }

        _objects.Remove(sceneObject);
        return true;
    }

    /// <summary>
    /// Gets whether the scene contains the supplied object reference.
    /// </summary>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="sceneObject"/> is null.
    /// </exception>
    public bool Contains(SceneObject sceneObject)
    {
        ArgumentNullException.ThrowIfNull(sceneObject);
        return _objectSet.Contains(sceneObject);
    }

    /// <summary>
    /// Removes every object from the scene.
    /// </summary>
    public void Clear()
    {
        _objects.Clear();
        _objectSet.Clear();
    }
}
