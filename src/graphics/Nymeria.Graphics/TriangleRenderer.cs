using System.Runtime.InteropServices;
using SDL3;

namespace Nymeria.Graphics;

/// <summary>
/// Draws one colored triangle into a native window using SDL3's GPU API.
/// </summary>
/// <remarks>
/// This renderer uses SDL's Metal backend and Metal Shading Language (MSL)
/// source, which SDL compiles when the shaders are created. It must be created,
/// used, and disposed on the process main thread. The window must outlive the
/// renderer.
/// </remarks>
public sealed class TriangleRenderer : IDisposable
{
    private const string ShaderResourceName = "Nymeria.Graphics.Shaders.Triangle.metal";
    private const string VertexEntryPoint = "vertex_main";
    private const string FragmentEntryPoint = "fragment_main";
    private const int VertexStride = 6 * sizeof(float);

    private static readonly float[] s_vertices =
    {
        // X      Y      Z      R      G      B
         0.0f,  0.6f,  0.0f,  1.0f,  0.0f,  0.0f,
        -0.6f, -0.4f,  0.0f,  0.0f,  1.0f,  0.0f,
         0.6f, -0.4f,  0.0f,  0.0f,  0.0f,  1.0f,
    };

    private readonly IntPtr _window;
    private readonly IntPtr _device;
    private readonly IntPtr _vertexShader;
    private readonly IntPtr _fragmentShader;
    private readonly IntPtr _vertexBuffer;
    private readonly IntPtr _pipeline;
    private bool _disposed;

    private TriangleRenderer(
        IntPtr window,
        IntPtr device,
        IntPtr vertexShader,
        IntPtr fragmentShader,
        IntPtr vertexBuffer,
        IntPtr pipeline)
    {
        _window = window;
        _device = device;
        _vertexShader = vertexShader;
        _fragmentShader = fragmentShader;
        _vertexBuffer = vertexBuffer;
        _pipeline = pipeline;
    }

