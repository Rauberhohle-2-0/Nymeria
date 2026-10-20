# Nymeria examples

The examples are small, runnable programs that demonstrate the engine on macOS.
They are intentionally separate from the reusable projects: each example owns its
geometry, shaders, pipelines, and frame loop. `Nymeria.Graphics` only provides the
GPU device, the window presentation claim, shader and buffer creation, and the
swapchain format.

Both examples use SDL3's GPU API with the Metal backend and Metal Shading
Language (MSL) source. SDL compiles the embedded MSL source when the shaders are
created, so there is no Xcode build step and no generated shader artifact.

From the repository root:

```bash
dotnet restore Nymeria.slnx
dotnet build Nymeria.slnx
dotnet run --project examples/HelloTriangle/HelloTriangle.csproj
dotnet run --project examples/HelloCube/HelloCube.csproj
```

## HelloTriangle

`HelloTriangle` is the minimal regression example. It draws one static triangle
with red, green, and blue corners into a 960 × 540 window and waits up to 100 ms
for events between frames, so idle redraws stay near ten per second.

- Geometry: three interleaved position/color vertices in one vertex buffer.
- Shader: `HelloTriangle/Shaders/Triangle.metal`, embedded as
  `HelloTriangle.Shaders.Triangle.metal`.
- Coordinates: vertices are already in clip space. There is no matrix, camera,
  or depth buffer.
- Viewport: a centered square derived from the swapchain's physical pixels, so
  the triangle keeps its proportions on resize and on high-DPI displays.
- Culling: disabled; the pipeline draws a triangle list with one color target.

## HelloCube

`HelloCube` is the next step: a rotating cube with perspective projection and a
depth buffer. It draws into a 960 × 540 window, renders continuously, and targets
about 16 ms per frame.

- Geometry: 24 vertices (four per face) and 36 16-bit indices. Corners are
  duplicated so each of the six faces has a solid color instead of colors
  blending across shared vertices.
- Shader: `HelloCube/Shaders/Cube.metal`, embedded as
  `HelloCube.Shaders.Cube.metal`.
- Animation: the cube rotates around its Y axis with a fixed X tilt. Rotation is
  advanced by elapsed time, not by frame count, and each frame's delta is capped
  at 0.1 s so a long stall cannot make the cube jump.
- Pacing: each iteration processes pending events without blocking, renders,
  then delays the remainder of the 16 ms budget. There is no busy-wait.
- Uniform data: the vertex shader declares one uniform buffer. `GpuDevice`
  passes that count to SDL, and each frame pushes one 64-byte
  model-view-projection matrix to slot 0.

### Space, matrices, and shader layout

The example's world and eye spaces are right-handed with +Y up, and the camera
looks down -Z. It uses `System.Numerics` rather than a custom math library:

- the model matrix is a Y rotation composed with a fixed X tilt;
- the view matrix is `Matrix4x4.CreateLookAt` from `(0, 0, 5)` to the origin;
- the projection is `Matrix4x4.CreatePerspectiveFieldOfView` with a 45° field of
  view, near plane 0.1, far plane 100, and the swapchain's physical aspect ratio.

`System.Numerics` uses row vectors, so matrices compose as
`model * view * projection`. The 64 bytes are uploaded unchanged.
`System.Numerics` stores matrices row-major, while MSL stores `float4x4`
column-major. Uploading the row-major bytes and multiplying `mvp * position` in
MSL is therefore correct: MSL reads the bytes as the transpose, which matches
`position * mvp` in C#. Do not transpose the matrix again in C#.

SDL3 GPU clip space has lower-left `(-1, -1)`, upper-right `(1, 1)`, and depth
`[0, 1]` with the near plane at 0. `CreatePerspectiveFieldOfView` already
produces that `0..1` depth range, which matches Metal, so no y-flip or depth
remap is applied.

### Depth and visibility

The example owns its depth target; it is not part of `Nymeria.Graphics`. At
startup it picks the first supported 2D depth format in this order:
`D32Float`, `D24Unorm`, `D16Unorm`. The pipeline enables depth testing and depth
writes with the `Less` comparison, and culling stays disabled. The depth test,
not triangle winding, resolves which cube faces are visible.

The color and depth targets are cleared each frame. The depth target is cleared
to `1.0` and not stored after the render pass. When the swapchain's physical
drawable size changes, the depth texture is recreated at the new size; the GPU is
idle before the old texture is released because earlier command buffers may still
reference it.

Resizing, zero-sized drawables, minimizing, and restoring are handled without
blocking. A minimized window skips rendering. If a swapchain texture is acquired
but the drawable has zero size, the command buffer is submitted empty, because
SDL only allows cancellation before the swapchain texture is acquired.

## Platform limits

Both examples are macOS-only for now. They require SDL's Metal backend and MSL
source; Windows and Linux need their own native SDL3 packages and shader
formats. Neither example has textures, lighting, a reusable camera, or a scene
graph. GPU output is validated manually with
[../docs/SMOKE_TEST.md](../docs/SMOKE_TEST.md), not in CI.
