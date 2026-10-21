namespace Nymeria.Core.Lighting;

/// <summary>
/// The lighting configuration for one scene: ambient light plus at most one
/// directional light.
/// </summary>
/// <remarks>
/// <para>
/// The object is immutable and contains only CPU data. Changing the lighting at
/// runtime means assigning a new instance to <see cref="Scenes.Scene.Lighting"/>.
/// A scene without lighting data is rendered unlit by the examples that support
/// lighting, preserving the behavior of the earlier examples.
/// </para>
/// <para>
/// There is no light collection, point or spot light, shadow, or physically
/// based model. Those features are intentionally deferred.
/// </para>
/// </remarks>
public sealed class SceneLighting
{
    /// <summary>
    /// Creates a lighting configuration.
    /// </summary>
    /// <param name="ambient">The ambient contribution. It is required.</param>
    /// <param name="directional">
    /// The single directional light, or null for ambient-only lighting.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="ambient"/> is null.</exception>
    public SceneLighting(AmbientLight ambient, DirectionalLight? directional = null)
    {
        Ambient = ambient ?? throw new ArgumentNullException(nameof(ambient));
        Directional = directional;
    }

    /// <summary>Gets the ambient light.</summary>
    public AmbientLight Ambient { get; }

    /// <summary>Gets the directional light, or null for ambient-only lighting.</summary>
    public DirectionalLight? Directional { get; }

    /// <summary>
    /// Gets whether the configuration contributes no light at all.
    /// </summary>
    public bool IsUnlit =>
        Ambient.Intensity == 0.0f && (Directional is null || Directional.Intensity == 0.0f);
}
