using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using Nymeria.Core.Culling;
using Nymeria.Core.Lighting;
using Nymeria.Core.Lod;
using Nymeria.Core.Materials;
using Nymeria.Core.Models;
using Nymeria.Core.Rendering;
using Nymeria.Core.Scenes;
using Nymeria.Graphics;
using SDL3;

namespace Nymeria.Examples;

/// <summary>
/// Draws scene objects with their assigned <see cref="Material"/>, optional
/// scene lighting, one textured and tinted pipeline, and a depth buffer.
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
/// <para>
/// <see cref="Scene.Lighting"/> is optional. When it is null the renderer is
/// unlit: it adds a white ambient term and no directional term, so the material
/// base color is shown exactly as in the earlier material milestone. When it is
/// set, ambient and directional Lambert terms are evaluated in world space. The
/// renderer requires per-vertex normals and builds each object's inverse-transpose
/// normal matrix on the CPU; a singular or non-finite transform is reported
/// instead of uploading invalid data.
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
/// <para>
/// Optional per-object <see cref="MeshLod"/> configurations select one explicit
/// mesh variant per object per frame. Every distinct variant is uploaded once
/// and shared by reference, like any other mesh. Distance culling skips an
/// instance only when it is strictly beyond its configured maximum distance.
/// Objects without a configuration follow the original single-mesh path and are
/// never distance-culled. See <c>docs/LOD.md</c>.
/// </para>
/// <para>
/// Mesh and texture uploads are cached for the renderer's lifetime by default.
/// A caller that removes objects, such as a scene streamer unloading a chunk, can
/// call <see cref="PruneUnusedResources"/> to release caches that the current
/// scene no longer references. Pruning waits for the GPU to become idle before
/// releasing anything, so it belongs on an occasional unload path, not in the
/// per-frame draw loop.
/// </para>
/// <para>
/// Transparency follows <see cref="Material.Transparency"/>. Opaque and masked
/// instances share the depth-writing pipeline and are drawn in scene order; the
/// fragment shader applies the mask cutoff. Blended instances are collected into
/// a reusable per-frame list, sorted back to front with
/// <see cref="BlendOrder"/>, and drawn after the opaque phase through a second
/// pipeline that keeps depth testing but disables depth writes and enables
/// straight source-over blending. Effective alpha is the sampled texture alpha
/// times <see cref="Material.Alpha"/>. Neither phase uploads a resource or
/// allocates once the instance lists have grown to their steady size.
/// </para>
/// </remarks>
public sealed class MaterialRenderer : IDisposable
{
    private const string ShaderResourcePrefix = "Nymeria.Examples.Shaders.Material";
    private const int VertexStride = 8 * sizeof(float);

    private readonly IntPtr _window;
    private readonly GpuDevice _device;
    private readonly IntPtr _vertexShader;
    private readonly IntPtr _fragmentShader;
    private readonly IntPtr _opaquePipeline;
    private readonly IntPtr _blendPipeline;
    private readonly IntPtr _sampler;
    private readonly IntPtr _mippedSampler;
    private readonly IntPtr _fallbackTexture;
    private readonly SDL.GPUTextureFormat _depthFormat;
    private readonly Dictionary<MeshData, GpuMesh> _meshes =
        new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Texture, GpuTexture> _textures =
        new(ReferenceEqualityComparer.Instance);
    private readonly SceneVisibilityCounter _visibility = new();
    private readonly Frustum _cullingFrustum = new();
    private readonly List<BlendedInstance> _blended = new();
    private BlendOrderEntry[] _blendOrder = [];
    private IntPtr _depthTexture;
    private uint _depthWidth;
    private uint _depthHeight;
    private bool _disposed;