    /// <summary>
    /// Creates a Metal GPU device, claims the window for presentation, and
    /// creates the shaders, vertex buffer, and graphics pipeline.
    /// </summary>
    /// <param name="window">The native <c>SDL_Window*</c> handle to render into.</param>
    /// <exception cref="ArgumentException">The window handle is zero.</exception>
    /// <exception cref="InvalidOperationException">
    /// The call is not on the main thread, the Metal backend is unavailable, or
    /// GPU resource creation fails.
    /// </exception>
    public static TriangleRenderer Create(IntPtr window)
    {
        if (window == IntPtr.Zero)
        {
            throw new ArgumentException("A native window handle is required.", nameof(window));
        }

        if (!SDL.IsMainThread())
        {
            throw new InvalidOperationException(
                "The GPU device must be created on the process main thread. " +
                "Call TriangleRenderer.Create from Main, not from a worker thread.");
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
        IntPtr vertexShader = IntPtr.Zero;
        IntPtr fragmentShader = IntPtr.Zero;
        IntPtr vertexBuffer = IntPtr.Zero;
        IntPtr pipeline = IntPtr.Zero;

        try
        {
            if (!SDL.ClaimWindowForGPUDevice(device, window))
            {
                throw new InvalidOperationException(
                    $"SDL could not claim the window for GPU presentation: {SDL.GetError()}");
            }

            isWindowClaimed = true;

            byte[] shaderCode = ReadShaderSource();
            vertexShader = CreateShader(device, shaderCode, SDL.GPUShaderStage.Vertex, VertexEntryPoint);
            fragmentShader = CreateShader(device, shaderCode, SDL.GPUShaderStage.Fragment, FragmentEntryPoint);
            vertexBuffer = CreateVertexBuffer(device);

            SDL.GPUTextureFormat swapchainFormat = SDL.GetGPUSwapchainTextureFormat(device, window);
            pipeline = CreatePipeline(device, vertexShader, fragmentShader, swapchainFormat);

            return new TriangleRenderer(
                window,
                device,
                vertexShader,
                fragmentShader,
                vertexBuffer,
                pipeline);
        }
        catch
        {
            if (pipeline != IntPtr.Zero)
            {
                SDL.ReleaseGPUGraphicsPipeline(device, pipeline);
            }

            if (vertexBuffer != IntPtr.Zero)
            {
                SDL.ReleaseGPUBuffer(device, vertexBuffer);
            }

            if (fragmentShader != IntPtr.Zero)
            {
                SDL.ReleaseGPUShader(device, fragmentShader);
            }

            if (vertexShader != IntPtr.Zero)
            {
                SDL.ReleaseGPUShader(device, vertexShader);
            }

            if (isWindowClaimed)
            {
                SDL.ReleaseWindowFromGPUDevice(device, window);
            }

            SDL.DestroyGPUDevice(device);
            throw;
        }
    }

    /// <summary>
    /// Acquires a swapchain texture and draws one triangle. Frames without a
    /// drawable texture, such as while the window is minimized, are skipped.
    /// </summary>
    public void Render()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if ((SDL.GetWindowFlags(_window) & SDL.WindowFlags.Minimized) != 0)
        {
            return;
        }

        IntPtr commandBuffer = SDL.AcquireGPUCommandBuffer(_device);

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

        if (swapchainTexture == IntPtr.Zero || width == 0 || height == 0)
        {
            SDL.CancelGPUCommandBuffer(commandBuffer);
            return;
        }

        var colorTarget = new SDL.GPUColorTargetInfo
        {
            Texture = swapchainTexture,
            ClearColor = new SDL.FColor { R = 0.05f, G = 0.06f, B = 0.09f, A = 1.0f },
            LoadOp = SDL.GPULoadOp.Clear,
            StoreOp = SDL.GPUStoreOp.Store,
        };

        Span<SDL.GPUColorTargetInfo> colorTargets = stackalloc SDL.GPUColorTargetInfo[1];
        colorTargets[0] = colorTarget;

        IntPtr renderPass = SDL.BeginGPURenderPass(
            commandBuffer,
            colorTargets,
            1,
            IntPtr.Zero);

        if (renderPass == IntPtr.Zero)
        {
            SDL.CancelGPUCommandBuffer(commandBuffer);
            throw new InvalidOperationException(
                $"SDL could not begin a GPU render pass: {SDL.GetError()}");
        }

        SDL.BindGPUGraphicsPipeline(renderPass, _pipeline);

        Span<SDL.GPUBufferBinding> vertexBindings = stackalloc SDL.GPUBufferBinding[1];
        vertexBindings[0] = new SDL.GPUBufferBinding { Buffer = _vertexBuffer, Offset = 0 };
        SDL.BindGPUVertexBuffers(renderPass, 0, vertexBindings, 1);

        // Use a centered square in physical pixels so the triangle keeps its
        // proportions on resize and on high-DPI drawables.
        float side = MathF.Min(width, height);
        var viewport = new SDL.GPUViewport
        {
            X = (width - side) * 0.5f,
            Y = (height - side) * 0.5f,
            W = side,
            H = side,
            MinDepth = 0.0f,
            MaxDepth = 1.0f,
        };
        var scissor = new SDL.Rect
        {
            X = (int)viewport.X,
            Y = (int)viewport.Y,
            W = (int)side,
            H = (int)side,
        };

        SDL.SetGPUViewport(renderPass, in viewport);
        SDL.SetGPUScissor(renderPass, in scissor);
        SDL.DrawGPUPrimitives(renderPass, 3, 1, 0, 0);
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

        SDL.WaitForGPUIdle(_device);
        SDL.ReleaseGPUGraphicsPipeline(_device, _pipeline);
        SDL.ReleaseGPUBuffer(_device, _vertexBuffer);
        SDL.ReleaseGPUShader(_device, _fragmentShader);
        SDL.ReleaseGPUShader(_device, _vertexShader);
        SDL.ReleaseWindowFromGPUDevice(_device, _window);
        SDL.DestroyGPUDevice(_device);
    }

