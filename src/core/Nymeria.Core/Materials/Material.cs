using System.Numerics;

namespace Nymeria.Core.Materials;

/// <summary>
/// A small, format-independent surface description: a base-color tint, an
/// optional base-color texture, and how alpha is used when rendering.
/// </summary>
/// <remarks>
/// <para>
/// A material is CPU data. It contains no GPU texture, sampler, or buffer
/// handle. Renderers may share one material across many scene objects and may
/// upload the referenced <see cref="Texture"/> once per distinct instance.
/// </para>
/// <para>
/// The renderer combines the inputs by multiplying the sampled texture RGB value
/// by <see cref="BaseColor"/>. A material without a texture uses a white fallback
/// texture, so its base color is shown directly. Effective alpha is the sampled
/// texture alpha multiplied by <see cref="Alpha"/>.
/// </para>
/// <para>
/// <see cref="Transparency"/> selects what the renderer does with that alpha:
/// <see cref="MaterialTransparency.Opaque"/> ignores it and writes an opaque
/// fragment, <see cref="MaterialTransparency.Mask"/> discards fragments below
/// <see cref="AlphaCutoff"/>, and <see cref="MaterialTransparency.Blend"/>
/// composes straight source-over alpha. The default is opaque, so a material
/// that does not ask for transparency renders exactly as before. This is
/// intentionally not a physically based material.
/// </para>
/// </remarks>
public sealed class Material
{
    /// <summary>The default alpha cutoff used by <see cref="MaterialTransparency.Mask"/>.</summary>
    public const float DefaultAlphaCutoff = 0.5f;

    /// <summary>
    /// Gets the default material: an untextured, opaque white tint.
    /// </summary>
    /// <remarks>
    /// Scene objects without an explicit material use this shared instance.
    /// </remarks>
    public static Material Default { get; } = new(Vector3.One, null, "default");

    /// <summary>
    /// Creates a material from a base-color tint, an optional texture, and an
    /// optional alpha policy.
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
    /// <param name="transparency">
    /// How alpha is used. The default is <see cref="MaterialTransparency.Opaque"/>.
    /// </param>
    /// <param name="alphaCutoff">
    /// The mask threshold in the inclusive range 0 to 1. A masked fragment is
    /// discarded when its effective alpha is below this value. The value is
    /// validated but has no effect in the opaque and blend modes.
    /// </param>
    /// <param name="alpha">
    /// The material opacity in the inclusive range 0 to 1, multiplied with the
    /// sampled texture alpha. It is ignored in the opaque mode.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A base-color component, <paramref name="alphaCutoff"/>, or
    /// <paramref name="alpha"/> is not finite or outside its allowed range, or
    /// <paramref name="transparency"/> is not a defined mode.
    /// </exception>
    public Material(
        Vector3 baseColor,
        Texture? baseColorTexture = null,
        string? name = null,
        MaterialTransparency transparency = MaterialTransparency.Opaque,
        float alphaCutoff = DefaultAlphaCutoff,
        float alpha = 1.0f)
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

        if (!Enum.IsDefined(transparency))
        {
            throw new ArgumentOutOfRangeException(
                nameof(transparency),
                transparency,
                $"The transparency mode must be one of {nameof(MaterialTransparency)}.");
        }

        if (!IsUnitRange(alphaCutoff))
        {
            throw new ArgumentOutOfRangeException(
                nameof(alphaCutoff),
                alphaCutoff,
                "The alpha cutoff must be finite and between 0 and 1.");
        }

        if (!IsUnitRange(alpha))
        {
            throw new ArgumentOutOfRangeException(
                nameof(alpha),
                alpha,
                "The material alpha must be finite and between 0 and 1.");
        }

        BaseColor = baseColor;
        BaseColorTexture = baseColorTexture;
        Name = string.IsNullOrWhiteSpace(name) ? null : name;
        Transparency = transparency;
        AlphaCutoff = alphaCutoff;
        Alpha = alpha;
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

    /// <summary>Gets how alpha is used when rendering.</summary>
    public MaterialTransparency Transparency { get; }

    /// <summary>
    /// Gets the mask threshold. A fragment whose effective alpha is below this
    /// value is discarded in <see cref="MaterialTransparency.Mask"/> mode.
    /// </summary>
    public float AlphaCutoff { get; }

    /// <summary>
    /// Gets the material opacity multiplied with the sampled texture alpha. It
    /// is ignored in <see cref="MaterialTransparency.Opaque"/> mode.
    /// </summary>
    public float Alpha { get; }

    /// <summary>Gets whether this material references a base-color texture.</summary>
    public bool HasTexture => BaseColorTexture is not null;

    /// <summary>
    /// Gets whether the renderer must order this material after opaque geometry.
    /// </summary>
    public bool RequiresBlendOrdering => Transparency == MaterialTransparency.Blend;

    private static bool IsUnitRange(float value)
    {
        return float.IsFinite(value) && value >= 0.0f && value <= 1.0f;
    }
}
