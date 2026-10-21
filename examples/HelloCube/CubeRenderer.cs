using System.Numerics;
using System.Runtime.InteropServices;
using Nymeria.Graphics;
using SDL3;

namespace HelloCube;

/// <summary>
/// Draws several depth-tested cubes into a native window using SDL3's GPU API.
/// The camera controls which part of the scene is visible.
/// </summary>
/// <remarks>
/// This example uses SDL's Metal backend and Metal Shading Language (MSL)
/// source, which SDL compiles when the shaders are created. It must be created,
/// used, and disposed on the process main thread. The window must outlive the
/// renderer.
/// </remarks>
public sealed class CubeRenderer : IDisposable
{
    private const string ShaderResourceName = "HelloCube.Shaders.Cube.metal";
    private const string VertexEntryPoint = "vertex_main";
    private const string FragmentEntryPoint = "fragment_main";
    private const int VertexStride = 6 * sizeof(float);

    // Fixed cube positions make camera movement and perspective easy to verify.
    // Only the central cube rotates, preserving the previous milestone without
    // moving any cube from its position.
    private static readonly CubePlacement[] s_cubes =
    [
        new(new Vector3(0.0f, 0.0f, 0.0f), 1.2f, 0.0f, 0.45f, Spins: true),
        new(new Vector3(-3.5f, 0.0f, -1.0f), 0.7f, 0.6f, 0.1f),
        new(new Vector3(3.5f, 0.0f, -1.0f), 0.7f, -0.6f, -0.1f),
        new(new Vector3(-2.2f, 2.4f, -4.0f), 0.55f, 0.9f, 0.35f),
        new(new Vector3(2.2f, -2.4f, -4.0f), 0.55f, -0.9f, -0.35f),
        new(new Vector3(-4.5f, 1.0f, -7.0f), 0.9f, 0.3f, 0.1f),
        new(new Vector3(4.5f, -1.0f, -7.0f), 0.9f, -0.3f, -0.1f),
    ];

    private readonly IntPtr _window;
    private readonly GpuDevice _device;
    private readonly IntPtr _vertexShader;
    private readonly IntPtr _fragmentShader;
    private readonly IntPtr _vertexBuffer;
    private readonly IntPtr _indexBuffer;
    private readonly IntPtr _pipeline;
    private readonly uint _indexCount;
    private readonly SDL.GPUTextureFormat _depthFormat;
    private IntPtr _depthTexture;
    private uint _depthWidth;
    private uint _depthHeight;
    private bool _disposed;

    private CubeRenderer(
        IntPtr window,
        GpuDevice device,
        IntPtr vertexShader,
        IntPtr fragmentShader,
        IntPtr vertexBuffer,
        IntPtr indexBuffer,
        IntPtr pipeline,
        uint indexCount,
        SDL.GPUTextureFormat depthFormat)
    {
        _window = window;
        _device = device;
        _vertexShader = vertexShader;
        _fragmentShader = fragmentShader;
        _vertexBuffer = vertexBuffer;
        _indexBuffer = indexBuffer;
        _pipeline = pipeline;
        _indexCount = indexCount;
        _depthFormat = depthFormat;
    }