    private MaterialRenderer(
        IntPtr window,
        GpuDevice device,
        IntPtr vertexShader,
        IntPtr fragmentShader,
        IntPtr opaquePipeline,
        IntPtr blendPipeline,
        IntPtr sampler,
        IntPtr mippedSampler,
        IntPtr fallbackTexture,
        SDL.GPUTextureFormat depthFormat)
    {
        _window = window;
        _device = device;
        _vertexShader = vertexShader;
        _fragmentShader = fragmentShader;
        _opaquePipeline = opaquePipeline;
        _blendPipeline = blendPipeline;
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
    /// The call is not on the main thread, the platform GPU backend is
    /// unavailable, no shader assets exist for the device's shader format, or GPU
    /// resource creation fails.
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
        IntPtr opaquePipeline = IntPtr.Zero;
        IntPtr blendPipeline = IntPtr.Zero;
        IntPtr sampler = IntPtr.Zero;
        IntPtr mippedSampler = IntPtr.Zero;
        IntPtr fallbackTexture = IntPtr.Zero;

        try
        {
            device = GpuDevice.Create(window);
            Assembly assembly = typeof(MaterialRenderer).Assembly;
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
                program.Vertex.EntryPoint,
                uniformBufferCount: 1);
            fragmentShader = device.CreateShader(
                fragmentCode,
                program.Fragment.Format,
                SDL.GPUShaderStage.Fragment,
                program.Fragment.EntryPoint,
                uniformBufferCount: 1,
                samplerCount: 1);
            sampler = CreateSampler(device.Handle, useMipmaps: false);
            mippedSampler = CreateSampler(device.Handle, useMipmaps: true);
            fallbackTexture = CreateFallbackTexture(device);

            SDL.GPUTextureFormat depthFormat = ChooseDepthFormat(device.Handle);
            opaquePipeline = CreatePipeline(
                device.Handle,
                vertexShader,
                fragmentShader,
                device.SwapchainFormat,
                depthFormat,
                enableBlending: false);
            blendPipeline = CreatePipeline(
                device.Handle,
                vertexShader,
                fragmentShader,
                device.SwapchainFormat,
                depthFormat,
                enableBlending: true);

            return new MaterialRenderer(
                window,
                device,
                vertexShader,
                fragmentShader,
                opaquePipeline,
                blendPipeline,
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

                if (blendPipeline != IntPtr.Zero)
                {
                    SDL.ReleaseGPUGraphicsPipeline(device.Handle, blendPipeline);
                }

                if (opaquePipeline != IntPtr.Zero)
                {
                    SDL.ReleaseGPUGraphicsPipeline(device.Handle, opaquePipeline);
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
    /// Gets or sets whether fully out-of-view scene instances are skipped before
    /// draw submission.
    /// </summary>
    /// <remarks>
    /// Culling is disabled by default so existing examples keep their previous
    /// behavior. It only skips draw submission for an instance; scene objects,
    /// meshes, materials, and textures remain loaded. The test is conservative:
    /// intersecting or uncertain instances are still drawn. See
    /// <c>docs/CULLING.md</c>.
    /// </remarks>
    public bool FrustumCullingEnabled { get; set; }

    /// <summary>
    /// Gets or sets whether configured per-object LOD and distance culling are
    /// active.
    /// </summary>
    /// <remarks>
    /// The default is <see langword="true"/>. It only affects objects with a
    /// <see cref="SceneObject.Lod"/> configuration; objects without one render
    /// exactly as before. When set to false, every object draws its primary
    /// mesh, distance culling is bypassed, and each object's stored LOD level is
    /// reset. Enabling it again therefore starts from a fresh selection.
    /// </remarks>
    public bool LodEnabled { get; set; } = true;

    /// <summary>
    /// Gets or sets whether an object's configured maximum render distance is
    /// enforced while LOD is active.
    /// </summary>
    /// <remarks>
    /// The default is <see langword="true"/>. Disabling it keeps LOD selection
    /// but draws instances at any distance. Objects without a maximum render
    /// distance are never affected.
    /// </remarks>
    public bool DistanceCullingEnabled { get; set; } = true;

    /// <summary>
    /// Gets instance, draw-call, LOD, and submitted-triangle counts for the most
    /// recent frame that reached command submission.
    /// </summary>
    /// <remarks>
    /// <see cref="VisibilityStatistics.TotalInstances"/>,
    /// <see cref="VisibilityStatistics.VisibleInstances"/>,
    /// <see cref="VisibilityStatistics.CulledInstances"/>, and
    /// <see cref="VisibilityStatistics.DistanceCulledInstances"/> count scene
    /// instances, not GPU work. <see cref="VisibilityStatistics.DrawCalls"/> and
    /// <see cref="VisibilityStatistics.SubmittedTriangles"/> describe the work
    /// actually submitted. <see cref="VisibilityStatistics.LodDrawnInstances"/>,
    /// <see cref="VisibilityStatistics.LodUsage"/>,
    /// and <see cref="VisibilityStatistics.DrawCalls"/> are reset and recomputed
    /// each frame. A frame skipped for a minimized window or an unusable drawable
    /// leaves this value at zero.
    /// </remarks>
    public VisibilityStatistics LastFrameStatistics { get; private set; }

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

        // Counters describe one frame only. A skipped frame reports no work.
        _visibility.Reset();
        LastFrameStatistics = default;

        if ((SDL.GetWindowFlags(_window) & SDL.WindowFlags.Minimized) != 0)
        {
            return;
        }

        // Upload new meshes and textures before acquiring the frame command
        // buffer. Buffer and texture creation submit their own copy passes.
        // Upload bookkeeping allocates no per-object state, but the traversal
        // itself boxes one enumerator per pass over Scene.Objects; the benchmark
        // reports that residual, documented in docs/CULLING.md.
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
        bool cullingEnabled = FrustumCullingEnabled;
        Frustum? frustum;

        try
        {
            EnsureDepthTexture(width, height);
            view = camera.ViewMatrix;
            projection = camera.CreateProjectionMatrix((float)width / height);

            if (cullingEnabled)
            {
                // Reuse the renderer-owned scratch frustum so an enabled cull
                // path does not allocate a frustum and plane array every frame.
                _cullingFrustum.Update(view * projection);
            }

            frustum = cullingEnabled ? _cullingFrustum : null;
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

        SceneLighting? lighting = scene.Lighting;
        DirectionalLight? directional = lighting?.Directional;
        Vector4 ambientColor = lighting is null
            ? Vector4.One
            : new Vector4(lighting.Ambient.EffectiveColor, 1.0f);
        Vector4 lightDirectionToLight = directional is null
            ? Vector4.Zero
            : new Vector4(directional.DirectionToLight, 0.0f);
        Vector4 lightColor = directional is null
            ? Vector4.Zero
            : new Vector4(directional.EffectiveColor, 0.0f);

        int drawCalls = 0;
        long submittedTriangles = 0;
        bool lodEnabled = LodEnabled;
        bool distanceCullingEnabled = DistanceCullingEnabled;
        Vector3 cameraPosition = camera.Position;

        // Opaque and masked instances share the depth-writing pipeline and are
        // submitted in scene order, so previous frames keep their draw order.
        // Blended instances are collected and drawn afterwards, farthest first.
        SDL.BindGPUGraphicsPipeline(renderPass, _opaquePipeline);

        _blended.Clear();

        for (int objectIndex = 0; objectIndex < scene.Objects.Count; objectIndex++)
        {
            SceneObject sceneObject = scene.Objects[objectIndex];

            if (!_visibility.Test(
                    sceneObject,
                    frustum,
                    cameraPosition,
                    lodEnabled,
                    distanceCullingEnabled,
                    out Matrix4x4 model,
                    out MeshData selectedMesh))
            {
                continue;
            }

            GpuMesh mesh = _meshes[selectedMesh];
            Material material = sceneObject.Material;

            if (material.RequiresBlendOrdering)
            {
                Vector3 center = Vector3.Transform(
                    (selectedMesh.Bounds.Min + selectedMesh.Bounds.Max) * 0.5f,
                    model);
                _blended.Add(new BlendedInstance(
                    objectIndex,
                    model,
                    mesh,
                    material,
                    center));
                continue;
            }

            DrawInstance(
                renderPass,
                commandBuffer,
                objectIndex,
                in mesh,
                material,
                model,
                view,
                projection,
                ambientColor,
                lightDirectionToLight,
                lightColor);
            drawCalls++;
            submittedTriangles += mesh.IndexCount / 3;
        }

        if (_blended.Count > 0)
        {
            (int blendedDrawCalls, long blendedTriangles) = DrawBlendedInstances(
                renderPass,
                commandBuffer,
                view,
                projection,
                cameraPosition,
                ambientColor,
                lightDirectionToLight,
                lightColor);
            drawCalls += blendedDrawCalls;
            submittedTriangles += blendedTriangles;
        }

        SDL.EndGPURenderPass(renderPass);

        if (!SDL.SubmitGPUCommandBuffer(commandBuffer))
        {
            throw new InvalidOperationException(
                $"SDL could not submit the GPU command buffer: {SDL.GetError()}");
        }

        LastFrameStatistics = new VisibilityStatistics(
            _visibility.TotalInstances,
            _visibility.VisibleInstances,
            _visibility.CulledInstances,
            drawCalls,
            _visibility.DistanceCulledInstances,
            _visibility.LodDrawnInstances,
            submittedTriangles,
            _visibility.LodLevelUsage);
    }

    private void DrawInstance(
        IntPtr renderPass,
        IntPtr commandBuffer,
        int objectIndex,
        in GpuMesh mesh,
        Material material,
        Matrix4x4 model,
        Matrix4x4 view,
        Matrix4x4 projection,
        Vector4 ambientColor,
        Vector4 lightDirectionToLight,
        Vector4 lightColor)
    {
        if (!LightingMath.TryCreateNormalMatrix(model, out Matrix4x4 normalMatrix))
        {
            // A swapchain texture and render pass are already acquired, so
            // the command buffer must be submitted before propagating the
            // error; SDL no longer permits cancelling it at this point.
            string message =
                $"The model transform for scene object {objectIndex} is singular " +
                "or non-finite, so a normal matrix cannot be computed for lighting.";

            SDL.EndGPURenderPass(renderPass);

            if (!SDL.SubmitGPUCommandBuffer(commandBuffer))
            {
                throw new InvalidOperationException(
                    $"{message} SDL also could not submit the GPU command buffer: " +
                    $"{SDL.GetError()}");
            }

            throw new InvalidOperationException(message);
        }

        Span<VertexUniforms> vertexUniforms = stackalloc VertexUniforms[1];
        vertexUniforms[0] = new VertexUniforms
        {
            Mvp = model * view * projection,
            NormalMatrix = normalMatrix,
        };

        ReadOnlySpan<byte> vertexBytes = MemoryMarshal.AsBytes(vertexUniforms);
        SDL.PushGPUVertexUniformData(
            commandBuffer,
            0,
            vertexBytes,
            (uint)vertexBytes.Length);

        Span<FragmentUniforms> fragmentUniforms = stackalloc FragmentUniforms[1];
        fragmentUniforms[0] = new FragmentUniforms
        {
            AmbientColor = ambientColor,
            LightDirectionToLight = lightDirectionToLight,
            LightColor = lightColor,
            Tint = new Vector4(material.BaseColor, material.Alpha),
            AlphaParams = new Vector4(
                material.AlphaCutoff,
                (float)material.Transparency,
                0.0f,
                0.0f),
        };

        ReadOnlySpan<byte> fragmentBytes = MemoryMarshal.AsBytes(fragmentUniforms);
        SDL.PushGPUFragmentUniformData(
            commandBuffer,
            0,
            fragmentBytes,
            (uint)fragmentBytes.Length);

        Span<SDL.GPUBufferBinding> vertexBindings = stackalloc SDL.GPUBufferBinding[1];
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
        Span<SDL.GPUTextureSamplerBinding> samplerBindings =
            stackalloc SDL.GPUTextureSamplerBinding[1];
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

    private (int DrawCalls, long Triangles) DrawBlendedInstances(
        IntPtr renderPass,
        IntPtr commandBuffer,
        Matrix4x4 view,
        Matrix4x4 projection,
        Vector3 cameraPosition,
        Vector4 ambientColor,
        Vector4 lightDirectionToLight,
        Vector4 lightColor)
    {
        int count = _blended.Count;

        if (_blendOrder.Length < count)
        {
            // Growth happens only when the blended instance count reaches a new
            // high-water mark, so a steady scene does not allocate per frame.
            _blendOrder = new BlendOrderEntry[count];
        }

        for (int i = 0; i < count; i++)
        {
            _blendOrder[i] = new BlendOrderEntry(i, _blended[i].Center);
        }

        BlendOrder.SortBackToFront(_blendOrder.AsSpan(0, count), cameraPosition);

        // The blend pipeline keeps the depth test but stops writing depth, so
        // already drawn opaque geometry occludes blended instances while the
        // blended instances never occlude each other. Ordering is handled here.
        SDL.BindGPUGraphicsPipeline(renderPass, _blendPipeline);

        long triangles = 0;

        for (int i = 0; i < count; i++)
        {
            BlendedInstance instance = _blended[_blendOrder[i].Index];
            GpuMesh mesh = instance.Mesh;

            DrawInstance(
                renderPass,
                commandBuffer,
                instance.ObjectIndex,
                in mesh,
                instance.Material,
                instance.Model,
                view,
                projection,
                ambientColor,
                lightDirectionToLight,
                lightColor);
            triangles += instance.Mesh.IndexCount / 3;
        }

        return (count, triangles);
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
        SDL.ReleaseGPUGraphicsPipeline(_device.Handle, _blendPipeline);
        SDL.ReleaseGPUGraphicsPipeline(_device.Handle, _opaquePipeline);
        SDL.ReleaseGPUShader(_device.Handle, _fragmentShader);
        SDL.ReleaseGPUShader(_device.Handle, _vertexShader);
        _device.Dispose();
    }

    private void EnsureResourcesUploaded(Scene scene)
    {
        bool lodEnabled = LodEnabled;

        foreach (SceneObject sceneObject in scene.Objects)
        {
            GpuMesh mesh = GetOrCreateGpuMesh(sceneObject.Mesh);
            bool hasTextureCoordinates = mesh.HasTextureCoordinates;

            // Every explicit variant gets its own one-time upload. The cache is
            // keyed by MeshData reference, so sharing a variant between objects
            // or configurations never duplicates a buffer.
            if (lodEnabled && sceneObject.Lod is { } lod)
            {
                for (int level = 0; level < lod.Levels.Count; level++)
                {
                    GpuMesh variant = GetOrCreateGpuMesh(lod.Levels[level].Mesh);
                    hasTextureCoordinates |= variant.HasTextureCoordinates;
                }
            }

            Texture? texture = sceneObject.Material.BaseColorTexture;

            // A mesh without UVs binds the fallback texture, so uploading its
            // material's texture would only waste memory. Any selectable variant
            // with UVs may need the texture, so check all of them.
            if (texture is not null && hasTextureCoordinates)
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
    /// Releases cached GPU meshes and textures that the supplied scene no longer
    /// references.
    /// </summary>
    /// <param name="scene">
    /// The scene whose current objects define the live mesh and texture set.
    /// </param>
    /// <returns>
    /// The number of cached mesh and texture entries released. A mesh entry owns
    /// one vertex buffer and one index buffer.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Every object's primary mesh, every mesh variant in its optional
    /// <see cref="MeshLod"/>, and every material base-color texture is considered
    /// referenced, regardless of the current culling, LOD, or distance toggles.
    /// This is deliberately conservative: enabling a variant later must not find
    /// its buffers missing. Shared resources remain cached while any current
    /// object references them.
    /// </para>
    /// <para>
    /// The method waits for the GPU to become idle before releasing a cache entry,
    /// because a previous frame's submitted command buffer may still reference
    /// it. Release it only when no other scene is depending on this renderer's
    /// cache. A released resource is uploaded again if a later scene references
    /// it; the CPU mesh and texture data are never modified or disposed.
    /// </para>
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The renderer is disposed.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="scene"/> is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// SDL could not wait for the GPU to become idle. No cache entry is released
    /// in that case.
    /// </exception>
    public int PruneUnusedResources(Scene scene)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(scene);

        var referencedMeshes = new HashSet<MeshData>(ReferenceEqualityComparer.Instance);
        var referencedTextures = new HashSet<Texture>(ReferenceEqualityComparer.Instance);

        foreach (SceneObject sceneObject in scene.Objects)
        {
            referencedMeshes.Add(sceneObject.Mesh);

            if (sceneObject.Lod is { } lod)
            {
                foreach (MeshLodLevel level in lod.Levels)
                {
                    referencedMeshes.Add(level.Mesh);
                }
            }

            if (sceneObject.Material.BaseColorTexture is { } texture)
            {
                referencedTextures.Add(texture);
            }
        }

        var meshesToRelease = new List<MeshData>();
        var texturesToRelease = new List<Texture>();

        foreach (MeshData mesh in _meshes.Keys)
        {
            if (!referencedMeshes.Contains(mesh))
            {
                meshesToRelease.Add(mesh);
            }
        }

        foreach (Texture texture in _textures.Keys)
        {
            if (!referencedTextures.Contains(texture))
            {
                texturesToRelease.Add(texture);
            }
        }

        if (meshesToRelease.Count == 0 && texturesToRelease.Count == 0)
        {
            return 0;
        }

        if (!SDL.WaitForGPUIdle(_device.Handle))
        {
            throw new InvalidOperationException(
                $"SDL could not wait for the GPU while pruning unused resources: " +
                $"{SDL.GetError()}");
        }

        foreach (MeshData mesh in meshesToRelease)
        {
            GpuMesh gpuMesh = _meshes[mesh];
            SDL.ReleaseGPUBuffer(_device.Handle, gpuMesh.IndexBuffer);
            SDL.ReleaseGPUBuffer(_device.Handle, gpuMesh.VertexBuffer);
            _meshes.Remove(mesh);
        }

        foreach (Texture texture in texturesToRelease)
        {
            GpuTexture gpuTexture = _textures[texture];
            SDL.ReleaseGPUTexture(_device.Handle, gpuTexture.Handle);
            _textures.Remove(texture);
        }

        return meshesToRelease.Count + texturesToRelease.Count;
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
        SDL.GPUTextureFormat depthFormat,
        bool enableBlending)
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
            new()
            {
                Location = 2,
                BufferSlot = 0,
                Format = SDL.GPUVertexElementFormat.Float2,
                Offset = 6 * sizeof(float),
            },
        };

        // The blend pipeline composites straight source-over; the opaque and
        // mask pipelines leave blending disabled.
        SDL.GPUColorTargetBlendState blendState = enableBlending
            ? BlendState.SourceOver
            : default;

        SDL.GPUColorTargetDescription[] colorTargets =
        {
            new()
            {
                Format = swapchainFormat,
                BlendState = blendState,
            },
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
                    NumVertexAttributes = 3,
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
                // Masked instances share this state: the cutoff is applied by
                // discarding in the shader, which needs depth writes so the
                // surviving fragments occlude later geometry.
                DepthStencilState = new SDL.GPUDepthStencilState
                {
                    EnableDepthTest = true,
                    EnableDepthWrite = !enableBlending,
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

    /// <summary>
    /// Instance and draw-call counts for one submitted frame.
    /// </summary>
    /// <remarks>
    /// <see cref="VisibleInstances"/> counts scene instances that passed the
    /// visibility test, independent of how many primitives or GPU commands they
    /// produce. <see cref="DrawCalls"/> counts submitted indexed draw calls; this
    /// renderer issues one per visible instance. Neither value is a GPU timing.
    /// </remarks>
    public readonly record struct VisibilityStatistics(
        int TotalInstances,
        int VisibleInstances,
        int CulledInstances,
        int DrawCalls,
        int DistanceCulledInstances = 0,
        int LodDrawnInstances = 0,
        long SubmittedTriangles = 0,
        LodLevelUsage LodUsage = default);

    private readonly record struct GpuMesh(
        IntPtr VertexBuffer,
        IntPtr IndexBuffer,
        uint IndexCount,
        bool HasTextureCoordinates);

    [StructLayout(LayoutKind.Sequential)]
    private struct VertexUniforms
    {
        public Matrix4x4 Mvp;
        public Matrix4x4 NormalMatrix;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FragmentUniforms
    {
        public Vector4 AmbientColor;
        public Vector4 LightDirectionToLight;
        public Vector4 LightColor;

        // Alpha is the material opacity; RGB is the base-color tint.
        public Vector4 Tint;

        // X is the mask cutoff and Y is the MaterialTransparency value. Z and W
        // are reserved so the struct stays a whole number of 16-byte rows and
        // matches the MSL float4 layout.
        public Vector4 AlphaParams;
    }

    private readonly record struct GpuTexture(IntPtr Handle, int UploadedMipLevels);

    /// <summary>
    /// A blended instance collected during the opaque phase and drawn later in
    /// back-to-front order.
    /// </summary>
    private readonly record struct BlendedInstance(
        int ObjectIndex,
        Matrix4x4 Model,
        GpuMesh Mesh,
        Material Material,
        Vector3 Center);
}
