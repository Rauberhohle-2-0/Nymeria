using SDL3;

namespace Nymeria.Graphics;

/// <summary>
/// The vertex and fragment shader assets for one shader family in the format a
/// device accepts.
/// </summary>
/// <remarks>
/// <para>
/// This type holds the mapping from a device shader format to the embedded
/// resources and entry points Nymeria ships for that format. It is the only
/// place that knows the naming convention, so renderers select a program once and
/// never branch on the operating system or on the backend.
/// </para>
/// <para>
/// Metal takes Metal Shading Language source in one resource for both stages,
/// with the entry points <c>vertex_main</c> and <c>fragment_main</c>. Direct3D 12
/// takes DXIL bytecode, one signed blob per stage, with the entry points
/// <c>VSMain</c> and <c>PSMain</c>. No format is ever relabelled as another.
/// </para>
/// </remarks>
public sealed class ShaderProgram
{
    private const string MslResourceExtension = ".metal";
    private const string MslVertexEntryPoint = "vertex_main";
    private const string MslFragmentEntryPoint = "fragment_main";
    private const string DxilVertexResourceExtension = ".vert.dxil";
    private const string DxilFragmentResourceExtension = ".frag.dxil";
    private const string DxilVertexEntryPoint = "VSMain";
    private const string DxilFragmentEntryPoint = "PSMain";

    private ShaderProgram(SDL.GPUShaderFormat format, ShaderAsset vertex, ShaderAsset fragment)
    {
        Format = format;
        Vertex = vertex;
        Fragment = fragment;
    }

    /// <summary>
    /// Gets the shader format this program's assets are in.
    /// </summary>
    public SDL.GPUShaderFormat Format { get; }

    /// <summary>
    /// Gets the vertex stage asset.
    /// </summary>
    public ShaderAsset Vertex { get; }

    /// <summary>
    /// Gets the fragment stage asset.
    /// </summary>
    public ShaderAsset Fragment { get; }

    /// <summary>
    /// Selects the shader assets for a device shader format.
    /// </summary>
    /// <param name="resourcePrefix">
    /// The logical-name prefix shared by the family's embedded resources, for
    /// example <c>HelloTriangle.Shaders.Triangle</c>.
    /// </param>
    /// <param name="deviceFormat">
    /// The format the device accepts, normally <see cref="GpuDevice.ShaderFormat"/>.
    /// </param>
    /// <returns>The vertex and fragment assets for <paramref name="deviceFormat"/>.</returns>
    /// <exception cref="ArgumentException"><paramref name="resourcePrefix"/> is empty.</exception>
    /// <exception cref="InvalidOperationException">
    /// Nymeria ships no assets for <paramref name="deviceFormat"/>.
    /// </exception>
    public static ShaderProgram Select(string resourcePrefix, SDL.GPUShaderFormat deviceFormat)
    {
        ArgumentException.ThrowIfNullOrEmpty(resourcePrefix);

        switch (deviceFormat)
        {
            case SDL.GPUShaderFormat.MSL:
                return new ShaderProgram(
                    deviceFormat,
                    new ShaderAsset(
                        resourcePrefix + MslResourceExtension,
                        deviceFormat,
                        MslVertexEntryPoint),
                    new ShaderAsset(
                        resourcePrefix + MslResourceExtension,
                        deviceFormat,
                        MslFragmentEntryPoint));

            case SDL.GPUShaderFormat.DXIL:
                return new ShaderProgram(
                    deviceFormat,
                    new ShaderAsset(
                        resourcePrefix + DxilVertexResourceExtension,
                        deviceFormat,
                        DxilVertexEntryPoint),
                    new ShaderAsset(
                        resourcePrefix + DxilFragmentResourceExtension,
                        deviceFormat,
                        DxilFragmentEntryPoint));

            default:
                throw new InvalidOperationException(
                    $"Nymeria has no shader assets in the " +
                    $"{GpuBackendSelection.DescribeFormat(deviceFormat)} format for " +
                    $"'{resourcePrefix}'. It ships Metal Shading Language source for " +
                    "SDL's Metal backend and build-generated DXIL for SDL's Direct3D " +
                    "12 backend, so a device that reports another format cannot be " +
                    "rendered with.");
        }
    }
}
