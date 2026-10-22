namespace Nymeria.Core.Materials;

/// <summary>
/// How the renderer treats the alpha of a <see cref="Material"/>.
/// </summary>
/// <remarks>
/// <para>
/// The mode is CPU data. A renderer maps it to its own pipeline state; the
/// material itself contains no GPU state and no blend configuration.
/// </para>
/// <para>
/// <see cref="Opaque"/> ignores alpha and writes an opaque fragment.
/// <see cref="Mask"/> discards fragments whose effective alpha is below
/// <see cref="Material.AlphaCutoff"/> and is otherwise opaque.
/// <see cref="Blend"/> composites the fragment with straight-alpha source-over
/// blending and does not write depth.
/// </para>
/// </remarks>
public enum MaterialTransparency
{
    /// <summary>Depth-tested, depth-writing, unblended opaque rendering.</summary>
    Opaque = 0,

    /// <summary>
    /// Depth-tested, depth-writing rendering that discards fragments below the
    /// material's alpha cutoff.
    /// </summary>
    Mask = 1,

    /// <summary>
    /// Depth-tested rendering without depth writes, composited with straight
    /// source-over alpha blending.
    /// </summary>
    Blend = 2,
}