    /// <summary>
    /// Creates the GPU device, cube shaders, vertex and index buffers, depth
    /// format, and graphics pipeline.
    /// </summary>
    /// <param name="window">The native <c>SDL_Window*</c> handle to render into.</param>
    /// <exception cref="ArgumentException">The window handle is zero.</exception>
    /// <exception cref="InvalidOperationException">
    /// The call is not on the main thread, the Metal backend is unavailable, or
    /// GPU resource creation fails.
    /// </exception>
    public static CubeRenderer Create(IntPtr window)
    {
        if (window == IntPtr.Zero)
        {
            throw new ArgumentException("A native window handle is required.", nameof(window));
        }

        CubeModel.CubeGpuGeometry geometry =
            CubeModel.CreateGpuGeometry(CubeModel.Create());
        GpuDevice device = GpuDevice.Create(window);
        IntPtr vertexShader = IntPtr.Zero;
        IntPtr fragmentShader = IntPtr.Zero;
        IntPtr vertexBuffer = IntPtr.Zero;
        IntPtr indexBuffer = IntPtr.Zero;
        IntPtr pipeline = IntPtr.Zero;

        try
        {
            byte[] shaderCode = ReadShaderSource();
            vertexShader = device.CreateShader(
                shaderCode,
                SDL.GPUShaderStage.Vertex,
                VertexEntryPoint,
                uniformBufferCount: 1);
            fragmentShader = device.CreateShader(
                shaderCode,
                SDL.GPUShaderStage.Fragment,
                FragmentEntryPoint);
            vertexBuffer = device.CreateBuffer(
                MemoryMarshal.AsBytes(geometry.Vertices.AsSpan()),
                SDL.GPUBufferUsageFlags.Vertex);
            indexBuffer = device.CreateBuffer(
                MemoryMarshal.AsBytes(geometry.Indices.AsSpan()),
                SDL.GPUBufferUsageFlags.Index);
            SDL.GPUTextureFormat depthFormat = ChooseDepthFormat(device.Handle);
            pipeline = CreatePipeline(
                device.Handle,
                vertexShader,
                fragmentShader,
                device.SwapchainFormat,
                depthFormat);

            return new CubeRenderer(
                window,
                device,
                vertexShader,
                fragmentShader,
                vertexBuffer,
                indexBuffer,
                pipeline,
                (uint)geometry.Indices.Length,
                depthFormat);
        }
        catch
        {
            // Buffer creation submits an upload. Drain any pending work before
            // releasing resources so SDL is never asked to destroy a buffer that
            // a command buffer may still reference.
            SDL.WaitForGPUIdle(device.Handle);

            if (pipeline != IntPtr.Zero)
            {
                SDL.ReleaseGPUGraphicsPipeline(device.Handle, pipeline);
            }

            if (indexBuffer != IntPtr.Zero)
            {
                SDL.ReleaseGPUBuffer(device.Handle, indexBuffer);
            }

            if (vertexBuffer != IntPtr.Zero)
            {
                SDL.ReleaseGPUBuffer(device.Handle, vertexBuffer);
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
            throw;
        }
    }

    /// <summary>
    /// Acquires a swapchain texture and draws the scene. Frames without a
    /// drawable texture, such as while the window is minimized, are skipped.
    /// </summary>
    /// <param name="camera">The camera used to build the view and projection.</param>
    /// <param name="rotationRadians">Rotation of the central cube around its Y axis.</param>
    public void Render(Camera camera, double rotationRadians)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(camera);

        if ((SDL.GetWindowFlags(_window) & SDL.WindowFlags.Minimized) != 0)
        {
            return;
        }

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

        Span<Matrix4x4> matrixData = stackalloc Matrix4x4[1];

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

        Span<SDL.GPUBufferBinding> vertexBindings = stackalloc SDL.GPUBufferBinding[1];
        vertexBindings[0] = new SDL.GPUBufferBinding { Buffer = _vertexBuffer, Offset = 0 };
        SDL.BindGPUVertexBuffers(renderPass, 0, vertexBindings, 1);

        var indexBinding = new SDL.GPUBufferBinding { Buffer = _indexBuffer, Offset = 0 };
        SDL.BindGPUIndexBuffer(
            renderPass,
            in indexBinding,
            SDL.GPUIndexElementSize.IndexElementSize16Bit);

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

        foreach (CubePlacement cube in s_cubes)
        {
            matrixData[0] = CreateModelMatrix(cube, rotationRadians) * view * projection;
            ReadOnlySpan<byte> uniformBytes = MemoryMarshal.AsBytes(matrixData);
            SDL.PushGPUVertexUniformData(
                commandBuffer,
                0,
                uniformBytes,
                (uint)uniformBytes.Length);

            SDL.DrawGPUIndexedPrimitives(renderPass, _indexCount, 1, 0, 0, 0);
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

        SDL.ReleaseGPUGraphicsPipeline(_device.Handle, _pipeline);
        SDL.ReleaseGPUBuffer(_device.Handle, _indexBuffer);
        SDL.ReleaseGPUBuffer(_device.Handle, _vertexBuffer);
        SDL.ReleaseGPUShader(_device.Handle, _fragmentShader);
        SDL.ReleaseGPUShader(_device.Handle, _vertexShader);
        _device.Dispose();
    }

    private static byte[] ReadShaderSource()
    {
        using Stream? stream = typeof(CubeRenderer).Assembly
            .GetManifestResourceStream(ShaderResourceName);

        if (stream is null)
        {
            throw new InvalidOperationException(
                $"The embedded shader '{ShaderResourceName}' is missing. " +
                "Rebuild HelloCube so Shaders/Cube.metal is embedded.");
        }

        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
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

    private static Matrix4x4 CreateModelMatrix(
        in CubePlacement cube,
        double rotationRadians)
    {
        // System.Numerics uses row vectors, so the model matrix composes as
        // scale * rotation * translation. The example's world and eye spaces
        // are right-handed with +Y up. The view and projection come from the
        // camera, which uses System.Numerics' CreateLookAt and
        // CreatePerspectiveFieldOfView; the perspective matrix maps depth to
        // [0, 1], matching SDL3 GPU and Metal. No y-flip or depth remap is
        // applied.
        float yawRadians = cube.YawRadians +
            (cube.Spins ? (float)rotationRadians : 0.0f);

        return Matrix4x4.CreateScale(cube.Scale) *
            Matrix4x4.CreateRotationY(yawRadians) *
            Matrix4x4.CreateRotationX(cube.TiltRadians) *
            Matrix4x4.CreateTranslation(cube.Position);
    }

    private readonly record struct CubePlacement(
        Vector3 Position,
        float Scale,
        float YawRadians,
        float TiltRadians,
        bool Spins = false);

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
                Format = SDL.GPUVertexElementFormat.Float3,
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
}
