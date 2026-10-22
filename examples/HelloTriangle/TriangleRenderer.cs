using System.Reflection;
using System.Runtime.InteropServices;
using Nymeria.Graphics;
using SDL3;

namespace HelloTriangle;

/// <summary>
/// Draws one colored triangle into a native window using SDL3's GPU API.
/// </summary>
/// <remarks>
/// <para>
/// The shader assets follow the format the device reports: Metal takes Metal
/// Shading Language source, which SDL compiles when the shaders are created, and
/// Direct3D 12 takes build-generated DXIL bytecode. Neither format is used in
/// place of the other.
/// </para>
/// <para>
/// It must be created, used, and disposed on the process main thread. The window
/// must outlive the renderer.
/// </para>
/// </remarks>
public sealed class TriangleRenderer : IDisposable
{
    private const string ShaderResourcePrefix = "HelloTriangle.Shaders.Triangle";
    private const int VertexStride = 6 * sizeof(float);

    private static readonly float[] s_vertices =
    {
        // X      Y      Z      R      G      B
         0.0f,  0.6f,  0.0f,  1.0f,  0.0f,  0.0f,
        -0.6f, -0.4f,  0.0f,  0.0f,  1.0f,  0.0f,
         0.6f, -0.4f,  0.0f,  0.0f,  0.0f,  1.0f,
    };

    private readonly IntPtr _window;
    private readonly GpuDevice _device;
    private readonly IntPtr _vertexShader;
    private readonly IntPtr _fragmentShader;
    private readonly IntPtr _vertexBuffer;
    private readonly IntPtr _pipeline;
    private bool _disposed;

    private TriangleRenderer(
        IntPtr window,
        GpuDevice device,
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
    /// Creates the GPU device, triangle shaders, vertex buffer, and graphics
    /// pipeline.
    /// </summary>
    /// <param name="window">The native <c>SDL_Window*</c> handle to render into.</param>
    /// <exception cref="ArgumentException">The window handle is zero.</exception>
    /// <exception cref="InvalidOperationException">
    /// The call is not on the main thread, the platform backend is unavailable,
    /// no shader assets exist for the device's format, or GPU resource creation
    /// fails.
    /// </exception>
    public static TriangleRenderer Create(IntPtr window)
    {
        if (window == IntPtr.Zero)
        {
            throw new ArgumentException("A native window handle is required.", nameof(window));
        }

        GpuDevice device = GpuDevice.Create(window);
        IntPtr vertexShader = IntPtr.Zero;
        IntPtr fragmentShader = IntPtr.Zero;
        IntPtr vertexBuffer = IntPtr.Zero;
        IntPtr pipeline = IntPtr.Zero;

        try
        {
            Assembly assembly = typeof(TriangleRenderer).Assembly;
            ShaderProgram program = ShaderProgram.Select(ShaderResourcePrefix, device.ShaderFormat);
            byte[] vertexCode = program.Vertex.ReadBytes(assembly);

            // Metal keeps both stages in one source resource, so read it once.
            byte[] fragmentCode = program.Fragment.ResourceName == program.Vertex.ResourceName
                ? vertexCode
                : program.Fragment.ReadBytes(assembly);

            vertexShader = device.CreateShader(
                vertexCode,
                program.Vertex.Format,
                SDL.GPUShaderStage.Vertex,
                program.Vertex.EntryPoint);
            fragmentShader = device.CreateShader(
                fragmentCode,
                program.Fragment.Format,
                SDL.GPUShaderStage.Fragment,
                program.Fragment.EntryPoint);
            vertexBuffer = device.CreateBuffer(
                MemoryMarshal.AsBytes(s_vertices.AsSpan()),
                SDL.GPUBufferUsageFlags.Vertex);
            pipeline = CreatePipeline(
                device.Handle,
                vertexShader,
                fragmentShader,
                device.SwapchainFormat);

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
            // Buffer creation submits an upload. Drain any pending work before
            // releasing resources so SDL is never asked to destroy a buffer that
            // a command buffer may still reference.
            SDL.WaitForGPUIdle(device.Handle);

            if (pipeline != IntPtr.Zero)
            {
                SDL.ReleaseGPUGraphicsPipeline(device.Handle, pipeline);
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

        SDL.WaitForGPUIdle(_device.Handle);
        SDL.ReleaseGPUGraphicsPipeline(_device.Handle, _pipeline);
        SDL.ReleaseGPUBuffer(_device.Handle, _vertexBuffer);
        SDL.ReleaseGPUShader(_device.Handle, _fragmentShader);
        SDL.ReleaseGPUShader(_device.Handle, _vertexShader);
        _device.Dispose();
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
