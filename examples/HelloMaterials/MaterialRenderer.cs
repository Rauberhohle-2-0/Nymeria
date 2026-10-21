using System.Numerics;
using System.Runtime.InteropServices;
using Nymeria.Core.Materials;
using Nymeria.Core.Models;
using Nymeria.Core.Scenes;
using Nymeria.Graphics;
using SDL3;

namespace HelloMaterials;

/// <summary>
/// Draws scene objects with their assigned <see cref="Material"/>, using one
/// textured and tinted pipeline and a depth buffer.
/// </summary>
/// <remarks>
/// <para>
/// This example uses SDL's Metal backend and Metal Shading Language (MSL)
/// source, which SDL compiles when the shaders are created. It must be created,
/// used, and disposed on the process main thread. The window must outlive the
/// renderer.
/// </para>
/// <para>
/// The renderer owns one GPU upload per distinct <see cref="MeshData"/> and one
/// GPU texture per distinct <see cref="Texture"/> for its lifetime. RGBA mip
/// chains and block-compressed DDS textures both upload once; compressed formats
/// are capability-checked first. Scene objects and materials only reference CPU
/// data, so several objects can share the same mesh and material without
/// duplicate uploads. Uploads happen before the frame command buffer is acquired;
/// no upload occurs per frame.
/// </para>
/// <para>
/// A material without a texture, or a mesh without texture coordinates, binds
/// a generated white fallback texture so the fragment shader still combines
/// sampled RGB with the material tint. A texture with uploaded mip levels uses
/// the mipped sampler; a single-level texture uses the sampler with a maximum
/// LOD of zero, so no absent level is ever requested.
/// </para>
/// </remarks>
public sealed class MaterialRenderer : IDisposable
{
    private const string ShaderResourceName = "HelloMaterials.Shaders.Material.metal";
    private const string VertexEntryPoint = "vertex_main";
    private const string FragmentEntryPoint = "fragment_main";
    private const int VertexStride = 5 * sizeof(float);

    private readonly IntPtr _window;
    private readonly GpuDevice _device;
    private readonly IntPtr _vertexShader;
    private readonly IntPtr _fragmentShader;
    private readonly IntPtr _pipeline;
    private readonly IntPtr _sampler;
    private readonly IntPtr _mippedSampler;
    private readonly IntPtr _fallbackTexture;
    private readonly SDL.GPUTextureFormat _depthFormat;
    private readonly Dictionary<MeshData, GpuMesh> _meshes =
        new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Texture, GpuTexture> _textures =
        new(ReferenceEqualityComparer.Instance);
    private IntPtr _depthTexture;
    private uint _depthWidth;
    private uint _depthHeight;
    private bool _disposed;

    private MaterialRenderer(
        IntPtr window,
        GpuDevice device,
        IntPtr vertexShader,
        IntPtr fragmentShader,
        IntPtr pipeline,
        IntPtr sampler,
        IntPtr mippedSampler,
        IntPtr fallbackTexture,
        SDL.GPUTextureFormat depthFormat)
    {
        _window = window;
        _device = device;
        _vertexShader = vertexShader;
        _fragmentShader = fragmentShader;
        _pipeline = pipeline;
        _sampler = sampler;
        _mippedSampler = mippedSampler;
        _fallbackTexture = fallbackTexture;
        _depthFormat = depthFormat;
    }

