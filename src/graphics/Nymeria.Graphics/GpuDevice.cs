using System.Runtime.InteropServices;
using SDL3;

namespace Nymeria.Graphics;

/// <summary>
/// Owns an SDL3 GPU device and the presentation claim for one native window.
/// </summary>
/// <remarks>
/// Nymeria currently uses SDL's Metal backend and Metal Shading Language (MSL)
/// source, which SDL compiles when shader objects are created. Creating, using,
/// and disposing a device must happen on the process main thread. The window
/// must outlive the device.
/// </remarks>
public sealed class GpuDevice : IDisposable
{
    private readonly IntPtr _window;
    private readonly IntPtr _handle;
    private bool _disposed;

    private GpuDevice(IntPtr window, IntPtr handle, SDL.GPUTextureFormat swapchainFormat)
    {
        _window = window;
        _handle = handle;
        SwapchainFormat = swapchainFormat;
    }

    /// <summary>
    /// Gets the native <c>SDL_GPUDevice*</c> handle used to create resources and
    /// record command buffers.
    /// </summary>
    public IntPtr Handle => _handle;

    /// <summary>
    /// Gets the pixel format of the claimed window's swapchain.
    /// </summary>
    public SDL.GPUTextureFormat SwapchainFormat { get; }

    /// <summary>
    /// Creates a Metal GPU device and claims the window for presentation.
    /// </summary>
    /// <param name="window">The native <c>SDL_Window*</c> handle to render into.</param>
    /// <exception cref="ArgumentException">The window handle is zero.</exception>
    /// <exception cref="InvalidOperationException">
    /// The call is not on the main thread, the Metal backend is unavailable, or
    /// device creation fails.
    /// </exception>
    public static GpuDevice Create(IntPtr window)
    {
        if (window == IntPtr.Zero)
        {
            throw new ArgumentException("A native window handle is required.", nameof(window));
        }

        if (!SDL.IsMainThread())
        {
            throw new InvalidOperationException(
                "The GPU device must be created on the process main thread. " +
                "Call GpuDevice.Create from Main, not from a worker thread.");
        }

        if (!SDL.GPUSupportsShaderFormats(SDL.GPUShaderFormat.MSL, "metal"))
        {
            throw new InvalidOperationException(
                "The SDL3 Metal GPU backend is unavailable. Nymeria currently requires " +
                "a Metal-capable Mac and the SDL3 Metal driver. " +
                $"SDL error: {SDL.GetError()}");
        }

        IntPtr device = SDL.CreateGPUDevice(SDL.GPUShaderFormat.MSL, false, "metal");

        if (device == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                $"SDL could not create a Metal GPU device: {SDL.GetError()}");
        }

        bool isWindowClaimed = false;

