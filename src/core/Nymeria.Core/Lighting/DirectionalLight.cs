using System.Numerics;

namespace Nymeria.Core.Lighting;

/// <summary>
/// Exactly one directional light for a scene.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Direction"/> is the direction the light travels, in world space.
/// For example, a sun overhead traveling downward has direction
/// <c>(0, -1, 0)</c>. The direction is normalized when the light is created;
/// <see cref="DirectionToLight"/> is its negated value and is the vector used
/// by diffuse Lambert shading.
/// </para>
/// <para>
/// The light has no position. It affects every object equally, independent of
/// the camera. Colors are encoded values in the range 0 through 1 and intensity
/// scales the color before upload. Records are immutable, so changing a light at
/// runtime means replacing it in the scene's <see cref="SceneLighting"/>.
/// </para>
/// </remarks>
public sealed class DirectionalLight
{
    /// <summary>The largest accepted light intensity.</summary>
    public const float MaxIntensity = 100.0f;

    /// <summary>
    /// The default directional light: white at 1.0 intensity, traveling from
    /// the upper right front toward the lower left back.
    /// </summary>
    public static DirectionalLight Default { get; } =
        new(new Vector3(-0.4f, -1.0f, -0.3f), Vector3.One, 1.0f);

    /// <summary>
    /// Creates a directional light.
    /// </summary>
    /// <param name="direction">
    /// The world-space direction the light travels. It must be finite and have
    /// non-zero length; it is normalized before storage.
    /// </param>
    /// <param name="color">
    /// The light color. Each component must be finite and between 0 and 1.
    /// </param>
    /// <param name="intensity">
    /// The non-negative scalar intensity, up to
    /// <see cref="MaxIntensity"/>.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The direction is zero or non-finite, or a color or intensity value is
    /// outside its supported range.
    /// </exception>
    public DirectionalLight(Vector3 direction, Vector3 color, float intensity)
    {
        Direction = LightingMath.NormalizeDirection(direction, nameof(direction));
        LightingMath.ValidateColor(color, nameof(color));
        LightingMath.ValidateIntensity(intensity, nameof(intensity));

        Color = color;
        Intensity = intensity;
    }

    /// <summary>
    /// Gets the normalized world-space direction the light travels.
    /// </summary>
    public Vector3 Direction { get; }

    /// <summary>
    /// Gets the normalized world-space direction from the surface toward the
    /// light. This is the vector used by Lambert shading.
    /// </summary>
    public Vector3 DirectionToLight => -Direction;

    /// <summary>Gets the light color, with each component between 0 and 1.</summary>
    public Vector3 Color { get; }

    /// <summary>Gets the non-negative scalar intensity.</summary>
    public float Intensity { get; }

    /// <summary>
    /// Gets the color multiplied by the intensity, ready for upload.
    /// </summary>
    public Vector3 EffectiveColor => Color * Intensity;
}