    /// <summary>
    /// Creates the GPU device, material shaders, sampler, white fallback texture,
    /// depth format, and graphics pipeline. Mesh and material texture uploads are
    /// created lazily when they are first drawn.
    /// </summary>
    /// <param name="window">The native <c>SDL_Window*</c> handle to render into.</param>
    /// <exception cref="ArgumentException">The window handle is zero.</exception>
    /// <exception cref="InvalidOperationException">
    /// The call is not on the main thread, the Metal backend is unavailable, or
    /// GPU resource creation fails.
    /// </exception>
    public static MaterialRenderer Create(IntPtr window)
    {
        if (window == IntPtr.Zero)
        {
            throw new ArgumentException("A native window handle is required.", nameof(window));
        }

        GpuDevice? device = null;
        IntPtr vertexShader = IntPtr.Zero;
        IntPtr fragmentShader = IntPtr.Zero;
        IntPtr pipeline = IntPtr.Zero;
        IntPtr sampler = IntPtr.Zero;
        IntPtr mippedSampler = IntPtr.Zero;
        IntPtr fallbackTexture = IntPtr.Zero;

        try
        {
            device = GpuDevice.Create(window);
            byte[] shaderCode = ReadShaderSource();
            vertexShader = device.CreateShader(
                shaderCode,
                SDL.GPUShaderStage.Vertex,
                VertexEntryPoint,
                uniformBufferCount: 1);
            fragmentShader = device.CreateShader(
                shaderCode,
                SDL.GPUShaderStage.Fragment,
                FragmentEntryPoint,
                uniformBufferCount: 1,
                samplerCount: 1);
            sampler = CreateSampler(device.Handle, useMipmaps: false);
            mippedSampler = CreateSampler(device.Handle, useMipmaps: true);
            fallbackTexture = CreateFallbackTexture(device);

            SDL.GPUTextureFormat depthFormat = ChooseDepthFormat(device.Handle);
            pipeline = CreatePipeline(
                device.Handle,
                vertexShader,
                fragmentShader,
                device.SwapchainFormat,
                depthFormat);

            return new MaterialRenderer(
                window,
                device,
                vertexShader,
                fragmentShader,
                pipeline,
                sampler,
                mippedSampler,
                fallbackTexture,
                depthFormat);
        }
        catch
        {
            if (device is not null)
            {
                SDL.WaitForGPUIdle(device.Handle);

                if (pipeline != IntPtr.Zero)
                {
                    SDL.ReleaseGPUGraphicsPipeline(device.Handle, pipeline);
                }

                if (fallbackTexture != IntPtr.Zero)
                {
                    SDL.ReleaseGPUTexture(device.Handle, fallbackTexture);
                }

                if (mippedSampler != IntPtr.Zero)
                {
                    SDL.ReleaseGPUSampler(device.Handle, mippedSampler);
                }

                if (sampler != IntPtr.Zero)
                {
                    SDL.ReleaseGPUSampler(device.Handle, sampler);
                }

                if (fragmentShader != IntPtr.Zero)
                {
                    SDL.ReleaseGPUShader(device.Handle, fragmentShader);
                }

                if (vertexShader != IntPtr.Zero)
                {
                    SDL.ReleaseGPUShader(device.Handle, vertexShader);
                }

                device.Dispose();
            }

            throw;
        }
    }

    /// <summary>
    /// Acquires a swapchain texture and draws the current scene contents. An
    /// empty scene still clears the color and depth targets. Frames without a
    /// drawable texture, such as while the window is minimized, are skipped.
    /// </summary>
    /// <param name="scene">The scene to traverse.</param>
    /// <param name="camera">The camera used to build the view and projection.</param>
    public void Render(Scene scene, Camera camera)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(camera);

        if ((SDL.GetWindowFlags(_window) & SDL.WindowFlags.Minimized) != 0)
        {
            return;
        }

        // Upload new meshes and textures before acquiring the frame command
        // buffer. Buffer and texture creation submit their own copy passes, and
        // a scene traversal that only reads object references does not allocate
        // per frame.
        EnsureResourcesUploaded(scene);

        IntPtr commandBuffer = SDL.AcquireGPUCommandBuffer(_device.Handle);