        try
        {
            if (!SDL.ClaimWindowForGPUDevice(device, window))
            {
                throw new InvalidOperationException(
                    $"SDL could not claim the window for GPU presentation: {SDL.GetError()}");
            }

            isWindowClaimed = true;

            SDL.GPUTextureFormat swapchainFormat =
                SDL.GetGPUSwapchainTextureFormat(device, window);

            return new GpuDevice(window, device, swapchainFormat);
        }
        catch
        {
            if (isWindowClaimed)
            {
                SDL.ReleaseWindowFromGPUDevice(device, window);
            }

            SDL.DestroyGPUDevice(device);
            throw;
        }
    }

    /// <summary>
    /// Creates a shader from Metal Shading Language source.
    /// </summary>
    /// <param name="shaderCode">The MSL source bytes.</param>
    /// <param name="stage">The shader stage to create.</param>
    /// <param name="entryPoint">The MSL function name to use.</param>
    /// <param name="uniformBufferCount">
    /// The number of uniform buffers declared by the shader. SDL uses this
    /// count to bind data pushed with <c>SDL_PushGPU*UniformData</c>.
    /// </param>
    /// <returns>The native <c>SDL_GPUShader*</c> handle.</returns>
    /// <exception cref="InvalidOperationException">Shader creation fails.</exception>
    /// <remarks>
    /// The caller owns the returned shader and must release it before disposing
    /// this device.
    /// </remarks>
    public IntPtr CreateShader(
        byte[] shaderCode,
        SDL.GPUShaderStage stage,
        string entryPoint,
        uint uniformBufferCount = 0)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(shaderCode);
        ArgumentException.ThrowIfNullOrEmpty(entryPoint);

        IntPtr shader = SDL.CreateGPUShader(
            _handle,
            new SDL.GPUShaderCreateInfo
            {
                Format = SDL.GPUShaderFormat.MSL,
                Stage = stage,
                NumUniformBuffers = uniformBufferCount,
            },
            shaderCode,
            entryPoint);

        if (shader == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                $"SDL could not create the {stage} shader '{entryPoint}': {SDL.GetError()}");
        }

        return shader;
    }

    /// <summary>
    /// Creates a GPU buffer, uploads data into it, and returns the buffer.
    /// </summary>
    /// <param name="data">The bytes to upload.</param>
    /// <param name="usage">The buffer usage flags.</param>
    /// <returns>The native <c>SDL_GPUBuffer*</c> handle.</returns>
    /// <exception cref="ArgumentException">The data is empty.</exception>
    /// <exception cref="InvalidOperationException">Buffer creation or upload fails.</exception>
    /// <remarks>
    /// The caller owns the returned buffer and must release it before disposing
    /// this device.
    /// </remarks>
    public IntPtr CreateBuffer(ReadOnlySpan<byte> data, SDL.GPUBufferUsageFlags usage)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (data.IsEmpty)
        {
            throw new ArgumentException("Buffer data is required.", nameof(data));
        }

        uint size = (uint)data.Length;
        IntPtr buffer = SDL.CreateGPUBuffer(
            _handle,
            new SDL.GPUBufferCreateInfo
            {
                Usage = usage,
                Size = size,
            });

        if (buffer == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                $"SDL could not create the GPU buffer: {SDL.GetError()}");
        }

        IntPtr transferBuffer = IntPtr.Zero;

        try
        {
            transferBuffer = SDL.CreateGPUTransferBuffer(
                _handle,
                new SDL.GPUTransferBufferCreateInfo
                {
                    Usage = SDL.GPUTransferBufferUsage.Upload,
                    Size = size,
                });

            if (transferBuffer == IntPtr.Zero)
            {
                throw new InvalidOperationException(
                    $"SDL could not create the GPU transfer buffer: {SDL.GetError()}");
            }

            IntPtr mapped = SDL.MapGPUTransferBuffer(_handle, transferBuffer, false);

            if (mapped == IntPtr.Zero)
            {
                throw new InvalidOperationException(
                    $"SDL could not map the GPU transfer buffer: {SDL.GetError()}");
            }

            try
            {
                Marshal.Copy(data.ToArray(), 0, mapped, data.Length);
            }
            finally
            {
                SDL.UnmapGPUTransferBuffer(_handle, transferBuffer);
            }

            IntPtr commandBuffer = SDL.AcquireGPUCommandBuffer(_handle);

            if (commandBuffer == IntPtr.Zero)
            {
                throw new InvalidOperationException(
                    $"SDL could not acquire a GPU command buffer for the upload: {SDL.GetError()}");
            }

            IntPtr copyPass = SDL.BeginGPUCopyPass(commandBuffer);

            if (copyPass == IntPtr.Zero)
            {
                SDL.CancelGPUCommandBuffer(commandBuffer);
                throw new InvalidOperationException(
                    $"SDL could not begin a GPU copy pass: {SDL.GetError()}");
            }

            var source = new SDL.GPUTransferBufferLocation
            {
                TransferBuffer = transferBuffer,
                Offset = 0,
            };
            var destination = new SDL.GPUBufferRegion
            {
                Buffer = buffer,
                Offset = 0,
                Size = size,
            };

            SDL.UploadToGPUBuffer(copyPass, in source, in destination, false);
            SDL.EndGPUCopyPass(copyPass);

            if (!SDL.SubmitGPUCommandBuffer(commandBuffer))
            {
                throw new InvalidOperationException(
                    $"SDL could not submit the GPU buffer upload: {SDL.GetError()}");
            }

            return buffer;
        }
        catch
        {
            SDL.ReleaseGPUBuffer(_handle, buffer);
            throw;
        }
        finally
        {
            if (transferBuffer != IntPtr.Zero)
            {
                SDL.ReleaseGPUTransferBuffer(_handle, transferBuffer);
            }
        }
    }

    /// <summary>
    /// Waits for the GPU to become idle, releases the window claim, and destroys
    /// the device.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        SDL.WaitForGPUIdle(_handle);
        SDL.ReleaseWindowFromGPUDevice(_handle, _window);
        SDL.DestroyGPUDevice(_handle);
    }
}
