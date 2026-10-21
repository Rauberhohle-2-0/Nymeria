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
    /// <summary>
    /// The largest accepted texture width or height for <see cref="CreateTexture"/>.
    /// </summary>
    /// <remarks>
    /// This is a defensive GPU-side bound. CPU texture data is normally validated
    /// earlier and has a smaller documented limit in <c>Nymeria.Core</c>.
    /// </remarks>
    public const int MaxTextureDimension = 16384;

    /// <summary>
    /// The largest accepted tightly packed RGBA payload for
    /// <see cref="CreateTexture"/>, in bytes.
    /// </summary>
    public const int MaxTextureBytes = 256 * 1024 * 1024;

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
    /// <exception cref="ObjectDisposedException">The device has been disposed.</exception>
    public IntPtr Handle
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _handle;
        }
    }

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
    /// <param name="samplerCount">
    /// The number of texture samplers declared by the shader. SDL uses this
    /// count for fragment or vertex sampler bindings.
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
        uint uniformBufferCount = 0,
        uint samplerCount = 0)
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
                NumSamplers = samplerCount,
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
    /// Creates a 2D RGBA texture, uploads tightly packed pixels through a
    /// transfer buffer, and returns the texture.
    /// </summary>
    /// <param name="rgbaPixels">
    /// Exactly <c>width * height * 4</c> bytes of row-major RGBA pixels, with
    /// the first row at texture coordinate <c>v = 0</c> (the top row).
    /// </param>
    /// <param name="width">The texture width in pixels.</param>
    /// <param name="height">The texture height in pixels.</param>
    /// <returns>The native <c>SDL_GPUTexture*</c> handle.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A dimension is zero or beyond <see cref="MaxTextureDimension"/>, or the
    /// payload exceeds <see cref="MaxTextureBytes"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// The payload length does not match the dimensions.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Texture creation, transfer, or submission fails.
    /// </exception>
    /// <remarks>
    /// <para>
    /// The texture uses <c>R8G8B8A8Unorm</c>, one mip level, and the
    /// <c>Sampler</c> usage flag. No color-space conversion or mip generation is
    /// performed. The caller owns the returned texture and must release it with
    /// <c>SDL_ReleaseGPUTexture</c> after pending GPU work has completed and
    /// before disposing this device.
    /// </para>
    /// <para>
    /// The upload submits its own command buffer once. Callers should upload
    /// each distinct texture once and reuse the handle, not upload per frame.
    /// </para>
    /// </remarks>
    public IntPtr CreateTexture(
        ReadOnlySpan<byte> rgbaPixels,
        uint width,
        uint height)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (width == 0 || height == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(width),
                width,
                "Texture dimensions must be at least one pixel.");
        }

        if (width > MaxTextureDimension || height > MaxTextureDimension)
        {
            throw new ArgumentOutOfRangeException(
                nameof(width),
                width,
                $"Texture dimensions must not exceed {MaxTextureDimension} pixels.");
        }

        ulong expectedBytes = (ulong)width * height * 4;

        if (expectedBytes > MaxTextureBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(rgbaPixels),
                expectedBytes,
                $"The texture requires {expectedBytes} bytes, which exceeds the " +
                $"{MaxTextureBytes}-byte texture limit.");
        }

        if ((ulong)rgbaPixels.Length != expectedBytes)
        {
            throw new ArgumentException(
                $"A {width} x {height} RGBA texture requires {expectedBytes} bytes, " +
                $"but the supplied payload has {rgbaPixels.Length} bytes.",
                nameof(rgbaPixels));
        }

        IntPtr texture = SDL.CreateGPUTexture(
            _handle,
            new SDL.GPUTextureCreateInfo
            {
                Type = SDL.GPUTextureType.TextureType2D,
                Format = SDL.GPUTextureFormat.R8G8B8A8Unorm,
                Usage = SDL.GPUTextureUsageFlags.Sampler,
                Width = width,
                Height = height,
                LayerCountOrDepth = 1,
                NumLevels = 1,
                SampleCount = SDL.GPUSampleCount.SampleCount1,
            });

        if (texture == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                $"SDL could not create the GPU texture: {SDL.GetError()}");
        }

        IntPtr transferBuffer = IntPtr.Zero;

        try
        {
            transferBuffer = SDL.CreateGPUTransferBuffer(
                _handle,
                new SDL.GPUTransferBufferCreateInfo
                {
                    Usage = SDL.GPUTransferBufferUsage.Upload,
                    Size = (uint)rgbaPixels.Length,
                });

            if (transferBuffer == IntPtr.Zero)
            {
                throw new InvalidOperationException(
                    $"SDL could not create the texture transfer buffer: {SDL.GetError()}");
            }

            IntPtr mapped = SDL.MapGPUTransferBuffer(_handle, transferBuffer, false);

            if (mapped == IntPtr.Zero)
            {
                throw new InvalidOperationException(
                    $"SDL could not map the texture transfer buffer: {SDL.GetError()}");
            }

            try
            {
                Marshal.Copy(rgbaPixels.ToArray(), 0, mapped, rgbaPixels.Length);
            }
            finally
            {
                SDL.UnmapGPUTransferBuffer(_handle, transferBuffer);
            }

            IntPtr commandBuffer = SDL.AcquireGPUCommandBuffer(_handle);

            if (commandBuffer == IntPtr.Zero)
            {
                throw new InvalidOperationException(
                    $"SDL could not acquire a GPU command buffer for the texture upload: " +
                    $"{SDL.GetError()}");
            }

            IntPtr copyPass = SDL.BeginGPUCopyPass(commandBuffer);

            if (copyPass == IntPtr.Zero)
            {
                SDL.CancelGPUCommandBuffer(commandBuffer);
                throw new InvalidOperationException(
                    $"SDL could not begin a GPU copy pass for the texture upload: " +
                    $"{SDL.GetError()}");
            }

            var source = new SDL.GPUTextureTransferInfo
            {
                TransferBuffer = transferBuffer,
                Offset = 0,
                PixelsPerRow = width,
                RowsPerLayer = height,
            };
            var destination = new SDL.GPUTextureRegion
            {
                Texture = texture,
                MipLevel = 0,
                Layer = 0,
                X = 0,
                Y = 0,
                Z = 0,
                W = width,
                H = height,
                D = 1,
            };

            SDL.UploadToGPUTexture(copyPass, in source, in destination, false);
            SDL.EndGPUCopyPass(copyPass);

            if (!SDL.SubmitGPUCommandBuffer(commandBuffer))
            {
                throw new InvalidOperationException(
                    $"SDL could not submit the GPU texture upload: {SDL.GetError()}");
            }

            return texture;
        }
        catch
        {
            SDL.ReleaseGPUTexture(_handle, texture);
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
    /// Describes one mip level to upload with <see cref="CreateTexture(
    /// SDL.GPUTextureFormat, uint, uint, ReadOnlySpan{TextureUploadRegion},
    /// ReadOnlySpan{byte})}"/>.
    /// </summary>
    /// <param name="Offset">The byte offset of the level inside the shared payload.</param>
    /// <param name="Length">The exact encoded length of the level, in bytes.</param>
    /// <param name="Width">The level width in pixels.</param>
    /// <param name="Height">The level height in pixels.</param>
    public readonly record struct TextureUploadRegion(
        int Offset,
        int Length,
        uint Width,
        uint Height);

    /// <summary>
    /// Reports whether the GPU backend can create and sample a texture format.
    /// </summary>
    /// <param name="format">The texture format to check.</param>
    /// <param name="usage">The usage flags the texture will be created with.</param>
    /// <returns>True when the combination is supported.</returns>
    /// <remarks>
    /// Support depends on the backend, driver, and hardware. Callers that accept
    /// optional texture formats should check this before creating a texture so a
    /// missing capability produces a clean fallback instead of a failed upload.
    /// </remarks>
    public bool SupportsTextureFormat(
        SDL.GPUTextureFormat format,
        SDL.GPUTextureUsageFlags usage = SDL.GPUTextureUsageFlags.Sampler)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        return SDL.GPUTextureSupportsFormat(
            _handle,
            format,
            SDL.GPUTextureType.TextureType2D,
            usage);
    }

    /// <summary>
    /// Creates a 2D texture from an explicit mip chain and uploads it through a
    /// transfer buffer.
    /// </summary>
    /// <param name="format">
    /// The texture format. RGBA8 and the BC1 through BC7 families, including
    /// their sRGB variants, are supported by this helper.
    /// </param>
    /// <param name="width">The base-level width in pixels.</param>
    /// <param name="height">The base-level height in pixels.</param>
    /// <param name="regions">
    /// One region per mip level, ordered from the base level down. Offsets refer
    /// to <paramref name="payload"/>.
    /// </param>
    /// <param name="payload">The concatenated encoded data for every region.</param>
    /// <returns>The native <c>SDL_GPUTexture*</c> handle.</returns>
    /// <exception cref="ArgumentException">
    /// A region is malformed, a length does not match its dimensions, or the
    /// format is not one this helper can upload.
    /// </exception>
    /// <exception cref="NotSupportedException">
    /// The backend cannot sample the requested format.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Texture creation, transfer, or submission fails.
    /// </exception>
    /// <remarks>
    /// <para>
    /// Block-compressed regions must be block aligned: their width and height must
    /// be multiples of four. SDL's D3D12 and Vulkan backends disagree about how
    /// smaller block edges are expressed, and D3D12 requires a 256-byte row pitch.
    /// Callers therefore upload a block-aligned prefix of a compressed mip chain
    /// and must not sample levels they did not upload.
    /// </para>
    /// <para>
    /// Level data is copied into one transfer buffer with each level's offset
    /// padded to 512 bytes, then uploaded in a single copy pass. Rows are tightly
    /// packed; SDL derives the row pitch from each region's dimensions. Every mip
    /// level is uploaded once; no upload happens per frame.
    /// </para>
    /// </remarks>
    public IntPtr CreateTexture(
        SDL.GPUTextureFormat format,
        uint width,
        uint height,
        ReadOnlySpan<TextureUploadRegion> regions,
        ReadOnlySpan<byte> payload)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!TryGetTexelBlockInfo(format, out int blockBytes, out int texelsPerBlock))
        {
            throw new ArgumentException(
                $"The texture format '{format}' is not uploadable through this method. " +
                "Supported formats are RGBA8 and BC1 through BC7, including their " +
                "sRGB variants.",
                nameof(format));
        }

        if (width == 0 || height == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(width),
                width,
                "Texture dimensions must be at least one pixel.");
        }

        if (width > MaxTextureDimension || height > MaxTextureDimension)
        {
            throw new ArgumentOutOfRangeException(
                nameof(width),
                width,
                $"Texture dimensions must not exceed {MaxTextureDimension} pixels.");
        }

        if (regions.IsEmpty)
        {
            throw new ArgumentException("At least one mip level is required.", nameof(regions));
        }

        if (regions.Length > GetMaxMipLevelCount(width, height))
        {
            throw new ArgumentException(
                $"A {width} x {height} texture cannot have {regions.Length} mip levels.",
                nameof(regions));
        }

        long payloadTotal = 0;

        for (int level = 0; level < regions.Length; level++)
        {
            TextureUploadRegion region = regions[level];
            uint expectedWidth = (uint)Math.Max(1, (int)(width >> level));
            uint expectedHeight = (uint)Math.Max(1, (int)(height >> level));

            if (region.Width != expectedWidth || region.Height != expectedHeight)
            {
                throw new ArgumentException(
                    $"Mip level {level} is {region.Width} x {region.Height}, but a " +
                    $"{width} x {height} texture requires {expectedWidth} x " +
                    $"{expectedHeight} at that level.",
                    nameof(regions));
            }

            bool isCompressed = texelsPerBlock > 1;

            if (isCompressed && (region.Width % 4 != 0 || region.Height % 4 != 0))
            {
                throw new ArgumentException(
                    $"Compressed mip level {level} is {region.Width} x {region.Height}. " +
                    "Block-compressed regions must be multiples of four pixels so the " +
                    "upload works on every SDL backend.",
                    nameof(regions));
            }

            long expectedBytes = isCompressed
                ? checked((long)((region.Width + 3) / 4) * ((region.Height + 3) / 4) * blockBytes)
                : checked((long)region.Width * region.Height * blockBytes);

            if (region.Length != expectedBytes)
            {
                throw new ArgumentException(
                    $"Mip level {level} requires exactly {expectedBytes} bytes, but the " +
                    $"region declares {region.Length}.",
                    nameof(regions));
            }

            if (region.Offset < 0 || region.Length <= 0 ||
                (long)region.Offset + region.Length > payload.Length)
            {
                throw new ArgumentException(
                    $"Mip level {level} lies outside the supplied payload.",
                    nameof(regions));
            }

            payloadTotal += region.Length;
        }

        if (payloadTotal > MaxTextureBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(payload),
                payloadTotal,
                $"The texture requires {payloadTotal} bytes, which exceeds the " +
                $"{MaxTextureBytes}-byte texture limit.");
        }

        if (!SupportsTextureFormat(format))
        {
            throw new NotSupportedException(
                $"The GPU backend cannot sample the texture format '{format}'. " +
                "Check GpuDevice.SupportsTextureFormat before uploading an optional " +
                "format such as a block-compressed DDS texture.");
        }

        IntPtr texture = SDL.CreateGPUTexture(
            _handle,
            new SDL.GPUTextureCreateInfo
            {
                Type = SDL.GPUTextureType.TextureType2D,
                Format = format,
                Usage = SDL.GPUTextureUsageFlags.Sampler,
                Width = width,
                Height = height,
                LayerCountOrDepth = 1,
                NumLevels = (uint)regions.Length,
                SampleCount = SDL.GPUSampleCount.SampleCount1,
            });

        if (texture == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                $"SDL could not create the {format} GPU texture: {SDL.GetError()}");
        }

        IntPtr transferBuffer = IntPtr.Zero;

        try
        {
            // Pad each level's offset so every region starts on a 512-byte
            // boundary, which avoids an internal realignment copy on D3D12.
            const int LevelAlignment = 512;
            long bufferSize = 0;
            var offsets = new int[regions.Length];

            for (int level = 0; level < regions.Length; level++)
            {
                bufferSize = (bufferSize + LevelAlignment - 1) & ~(LevelAlignment - 1);
                offsets[level] = checked((int)bufferSize);
                bufferSize += regions[level].Length;
            }

            if (bufferSize > MaxTextureBytes)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(payload),
                    bufferSize,
                    $"The aligned upload buffer would require {bufferSize} bytes, which " +
                    $"exceeds the {MaxTextureBytes}-byte limit.");
            }

            transferBuffer = SDL.CreateGPUTransferBuffer(
                _handle,
                new SDL.GPUTransferBufferCreateInfo
                {
                    Usage = SDL.GPUTransferBufferUsage.Upload,
                    Size = (uint)bufferSize,
                });

            if (transferBuffer == IntPtr.Zero)
            {
                throw new InvalidOperationException(
                    $"SDL could not create the texture transfer buffer: {SDL.GetError()}");
            }

            IntPtr mapped = SDL.MapGPUTransferBuffer(_handle, transferBuffer, false);

            if (mapped == IntPtr.Zero)
            {
                throw new InvalidOperationException(
                    $"SDL could not map the texture transfer buffer: {SDL.GetError()}");
            }

            try
            {
                for (int level = 0; level < regions.Length; level++)
                {
                    TextureUploadRegion region = regions[level];
                    IntPtr destination = IntPtr.Add(mapped, offsets[level]);
                    Marshal.Copy(payload.Slice(region.Offset, region.Length).ToArray(), 0, destination, region.Length);
                }
            }
            finally
            {
                SDL.UnmapGPUTransferBuffer(_handle, transferBuffer);
            }

            IntPtr commandBuffer = SDL.AcquireGPUCommandBuffer(_handle);

            if (commandBuffer == IntPtr.Zero)
            {
                throw new InvalidOperationException(
                    $"SDL could not acquire a GPU command buffer for the texture upload: " +
                    $"{SDL.GetError()}");
            }

            IntPtr copyPass = SDL.BeginGPUCopyPass(commandBuffer);

            if (copyPass == IntPtr.Zero)
            {
                SDL.CancelGPUCommandBuffer(commandBuffer);
                throw new InvalidOperationException(
                    $"SDL could not begin a GPU copy pass for the texture upload: " +
                    $"{SDL.GetError()}");
            }

            for (int level = 0; level < regions.Length; level++)
            {
                TextureUploadRegion region = regions[level];
                var source = new SDL.GPUTextureTransferInfo
                {
                    TransferBuffer = transferBuffer,
                    Offset = (uint)offsets[level],
                    PixelsPerRow = 0,
                    RowsPerLayer = 0,
                };
                var destination = new SDL.GPUTextureRegion
                {
                    Texture = texture,
                    MipLevel = (uint)level,
                    Layer = 0,
                    X = 0,
                    Y = 0,
                    Z = 0,
                    W = region.Width,
                    H = region.Height,
                    D = 1,
                };

                SDL.UploadToGPUTexture(copyPass, in source, in destination, false);
            }

            SDL.EndGPUCopyPass(copyPass);

            if (!SDL.SubmitGPUCommandBuffer(commandBuffer))
            {
                throw new InvalidOperationException(
                    $"SDL could not submit the GPU texture upload: {SDL.GetError()}");
            }

            return texture;
        }
        catch
        {
            SDL.ReleaseGPUTexture(_handle, texture);
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

    private static int GetMaxMipLevelCount(uint width, uint height)
    {
        uint largest = Math.Max(width, height);
        int levels = 1;

        while (largest > 1)
        {
            largest >>= 1;
            levels++;
        }

        return levels;
    }

    /// <summary>
    /// Gets the encoded bytes and texels per block for a supported upload format.
    /// </summary>
    private static bool TryGetTexelBlockInfo(
        SDL.GPUTextureFormat format,
        out int blockBytes,
        out int texelsPerBlock)
    {
        texelsPerBlock = 1;

        switch (format)
        {
            case SDL.GPUTextureFormat.R8G8B8A8Unorm:
            case SDL.GPUTextureFormat.R8G8B8A8UnormSRGB:
                blockBytes = 4;
                return true;

            case SDL.GPUTextureFormat.BC1RGBAUnorm:
            case SDL.GPUTextureFormat.BC1RGBAUnormSRGB:
            case SDL.GPUTextureFormat.BC4RUnorm:
                blockBytes = 8;
                texelsPerBlock = 4;
                return true;

            case SDL.GPUTextureFormat.BC2RGBAUnorm:
            case SDL.GPUTextureFormat.BC2RGBAUnormSRGB:
            case SDL.GPUTextureFormat.BC3RGBAUnorm:
            case SDL.GPUTextureFormat.BC3RGBAUnormSRGB:
            case SDL.GPUTextureFormat.BC5RGUnorm:
            case SDL.GPUTextureFormat.BC7RGBAUnorm:
            case SDL.GPUTextureFormat.BC7RGBAUnormSRGB:
                blockBytes = 16;
                texelsPerBlock = 4;
                return true;

            default:
                blockBytes = 0;
                return false;
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
