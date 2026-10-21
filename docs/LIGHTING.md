# Lighting

This document is the authoritative description of Nymeria's minimal lighting
model. It covers the scene lighting data, the ambient and Lambert equations, the
normal transformation, the shared material renderer, and the `HelloLighting`
example. Related contracts are [SCENE.md](SCENE.md), [MATERIALS.md](MATERIALS.md),
[MODEL_DATA.md](MODEL_DATA.md), and [ARCHITECTURE.md](ARCHITECTURE.md).

## Purpose and non-goals

The model answers one question: how should a material surface respond to ambient
light and one directional light? It is deliberately small:

- one ambient color and intensity per scene;
- at most one directional light with a direction, color, and intensity;
- Lambert diffuse shading on the material base color and optional base-color
  texture;
- correct normals under rotation and non-uniform scale.

There is no specular term, PBR, physically based energy conservation, shadow,
point or spot light, light collection, deferred renderer, global illumination,
shader graph, or gamma-management redesign. Alpha behavior is unchanged: texture
alpha is decoded and preserved, but the example shaders write an opaque fragment.

## Data model and ownership

The lighting types live in `src/core/Nymeria.Core/Lighting/` under
`Nymeria.Core.Lighting`. They contain only `System.Numerics` and the base class
library; they have no SDL3, GPU, window, or format dependency.

### `AmbientLight`

An immutable ambient light with a `Color` and `Intensity`.

- Color components must be finite and between 0 and 1.
- Intensity must be finite and between 0 and `AmbientLight.MaxIntensity` (100).
- `EffectiveColor` is `Color * Intensity`, ready for upload.
- The default is white at 0.1 intensity.
- Ambient light has no position or direction. It is independent of the object
  transform and camera.

### `DirectionalLight`

An immutable directional light with a travel `Direction`, `Color`, and
`Intensity`.

- `Direction` is the **direction the light travels**, in world space. A sun
  overhead traveling downward is `(0, -1, 0)`.
- The constructor rejects a zero-length or non-finite direction. A valid
  direction is normalized before storage.
- `DirectionToLight` is `-Direction`; this is the vector used by Lambert shading.
- Color and intensity use the same ranges as ambient light.
- `EffectiveColor` is `Color * Intensity`.
- The default is white at 1.0 intensity, traveling from the upper right front
  toward the lower left back.

### `SceneLighting`

`SceneLighting` combines one required `AmbientLight` and an optional
`DirectionalLight`. It is immutable. Construct a new instance to change a light
at runtime and assign it to `Scene.Lighting`.

`Scene.Lighting` defaults to `null`. A null value means **unlit**. Renderers that
support lighting use this opt-in boundary to preserve the earlier examples:

- `HelloMaterials` sets no lighting and remains visually unlit.
- `HelloLighting` assigns a `SceneLighting` every frame it changes.
- `Scene` and `SceneObject` continue to own no GPU resources.

`SceneLighting.IsUnlit` reports whether both contributions have zero intensity.

## Lighting equation

Shading is evaluated in **world space** by the fragment shader:

```text
ambientColor      = AmbientLight.Color * AmbientLight.Intensity
directionalColor  = DirectionalLight.Color * DirectionalLight.Intensity
L                 = normalize(DirectionalLight.DirectionToLight)   // world space
N                 = normalize(world-space surface normal)
diffuse           = max(0, dot(N, L))
litColor          = baseColor * (ambientColor + directionalColor * diffuse)
```

`baseColor` is the sampled texture RGB multiplied by the material `BaseColor`
tint. Lighting is applied exactly once; the tint is not applied again after
lighting. The output alpha remains `1.0`.

If `Scene.Lighting` is `null`, the shared renderer supplies a white ambient term
and no directional term. That reduces the equation to `baseColor`, which is the
pre-lighting material behavior.

If the directional light is omitted or its intensity is zero, only the ambient
term remains. Ambient-only output is independent of the surface normal, object
position, and camera orientation.

## Coordinate space and color space

- Model space is the validated `MeshData` space. World space is the space after
  `Transform.CreateModelMatrix()`.
- Direct light direction and transformed normals are both in world space. Do not
  mix a model-space normal with a world-space light direction.
- Nymeria composes matrices for `System.Numerics` row vectors:
  `model * view * projection`.
- Lighting colors and material colors are encoded values. No decoder or renderer
  performs sRGB-to-linear conversion for the material path, so the lighting math
  intentionally preserves the existing color behavior. A future color-management
  change must update the material and lighting documentation together.
- Compressed DDS sRGB variants still decode to linear values at sample time on
  the GPU, as documented in [MATERIALS.md](MATERIALS.md).

## Normal transformation

Normals cannot use the model matrix directly when the object has non-uniform
scale. `LightingMath.TryCreateNormalMatrix` computes the inverse transpose of the
full model matrix:

```text
normalMatrix = transpose(inverse(model))
N_world      = normalize(N_model * normalMatrix)
```

