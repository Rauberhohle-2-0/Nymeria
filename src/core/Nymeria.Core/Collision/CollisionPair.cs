using Nymeria.Core.Scenes;

namespace Nymeria.Core.Collision;

/// <summary>
/// Two scene objects whose collision shapes overlap in one query.
/// </summary>
/// <remarks>
/// <see cref="First"/> precedes <see cref="Second"/> in scene insertion order, so
/// a pair is reported once and its ordering is deterministic. The pair holds
/// ordinary managed references and does not keep either object alive or in the
/// scene. Validate a stored pair with <see cref="Scene.Contains(SceneObject)"/>
/// after a streaming update or another scene mutation.
/// </remarks>
public readonly struct CollisionPair
{
    internal CollisionPair(SceneObject first, SceneObject second)
    {
        First = first;
        Second = second;
    }

    /// <summary>
    /// Gets the object that appeared first in the scene.
    /// </summary>
    public SceneObject First { get; }

    /// <summary>
    /// Gets the object that appeared second in the scene.
    /// </summary>
    public SceneObject Second { get; }
}
