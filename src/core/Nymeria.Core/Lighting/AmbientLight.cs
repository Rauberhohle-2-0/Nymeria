using System.Numerics;

namespace Nymeria.Core.Lighting;

/// <summary>
/// A uniform ambient light contribution for a scene.
/// </summary>
/// <remarks>
/// Ambient light has no direction or position. It is multiplied by the surface
/// base color exactly once, independent of the object transform or camera.
/// Colors use the same encoded values as material base colors and are expected
/// in the range 0 through 1; intensity scales the color before it reaches the
/// shader budget. The type contains no SDL3, GPU, or window dependency.
/// </remarks>
public sealed class AmbientLight
{
    /// <summary>The largest accepted light intensity.</summary>
    public const float MaxIntensity = 100.0f;

    /// <summary>
    /// The default ambient light: white at 0.1 intensity.
    /// </summary>
    public static AmbientLight Default { get; } = new(Vector3.One, 0.1f);

    /// <summary>
    /// Creates an ambient light.
    /// </summary>
    /// <param name="color">
    /// The light color. Each component must be finite and between 0 and 1.
    /// </param>
    /// <param name="intensity">
    /// The non-negative scalar intensity, up to
    /// <see cref="MaxIntensity"/>.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A color component or the intensity is outside its supported range.
    /// </exception>
    public AmbientLight(Vector3 color, float intensity)
    {
        LightingMath.ValidateColor(color, nameof(color));
        LightingMath.ValidateIntensity(intensity, nameof(intensity));

        Color = color;
        Intensity = intensity;
    }

    /// <summary>Gets the light color, with each component between 0 and 1.</summary>
    public Vector3 Color { get; }

    /// <summary>Gets the non-negative scalar intensity.</summary>
    public float Intensity { get; }

    /// <summary>
    /// Gets the color multiplied by the intensity, ready for upload.
    /// </summary>
    public Vector3 EffectiveColor => Color * Intensity;
}
