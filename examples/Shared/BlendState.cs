using SDL3;
using static SDL3.SDL;

namespace Nymeria.Examples;

/// <summary>
/// The GPU blend state used by the shared material renderer.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="SourceOver"/> is straight (non-premultiplied) source-over
/// compositing: source color times source alpha plus destination color times one
/// minus source alpha, and the same rule for the alpha channel. The fragment
/// shader outputs the effective alpha, so no premultiplied-alpha assumption is
/// made by the pipeline.
/// </para>
/// <para>
/// SDL3-CS 3.4.18 exposes <c>GPUBlendFactor</c> and <c>GPUBlendOp</c> as nested
/// types of <c>SDL</c>, but a qualified lookup of those two names fails with
/// CS0117 in this binding even though the metadata contains them. Importing the
/// enclosing type's static members resolves both, so this file keeps that
/// workaround in one place instead of scattering numeric casts. Every value here
/// is the documented SDL blend factor; no numeric constant is used.
/// </para>
/// </remarks>
internal static class BlendState
{
    /// <summary>
    /// Gets a straight source-over state with blending enabled and every color
    /// component written.
    /// </summary>
    public static SDL.GPUColorTargetBlendState SourceOver => new()
    {
        EnableBlend = true,
        SrcColorBlendFactor = GPUBlendFactor.SrcAlpha,
        DstColorBlendFactor = GPUBlendFactor.OneMinusSrcAlpha,
        ColorBlendOp = GPUBlendOp.Add,
        SrcAlphaBlendFactor = GPUBlendFactor.One,
        DstAlphaBlendFactor = GPUBlendFactor.OneMinusSrcAlpha,
        AlphaBlendOp = GPUBlendOp.Add,
        EnableColorWriteMask = true,
        ColorWriteMask =
            GPUColorComponentFlags.R |
            GPUColorComponentFlags.G |
            GPUColorComponentFlags.B |
            GPUColorComponentFlags.A,
    };
}
