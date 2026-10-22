using System.Reflection;
using SDL3;

namespace Nymeria.Graphics;

/// <summary>
/// One shader stage's compiled artifact for a single shader bytecode format.
/// </summary>
/// <remarks>
/// An asset names the embedded resource that holds the code, the format the code
/// is in, and the entry point to create the shader with. Keeping the three
/// together means a caller never has to guess a format-specific entry point or
/// hand MSL bytes to a device that expects DXIL.
/// </remarks>
public sealed class ShaderAsset
{
    internal ShaderAsset(string resourceName, SDL.GPUShaderFormat format, string entryPoint)
    {
        ResourceName = resourceName;
        Format = format;
        EntryPoint = entryPoint;
    }

    /// <summary>
    /// Gets the logical name of the embedded resource that holds this stage's code.
    /// </summary>
    public string ResourceName { get; }

    /// <summary>
    /// Gets the shader format the embedded bytes are in.
    /// </summary>
    /// <remarks>
    /// This is the bytecode format of the asset, not a claim about the device. It
    /// must equal <see cref="GpuDevice.ShaderFormat"/> before the bytes are
    /// submitted; <see cref="GpuDevice.CreateShader(byte[], SDL.GPUShaderFormat,
    /// SDL.GPUShaderStage, string, uint, uint)"/> enforces that.
    /// </remarks>
    public SDL.GPUShaderFormat Format { get; }

    /// <summary>
    /// Gets the entry point this stage is created with.
    /// </summary>
    /// <remarks>
    /// DXIL bakes the entry point into the bytecode, but the name is still passed
    /// to SDL so it matches the shader the code was compiled from.
    /// </remarks>
    public string EntryPoint { get; }

    /// <summary>
    /// Reads the embedded shader code for this asset.
    /// </summary>
    /// <param name="assembly">The assembly that embeds the shader resource.</param>
    /// <returns>The shader bytes in <see cref="Format"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="assembly"/> is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// The assembly does not embed <see cref="ResourceName"/>.
    /// </exception>
    public byte[] ReadBytes(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        using Stream? stream = assembly.GetManifestResourceStream(ResourceName);

        if (stream is null)
        {
            throw new InvalidOperationException(
                $"The embedded {GpuBackendSelection.DescribeFormat(Format)} shader " +
                $"'{ResourceName}' is missing from '{assembly.GetName().Name}'. " +
                "Rebuild the project that owns this shader so the resource is " +
                "embedded; on Windows the DXIL resources are generated from HLSL " +
                "by the DirectX Shader Compiler during the build.");
        }

        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }
}
