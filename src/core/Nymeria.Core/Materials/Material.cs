using System.Numerics;

namespace Nymeria.Core.Materials;

/// <summary>
/// A small, format-independent surface description: a base-color tint and an
/// optional base-color texture.
/// </summary>
/// <remarks>
/// <para>
/// A material is CPU data. It contains no GPU texture, sampler, or buffer
/// handle. Renderers may share one material across many scene objects and may
/// upload the referenced <see cref="Texture"/> once per distinct instance.
/// </para>
/// <para>
/// The renderer combines the two inputs by multiplying the sampled texture RGB
/// value by <see cref="BaseColor"/>. A material without a texture uses a white
/// fallback texture, so its base color is shown directly. Alpha is not blended.
/// This is intentionally not a physically based material.
/// </para>
/// </remarks>
public sealed class Material
{
    /// <summary>
    /// Gets the default material: an untextured white tint.
    /// </summary>
    /// <remarks>
    /// Scene objects without an explicit material use this shared instance.
    /// </remarks>
    public static Material Default { get; } = new(Vector3.One, null, "default");

    /// <summary>
    /// Creates a material from a base-color tint and an optional texture.
    /// </summary>
    /// <param name="baseColor">
    /// The linear display tint multiplied with the texture. Every component must
    /// be finite and between 0 and 1.
    /// </param>
    /// <param name="baseColorTexture">
    /// The optional base-color texture, either decoded RGBA pixels or compressed
    /// blocks. The material references it and does not own or copy its payload.
    /// </param>
    /// <param name="name">An optional name used in diagnostics.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A base-color component is not finite or is outside the 0 to 1 range.
    /// </exception>
    public Material(
        Vector3 baseColor,
        Texture? baseColorTexture = null,
        string? name = null)
    {
        if (!float.IsFinite(baseColor.X) ||
            !float.IsFinite(baseColor.Y) ||
            !float.IsFinite(baseColor.Z) ||
            baseColor.X < 0.0f ||
            baseColor.X > 1.0f ||
            baseColor.Y < 0.0f ||
            baseColor.Y > 1.0f ||
            baseColor.Z < 0.0f ||
            baseColor.Z > 1.0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(baseColor),
                baseColor,
                "Base-color components must be finite and between 0 and 1.");
        }

        BaseColor = baseColor;
        BaseColorTexture = baseColorTexture;
        Name = string.IsNullOrWhiteSpace(name) ? null : name;
    }

    /// <summary>Gets the base-color tint.</summary>
    public Vector3 BaseColor { get; }

    /// <summary>
    /// Gets the optional base-color texture, or null when the material is
    /// untextured.
    /// </summary>
    public Texture? BaseColorTexture { get; }

    /// <summary>Gets the optional name used in diagnostics.</summary>
    public string? Name { get; }

    /// <summary>Gets whether this material references a base-color texture.</summary>
    public bool HasTexture => BaseColorTexture is not null;
}