        if (commandBuffer == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                $"SDL could not acquire a GPU command buffer: {SDL.GetError()}");
        }

        // Do not wait here: a minimized or full swapchain must not block the
        // event loop. A null texture means "skip this frame", not an error.
        if (!SDL.AcquireGPUSwapchainTexture(
                commandBuffer,
                _window,
                out IntPtr swapchainTexture,
                out uint width,
                out uint height))
        {
            SDL.CancelGPUCommandBuffer(commandBuffer);
            throw new InvalidOperationException(
                $"SDL could not acquire a GPU swapchain texture: {SDL.GetError()}");
        }

        if (swapchainTexture == IntPtr.Zero)
        {
            SDL.CancelGPUCommandBuffer(commandBuffer);
            return;
        }

        // Once a swapchain texture is acquired the command buffer cannot be
        // cancelled. A zero-sized drawable is not usable, so submit it empty.
        if (width == 0 || height == 0)
        {
            SDL.SubmitGPUCommandBuffer(commandBuffer);
            return;
        }

        Matrix4x4 view;
        Matrix4x4 projection;

        try
        {
            EnsureDepthTexture(width, height);
            view = camera.ViewMatrix;
            projection = camera.CreateProjectionMatrix((float)width / height);
        }
        catch
        {
            // A swapchain texture is already acquired, so cancellation is not
            // allowed. Submit the still-empty command buffer before propagating
            // the error instead of leaking an acquired native command buffer.
            SDL.SubmitGPUCommandBuffer(commandBuffer);
            throw;
        }

        var colorTarget = new SDL.GPUColorTargetInfo
        {
            Texture = swapchainTexture,
            ClearColor = new SDL.FColor { R = 0.05f, G = 0.06f, B = 0.09f, A = 1.0f },
            LoadOp = SDL.GPULoadOp.Clear,
            StoreOp = SDL.GPUStoreOp.Store,
        };
        var depthTarget = new SDL.GPUDepthStencilTargetInfo
        {
            Texture = _depthTexture,
            ClearDepth = 1.0f,
            LoadOp = SDL.GPULoadOp.Clear,
            StoreOp = SDL.GPUStoreOp.DontCare,
            StencilLoadOp = SDL.GPULoadOp.DontCare,
            StencilStoreOp = SDL.GPUStoreOp.DontCare,
            Cycle = 1,
        };

        Span<SDL.GPUColorTargetInfo> colorTargets = stackalloc SDL.GPUColorTargetInfo[1];
        colorTargets[0] = colorTarget;

        IntPtr renderPass = SDL.BeginGPURenderPass(
            commandBuffer,
            colorTargets,
            1,
            in depthTarget);

        if (renderPass == IntPtr.Zero)
        {
            // Cancellation is not allowed once a swapchain texture has been
            // acquired, so submit the still-empty command buffer first.
            string error = SDL.GetError();
            SDL.SubmitGPUCommandBuffer(commandBuffer);
            throw new InvalidOperationException(
                $"SDL could not begin a GPU render pass: {error}");
        }

        SDL.BindGPUGraphicsPipeline(renderPass, _pipeline);

        var viewport = new SDL.GPUViewport
        {
            X = 0.0f,
            Y = 0.0f,
            W = width,
            H = height,
            MinDepth = 0.0f,
            MaxDepth = 1.0f,
        };
        var scissor = new SDL.Rect
        {
            X = 0,
            Y = 0,
            W = (int)width,
            H = (int)height,
        };

        SDL.SetGPUViewport(renderPass, in viewport);
        SDL.SetGPUScissor(renderPass, in scissor);

        Span<Matrix4x4> matrixData = stackalloc Matrix4x4[1];
        Span<Vector4> tintData = stackalloc Vector4[1];
        Span<SDL.GPUBufferBinding> vertexBindings = stackalloc SDL.GPUBufferBinding[1];
        Span<SDL.GPUTextureSamplerBinding> samplerBindings =
            stackalloc SDL.GPUTextureSamplerBinding[1];

        foreach (SceneObject sceneObject in scene.Objects)
        {
            GpuMesh mesh = _meshes[sceneObject.Mesh];
            Material material = sceneObject.Material;

            matrixData[0] =
                sceneObject.Transform.CreateModelMatrix() * view * projection;

            ReadOnlySpan<byte> uniformBytes = MemoryMarshal.AsBytes(matrixData);
            SDL.PushGPUVertexUniformData(
                commandBuffer,
                0,
                uniformBytes,
                (uint)uniformBytes.Length);

            tintData[0] = new Vector4(material.BaseColor, 1.0f);
            ReadOnlySpan<byte> tintBytes = MemoryMarshal.AsBytes(tintData);
            SDL.PushGPUFragmentUniformData(
                commandBuffer,
                0,
                tintBytes,
                (uint)tintBytes.Length);

            vertexBindings[0] = new SDL.GPUBufferBinding
            {
                Buffer = mesh.VertexBuffer,
                Offset = 0,
            };
            SDL.BindGPUVertexBuffers(renderPass, 0, vertexBindings, 1);

            var indexBinding = new SDL.GPUBufferBinding
            {
                Buffer = mesh.IndexBuffer,
                Offset = 0,
            };
            SDL.BindGPUIndexBuffer(
                renderPass,
                in indexBinding,
                SDL.GPUIndexElementSize.IndexElementSize32Bit);

            GpuTexture texture = ResolveTexture(mesh, material);
            samplerBindings[0] = new SDL.GPUTextureSamplerBinding
            {
                Texture = texture.Handle,
                Sampler = texture.UploadedMipLevels > 1 ? _mippedSampler : _sampler,
            };
            SDL.BindGPUFragmentSamplers(renderPass, 0, samplerBindings, 1);

            SDL.DrawGPUIndexedPrimitives(
                renderPass,
                mesh.IndexCount,
                1,
                0,
                0,
                0);
        }

        SDL.EndGPURenderPass(renderPass);

        if (!SDL.SubmitGPUCommandBuffer(commandBuffer))
        {
            throw new InvalidOperationException(
                $"SDL could not submit the GPU command buffer: {SDL.GetError()}");
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        SDL.WaitForGPUIdle(_device.Handle);

        if (_depthTexture != IntPtr.Zero)
        {
            SDL.ReleaseGPUTexture(_device.Handle, _depthTexture);
        }

        foreach (GpuTexture texture in _textures.Values)
        {
            SDL.ReleaseGPUTexture(_device.Handle, texture.Handle);
        }

        _textures.Clear();

        foreach (GpuMesh mesh in _meshes.Values)
        {
            SDL.ReleaseGPUBuffer(_device.Handle, mesh.IndexBuffer);
            SDL.ReleaseGPUBuffer(_device.Handle, mesh.VertexBuffer);
        }

        _meshes.Clear();

        SDL.ReleaseGPUTexture(_device.Handle, _fallbackTexture);
        SDL.ReleaseGPUSampler(_device.Handle, _mippedSampler);
        SDL.ReleaseGPUSampler(_device.Handle, _sampler);
        SDL.ReleaseGPUGraphicsPipeline(_device.Handle, _pipeline);
        SDL.ReleaseGPUShader(_device.Handle, _fragmentShader);
        SDL.ReleaseGPUShader(_device.Handle, _vertexShader);
        _device.Dispose();
    }

    private void EnsureResourcesUploaded(Scene scene)
    {
        foreach (SceneObject sceneObject in scene.Objects)
        {
            GpuMesh mesh = GetOrCreateGpuMesh(sceneObject.Mesh);
            Texture? texture = sceneObject.Material.BaseColorTexture;

            // A mesh without UVs binds the fallback texture, so uploading its
            // material's texture would only waste memory.
            if (texture is not null && mesh.HasTextureCoordinates)
            {
                GetOrCreateGpuTexture(texture);
            }
        }
    }

    private GpuMesh GetOrCreateGpuMesh(MeshData mesh)
    {
        if (_meshes.TryGetValue(mesh, out GpuMesh cached))
        {
            return cached;
        }

        MaterialGpuGeometry geometry = MaterialGpuGeometryBuilder.Build(mesh);
        IntPtr vertexBuffer = IntPtr.Zero;
        IntPtr indexBuffer = IntPtr.Zero;

        try
        {
            vertexBuffer = _device.CreateBuffer(
                MemoryMarshal.AsBytes(geometry.Vertices.AsSpan()),
                SDL.GPUBufferUsageFlags.Vertex);
            indexBuffer = _device.CreateBuffer(
                MemoryMarshal.AsBytes(geometry.Indices.AsSpan()),
                SDL.GPUBufferUsageFlags.Index);

            var gpuMesh = new GpuMesh(
                vertexBuffer,
                indexBuffer,
                (uint)geometry.Indices.Length,
                geometry.HasTextureCoordinates);
            _meshes.Add(mesh, gpuMesh);
            return gpuMesh;
        }
        catch
        {
            // Buffer creation submits an upload. Drain any pending work before
            // releasing a buffer that a command buffer may still reference.
            SDL.WaitForGPUIdle(_device.Handle);

            if (indexBuffer != IntPtr.Zero)
            {
                SDL.ReleaseGPUBuffer(_device.Handle, indexBuffer);
            }

            if (vertexBuffer != IntPtr.Zero)
            {
                SDL.ReleaseGPUBuffer(_device.Handle, vertexBuffer);
            }

            throw;
        }
    }

    private GpuTexture GetOrCreateGpuTexture(Texture texture)
    {
        if (_textures.TryGetValue(texture, out GpuTexture cached))
        {
            return cached;
        }

        IntPtr handle = UploadTexture(texture);
        int uploadedLevels = texture is CompressedTextureData compressed
            ? CountUploadableLevels(compressed)
            : texture.MipLevelCount;
        var gpuTexture = new GpuTexture(handle, uploadedLevels);
        _textures.Add(texture, gpuTexture);
        return gpuTexture;
    }

    /// <summary>
    /// Reports whether this renderer can upload and sample a material texture.
    /// </summary>
    /// <remarks>
    /// The example calls this after the device exists so an unsupported
    /// compressed DDS variant produces a diagnostic and a fallback instead of a
    /// failed frame. Decoded RGBA textures are always supported.
    /// </remarks>
    public bool SupportsTexture(Texture texture)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(texture);

        if (texture is not CompressedTextureData compressed)
        {
            return true;
        }

        if (!TryGetGpuFormat(compressed, out SDL.GPUTextureFormat format) ||
            CountUploadableLevels(compressed) == 0)
        {
            return false;
        }

        return _device.SupportsTextureFormat(format);
    }

    private IntPtr UploadTexture(Texture texture)
    {
        if (texture is CompressedTextureData compressed)
        {
            if (!TryGetGpuFormat(compressed, out SDL.GPUTextureFormat format))
            {
                throw new NotSupportedException(
                    $"The compressed texture format '{compressed.Format}' is not " +
                    "supported by the material renderer on this backend.");
            }

            int levelCount = CountUploadableLevels(compressed);

            if (levelCount == 0)
            {
                throw new NotSupportedException(
                    $"The {CompressedTextureData.GetFormatName(compressed.Format)} " +
                    $"texture is {compressed.Width} x {compressed.Height}, which is not " +
                    "block aligned. Block-compressed uploads require dimensions that " +
                    "are multiples of four.");
            }

            return UploadLevels(
                format,
                compressed.Width,
                compressed.Height,
                compressed.MipLevels,
                levelCount);
        }

        if (texture is not TextureData rgba)
        {
            throw new NotSupportedException(
                $"The texture type '{texture.GetType().Name}' is not supported by the " +
                "material renderer.");
        }

        return UploadLevels(
            SDL.GPUTextureFormat.R8G8B8A8Unorm,
            texture.Width,
            texture.Height,
            rgba.MipLevels,
            texture.MipLevelCount);
    }

    private IntPtr UploadLevels(
        SDL.GPUTextureFormat format,
        int width,
        int height,
        IReadOnlyList<ReadOnlyMemory<byte>> levels,
        int levelCount)
    {
        var regions = new GpuDevice.TextureUploadRegion[levelCount];
        int totalBytes = 0;

        for (int level = 0; level < levelCount; level++)
        {
            regions[level] = new GpuDevice.TextureUploadRegion(
                totalBytes,
                levels[level].Length,
                (uint)Math.Max(1, width >> level),
                (uint)Math.Max(1, height >> level));
            totalBytes = checked(totalBytes + levels[level].Length);
        }

        var payload = new byte[totalBytes];

        for (int level = 0; level < levelCount; level++)
        {
            levels[level].Span.CopyTo(payload.AsSpan(regions[level].Offset, regions[level].Length));
        }

        return _device.CreateTexture(
            format,
            (uint)width,
            (uint)height,
            regions,
            payload);
    }

    private static int CountUploadableLevels(CompressedTextureData texture)
    {
        int levels = 0;

        for (int level = 0; level < texture.MipLevelCount; level++)
        {
            // SDL's D3D12 and Vulkan backends disagree about block edges below
            // 4 x 4, so only a block-aligned prefix is uploaded.
            if (texture.GetMipWidth(level) % 4 != 0 || texture.GetMipHeight(level) % 4 != 0)
            {
                break;
            }

            levels++;
        }

        return levels;
    }

    private static bool TryGetGpuFormat(
        CompressedTextureData texture,
        out SDL.GPUTextureFormat format)
    {
        format = default;

        switch (texture.Format, texture.IsSrgb)
        {
            case (CompressedTextureFormat.Bc1, false):
                format = SDL.GPUTextureFormat.BC1RGBAUnorm;
                return true;
            case (CompressedTextureFormat.Bc1, true):
                format = SDL.GPUTextureFormat.BC1RGBAUnormSRGB;
                return true;
            case (CompressedTextureFormat.Bc2, false):
                format = SDL.GPUTextureFormat.BC2RGBAUnorm;
                return true;
            case (CompressedTextureFormat.Bc2, true):
                format = SDL.GPUTextureFormat.BC2RGBAUnormSRGB;
                return true;
            case (CompressedTextureFormat.Bc3, false):
                format = SDL.GPUTextureFormat.BC3RGBAUnorm;
                return true;
            case (CompressedTextureFormat.Bc3, true):
                format = SDL.GPUTextureFormat.BC3RGBAUnormSRGB;
                return true;
            case (CompressedTextureFormat.Bc4, false):
                format = SDL.GPUTextureFormat.BC4RUnorm;
                return true;
            case (CompressedTextureFormat.Bc5, false):
                format = SDL.GPUTextureFormat.BC5RGUnorm;
                return true;
            case (CompressedTextureFormat.Bc7, false):
                format = SDL.GPUTextureFormat.BC7RGBAUnorm;
                return true;
            case (CompressedTextureFormat.Bc7, true):
                format = SDL.GPUTextureFormat.BC7RGBAUnormSRGB;
                return true;
            default:
                // BC4 and BC5 have no sRGB variants in SDL's GPU API.
                return false;
        }
    }

    private GpuTexture ResolveTexture(GpuMesh mesh, Material material)
    {
        if (!mesh.HasTextureCoordinates || material.BaseColorTexture is not { } texture ||
            !_textures.TryGetValue(texture, out GpuTexture handle))
        {
            return new GpuTexture(_fallbackTexture, 1);
        }

        return handle;
    }

    private static byte[] ReadShaderSource()
    {
        using Stream? stream = typeof(MaterialRenderer).Assembly
            .GetManifestResourceStream(ShaderResourceName);

        if (stream is null)
        {
            throw new InvalidOperationException(
                $"The embedded shader '{ShaderResourceName}' is missing. " +
                "Rebuild HelloMaterials so Shaders/Material.metal is embedded.");
        }

        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    private static IntPtr CreateSampler(IntPtr device, bool useMipmaps)
    {
        // Two samplers keep the mipped path explicit: a single-level texture is
        // sampled with MaxLod 0 and can never request a level that does not
        // exist, while a texture with uploaded mips interpolates within the
        // chain SDL actually holds.
        IntPtr sampler = SDL.CreateGPUSampler(
            device,
            new SDL.GPUSamplerCreateInfo
            {
                MinFilter = SDL.GPUFilter.Linear,
                MagFilter = SDL.GPUFilter.Linear,
                MipmapMode = useMipmaps
                    ? SDL.GPUSamplerMipmapMode.Linear
                    : SDL.GPUSamplerMipmapMode.Nearest,
                AddressModeU = SDL.GPUSamplerAddressMode.ClampToEdge,
                AddressModeV = SDL.GPUSamplerAddressMode.ClampToEdge,
                AddressModeW = SDL.GPUSamplerAddressMode.ClampToEdge,
                MinLod = 0.0f,
                MaxLod = useMipmaps ? 32.0f : 0.0f,
                MaxAnisotropy = 1.0f,
                EnableAnisotropy = false,
                EnableCompare = false,
            });

        if (sampler == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                $"SDL could not create the texture sampler: {SDL.GetError()}");
        }

        return sampler;
    }

    private static IntPtr CreateFallbackTexture(GpuDevice device)
    {
        // A 1 x 1 opaque white texture leaves the material tint unchanged when
        // the shader multiplies sampled RGB by the tint. It is uploaded once and
        // shared by every untextured material and every mesh without UVs.
        ReadOnlySpan<byte> whitePixel = [255, 255, 255, 255];
        return device.CreateTexture(whitePixel, 1, 1);
    }

    private static SDL.GPUTextureFormat ChooseDepthFormat(IntPtr device)
    {
        // SDL leaves depth format choice to the app. Prefer D32Float, then the
        // other formats SDL documents as commonly supported.
        ReadOnlySpan<SDL.GPUTextureFormat> candidates =
        [
            SDL.GPUTextureFormat.D32Float,
            SDL.GPUTextureFormat.D24Unorm,
            SDL.GPUTextureFormat.D16Unorm,
        ];

        foreach (SDL.GPUTextureFormat format in candidates)
        {
            if (SDL.GPUTextureSupportsFormat(
                    device,
                    format,
                    SDL.GPUTextureType.TextureType2D,
                    SDL.GPUTextureUsageFlags.DepthStencilTarget))
            {
                return format;
            }
        }

        throw new InvalidOperationException(
            "The GPU backend supports none of the required 2D depth formats " +
            "(D32Float, D24Unorm, D16Unorm).");
    }

    private void EnsureDepthTexture(uint width, uint height)
    {
        if (_depthTexture != IntPtr.Zero && _depthWidth == width && _depthHeight == height)
        {
            return;
        }

        IntPtr newTexture = CreateDepthTexture(width, height);

        if (_depthTexture != IntPtr.Zero)
        {
            // The previous frame's command buffer may still reference the old
            // texture. Resizes are rare, so draining the GPU before releasing it
            // keeps lifetime handling simple and safe.
            if (!SDL.WaitForGPUIdle(_device.Handle))
            {
                SDL.ReleaseGPUTexture(_device.Handle, newTexture);
                throw new InvalidOperationException(
                    $"SDL could not wait for the GPU while resizing the depth texture: {SDL.GetError()}");
            }

            SDL.ReleaseGPUTexture(_device.Handle, _depthTexture);
        }

        _depthTexture = newTexture;
        _depthWidth = width;
        _depthHeight = height;
    }

    private IntPtr CreateDepthTexture(uint width, uint height)
    {
        IntPtr texture = SDL.CreateGPUTexture(
            _device.Handle,
            new SDL.GPUTextureCreateInfo
            {
                Type = SDL.GPUTextureType.TextureType2D,
                Format = _depthFormat,
                Usage = SDL.GPUTextureUsageFlags.DepthStencilTarget,
                Width = width,
                Height = height,
                LayerCountOrDepth = 1,
                NumLevels = 1,
                SampleCount = SDL.GPUSampleCount.SampleCount1,
            });

        if (texture == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                $"SDL could not create a {_depthFormat} depth texture: {SDL.GetError()}");
        }

        return texture;
    }

    private static IntPtr CreatePipeline(
        IntPtr device,
        IntPtr vertexShader,
        IntPtr fragmentShader,
        SDL.GPUTextureFormat swapchainFormat,
        SDL.GPUTextureFormat depthFormat)
    {
        var vertexBuffer = new SDL.GPUVertexBufferDescription
        {
            Slot = 0,
            Pitch = VertexStride,
            InputRate = SDL.GPUVertexInputRate.Vertex,
        };

        SDL.GPUVertexAttribute[] attributes =
        {
            new()
            {
                Location = 0,
                BufferSlot = 0,
                Format = SDL.GPUVertexElementFormat.Float3,
                Offset = 0,
            },
            new()
            {
                Location = 1,
                BufferSlot = 0,
                Format = SDL.GPUVertexElementFormat.Float2,
                Offset = 3 * sizeof(float),
            },
        };

        SDL.GPUColorTargetDescription[] colorTargets =
        {
            new() { Format = swapchainFormat },
        };

        IntPtr pipeline = SDL.CreateGPUGraphicsPipeline(
            device,
            new SDL.GPUGraphicsPipelineCreateInfo
            {
                VertexShader = vertexShader,
                FragmentShader = fragmentShader,
                PrimitiveType = SDL.GPUPrimitiveType.TriangleList,
                VertexInputState = new SDL.GPUVertexInputState
                {
                    NumVertexBuffers = 1,
                    NumVertexAttributes = 2,
                },
                RasterizerState = new SDL.GPURasterizerState
                {
                    FillMode = SDL.GPUFillMode.Fill,
                    CullMode = SDL.GPUCullMode.None,
                    FrontFace = SDL.GPUFrontFace.CounterClockwise,
                    EnableDepthClip = true,
                },
                MultisampleState = new SDL.GPUMultisampleState
                {
                    SampleCount = SDL.GPUSampleCount.SampleCount1,
                },
                DepthStencilState = new SDL.GPUDepthStencilState
                {
                    EnableDepthTest = true,
                    EnableDepthWrite = true,
                    EnableStencilTest = false,
                    CompareOp = SDL.GPUCompareOp.Less,
                },
                TargetInfo = new SDL.GPUGraphicsPipelineTargetInfo
                {
                    NumColorTargets = 1,
                    HasDepthStencilTarget = true,
                    DepthStencilFormat = depthFormat,
                },
            },
            new ReadOnlySpan<SDL.GPUVertexBufferDescription>(in vertexBuffer),
            attributes,
            colorTargets);

        if (pipeline == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                $"SDL could not create the graphics pipeline for swapchain format " +
                $"'{swapchainFormat}' and depth format '{depthFormat}': {SDL.GetError()}");
        }

        return pipeline;
    }

    private readonly record struct GpuMesh(
        IntPtr VertexBuffer,
        IntPtr IndexBuffer,
        uint IndexCount,
        bool HasTextureCoordinates);

    private readonly record struct GpuTexture(IntPtr Handle, int UploadedMipLevels);
}