Because Nymeria uses row vectors, `N_model * normalMatrix` is the correct normal
transform; the shader receives the row-major bytes and MSL interprets them as
column-major, matching the existing matrix convention. A normal is uploaded with
a fourth component of zero, so the translation part of the inverse transpose does
not affect it.

The GPU geometry builder requires one finite, non-zero normal per vertex and
normalizes it before upload. A mesh without normals, or with a zero-length
normal, is rejected with a clear error instead of inventing normals or uploading
NaNs.

A singular transform, such as zero scale, has no normal matrix. The renderer
detects this with `LightingMath.TryCreateNormalMatrix` and reports the scene
object index instead of drawing it with invalid data. Negative scale is allowed
and mirrors the geometry as before; the inverse transpose keeps the normal
consistent with the mirrored surface.

The shader also guards the interpolated normal against zero length before the
dot product, so a degenerate triangle cannot produce NaN. The fragment then
treats that surface as receiving no diffuse light while preserving ambient.

## Renderer integration

The shared `examples/Shared/MaterialRenderer.cs` owns one pipeline, sampler set,
white fallback texture, depth target, mesh cache, and texture cache. It reads
`Scene.Lighting` once per frame:

- `null`: white ambient, zero directional color, zero direction.
- non-null: the ambient effective color, the directional effective color, and
  `DirectionalLight.DirectionToLight`.

Each draw uploads one vertex uniform block containing the model-view-projection
matrix and the inverse-transpose normal matrix, and one fragment uniform block
containing ambient color, direction to light, directional color, and material
tint. This uses the existing SDL3 uniform-push path; there is no new buffer
allocation per frame and no second renderer architecture.

`HelloMaterials` and `HelloLighting` both link the shared renderer and material
shader. `HelloMaterials` leaves lighting unset, so it exercises the unlit path.
`HelloLighting` sets scene lighting, so it exercises the lit path with the same
mesh, material, texture, sampler, and depth code.

Meshes remain shared by reference. The renderer caches one upload per distinct
`MeshData` and one GPU texture per distinct `Texture`, so repeated instances in
`HelloLighting` share geometry and the checkerboard upload.

## `HelloLighting`

`examples/HelloLighting` demonstrates the model with an imported OBJ mesh:

```bash
dotnet run --project examples/HelloLighting/HelloLighting.csproj
dotnet run --project examples/HelloLighting/HelloLighting.csproj -- path/to/model.obj
```

- The default mesh is the checked-in `HelloOBJ` cube fixture, copied beside the
  built executable. The OBJ contains positions, outward face normals, UVs, and
  face subsets. No OMSI or third-party asset is used.
- A user-supplied OBJ is drawn from its first mesh only. A file whose first mesh
  has no normals is rejected with a diagnostic; normals are never invented.
- The default texture is the checked-in `HelloMaterials` checkerboard PNG,
  copied beside the executable and decoded by `Nymeria.Formats.Textures`.
- Three instances share the imported mesh: two use the textured material and one
  uses a solid orange material. The middle object uses a strongly non-uniform
  scale to make an incorrect normal transform visible.
- The camera and input use the shared `Camera` and `FlyCameraControls`. Movement,
  resize, minimize, depth, frame budget, and shutdown behavior follow the other
  interactive examples.

Controls:

| Input | Action |
| --- | --- |
| Arrow left / right | Rotate the directional light around the world Y axis |
| Arrow up / down | Raise or lower the light elevation |
| `L` | Toggle between ambient-plus-directional and ambient-only |
| `[` / `]` | Decrease / increase directional intensity |
| `R` | Reset the light direction, intensity, and enabled state |
| `W`/`A`/`S`/`D`, `Space`, `Left Control`, `Shift` | Shared camera movement |
| Hold right mouse button | Shared camera look |

Ambient intensity is 0.18 in the example so the ambient-only view remains clearly
visible. Changing it is a code change to the `AmbientLight` constructor; no UI
framework is introduced.

## Tests

`tests/Nymeria.Core.Tests` covers the CPU-testable model:

- front-facing, perpendicular, and back-facing Lambert terms;
- normalization of non-unit inputs and safe handling of zero or non-finite
  normals and directions;
- ambient and directional color/intensity validation, defaults, and effective
  colors;
- ambient-plus-directional combination on a front-facing surface;
- normal matrices for identity, rotation, non-uniform scale, translation, zero
  scale, and non-finite transforms;
- `Scene.Lighting` defaulting to null and accepting replacement or clearing.

Shader compilation, pipeline layout, normal/skinning interaction, runtime light
controls, and visual output are validated by the manual
[smoke test](SMOKE_TEST.md), not by headless CI.

## Intentionally deferred

Shadows, point and spot lights, multiple directional lights, specular highlights,
physically based rendering, normal maps, gamma-management redesign, deferred
rendering, global illumination, light probes, and shader graphs are not part of
this milestone. The lighting model is one ambient term and one directional term
by design.