    private static byte[] ReadShaderSource()
    {
        using Stream? stream = typeof(TriangleRenderer).Assembly
            .GetManifestResourceStream(ShaderResourceName);

        if (stream is null)
        {
            throw new InvalidOperationException(
                $"The embedded shader '{ShaderResourceName}' is missing. " +
                "Rebuild Nymeria.Graphics so Shaders/Triangle.metal is embedded.");
        }

        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    private static IntPtr CreateShader(
        IntPtr device,
        byte[] shaderCode,
        SDL.GPUShaderStage stage,
        string entryPoint)
    {
        IntPtr shader = SDL.CreateGPUShader(
            device,
            new SDL.GPUShaderCreateInfo
            {
                Format = SDL.GPUShaderFormat.MSL,
                Stage = stage,
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

    private static IntPtr CreateVertexBuffer(IntPtr device)
    {
        uint size = (uint)(s_vertices.Length * sizeof(float));
        IntPtr buffer = SDL.CreateGPUBuffer(
            device,
            new SDL.GPUBufferCreateInfo
            {
                Usage = SDL.GPUBufferUsageFlags.Vertex,
                Size = size,
            });

        if (buffer == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                $"SDL could not create the vertex buffer: {SDL.GetError()}");
        }

        IntPtr transferBuffer = IntPtr.Zero;

        try
        {
            transferBuffer = SDL.CreateGPUTransferBuffer(
                device,
                new SDL.GPUTransferBufferCreateInfo
                {
                    Usage = SDL.GPUTransferBufferUsage.Upload,
                    Size = size,
                });

            if (transferBuffer == IntPtr.Zero)
            {
                throw new InvalidOperationException(
                    $"SDL could not create the vertex transfer buffer: {SDL.GetError()}");
            }

            IntPtr mapped = SDL.MapGPUTransferBuffer(device, transferBuffer, false);

            if (mapped == IntPtr.Zero)
            {
                throw new InvalidOperationException(
                    $"SDL could not map the vertex transfer buffer: {SDL.GetError()}");
            }

            try
            {
                Marshal.Copy(s_vertices, 0, mapped, s_vertices.Length);
            }
            finally
            {
                SDL.UnmapGPUTransferBuffer(device, transferBuffer);
            }

            IntPtr commandBuffer = SDL.AcquireGPUCommandBuffer(device);

            if (commandBuffer == IntPtr.Zero)
            {
                throw new InvalidOperationException(
                    $"SDL could not acquire a GPU command buffer for the vertex upload: {SDL.GetError()}");
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
                    $"SDL could not submit the vertex upload: {SDL.GetError()}");
            }

            return buffer;
        }
        catch
        {
            SDL.ReleaseGPUBuffer(device, buffer);
            throw;
        }
        finally
        {
            if (transferBuffer != IntPtr.Zero)
            {
                SDL.ReleaseGPUTransferBuffer(device, transferBuffer);
            }
        }
    }

    private static IntPtr CreatePipeline(
        IntPtr device,
        IntPtr vertexShader,
        IntPtr fragmentShader,
        SDL.GPUTextureFormat swapchainFormat)
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
                },
                MultisampleState = new SDL.GPUMultisampleState
                {
                    SampleCount = SDL.GPUSampleCount.SampleCount1,
                },
                TargetInfo = new SDL.GPUGraphicsPipelineTargetInfo
                {
                    NumColorTargets = 1,
                },
            },
            new ReadOnlySpan<SDL.GPUVertexBufferDescription>(in vertexBuffer),
            attributes,
            colorTargets);

        if (pipeline == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                $"SDL could not create the graphics pipeline for swapchain format " +
                $"'{swapchainFormat}': {SDL.GetError()}");
        }

        return pipeline;
    }
}
