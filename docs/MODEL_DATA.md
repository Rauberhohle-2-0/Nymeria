# Model Data and Parser Contract

This document is the authoritative description of Nymeria's format-independent,
CPU-side model data and the boundary that format parsers implement. It does not
describe any file format. Wavefront OBJ support lives in
[OBJ.md](OBJ.md); OMSI O3D parsing remains a future task.

## Purpose and non-goals

`Nymeria.Core` owns a small model representation that both a text format and a
binary format can produce without depending on SDL3, Metal, a window, or GPU
resources. It contains no file-format parsing, format detection, material
system, texture decoding, scene graph, animation, asset cache, or renderer.

A format parser converts source geometry into these types and returns a
`ModelData`. Validation happens while those types are constructed, so an invalid
model cannot cross the parser boundary.

## Public data contract

The public types live in `src/core/Nymeria.Core/Models/` under the
`Nymeria.Core.Models` namespace.

### `ModelData`

A model is a non-empty, named list of meshes. The name is used for diagnostics.

```csharp
var model = new ModelData(
    "Cube",
    new[]
    {
        new MeshData(
            positions,
            normals,
            textureCoordinates,
            indices,
            subsets),
    });
```

`ModelData` does not contain node transforms, a scene hierarchy, materials,
textures, animation, or renderer state. A parser must apply or bake any source
transforms needed to express the geometry in one model space.

### `MeshData`

A mesh contains parallel vertex arrays and a triangle index buffer:

| Property | Type | Required | Meaning |
| --- | --- | --- | --- |
| `Positions` | `ReadOnlyMemory<Vector3>` | Yes | One model-space position per vertex. Non-empty and finite. |
| `Normals` | `ReadOnlyMemory<Vector3>` | No | One model-space normal per vertex, or empty. Finite when present. |
| `TextureCoordinates` | `ReadOnlyMemory<Vector2>` | No | One UV per vertex, or empty. Finite when present. |
| `Indices` | `ReadOnlyMemory<uint>` | Yes | Zero-based triangle indices. Non-empty and divisible by three. |
| `Subsets` | `IReadOnlyList<MeshSubset>` | Yes | Draw ranges. Always contains at least one subset. |

Normals and texture coordinates must either be empty or match the position
count. The representation has no vertex colors, tangents, skinning weights,
morph targets, or per-vertex material indices.

The constructor takes ownership of the supplied memory and stores it without
copying. Callers must not mutate the arrays after constructing a `MeshData`.
Small metadata such as the mesh and subset lists is copied or wrapped so the
model's structure cannot be changed through the caller's list.

### `MeshSubset`

A subset is a triangle-aligned range in the mesh index buffer with an optional,
opaque material name:

```csharp
new MeshSubset(indexOffset: 0, indexCount: 6, materialName: "front")
```

- `IndexOffset` and `IndexCount` must describe whole triangles inside the index
  buffer.
- `MaterialName` is `null` or a non-empty source name such as an OBJ `usemtl`
  value. It is not resolved by the model and has no associated material data.
- When no subsets are supplied, `MeshData` creates one subset covering the whole
  index buffer with no material name.
- Subsets may describe any triangle-aligned part of the mesh. The foundation
  does not require them to cover every index or forbid overlap; a parser should
  produce disjoint, complete subsets when the source has meaningful groups.

## Coordinate and index conventions

All loaders must convert source data to these conventions before constructing a
`MeshData`:

| Aspect | Convention |
| --- | --- |
| Handedness | Right-handed. |
| Up axis | `+Y`. |
| Forward axis | `-Z`, matching `Nymeria.Graphics.Camera` and `HelloCube`. |
| Position and normal space | One model space. No node hierarchy or per-mesh transform is stored. |
| Units | Source units are preserved. The foundation does not rescale. |
| Normal length | Expected to be unit length, but only finite values are validated. |
| Primitive type | Triangle list only. |
| Indices | Zero-based `uint`, three per triangle, no negative or one-based values. |
| Winding | Counter-clockwise front faces when viewed from outside, matching SDL3's `CounterClockwise` front-face setting. Culling is currently disabled, but loaders must not rely on that. |
| Texture coordinates | `Vector2` with `(0, 0)` at the top-left, `+U` right, and `+V` down. |

Format-specific conversions belong in the parser. The Wavefront OBJ parser
resolves one-based and negative indices, converts texture coordinates from OBJ's
bottom-left origin (`v = 1 - v`), fan-triangulates its documented convex polygon
subset, and preserves OBJ's right-handed counter-clockwise winding. A future OMSI
O3D parser must convert that format's coordinate system, handedness, winding, and
index width. The core types must not grow format-specific branches for either
source. See [OBJ.md](OBJ.md) for the complete OBJ subset.

## Validation boundary and error behavior

`MeshData` and `ModelData` constructors are the validation boundary. They throw
`ModelFormatException` when content violates an invariant:

- a model has no meshes or a null mesh;
- a mesh has no positions or no indices;
- the index count is not divisible by three;
- a position, normal, or texture coordinate is not finite;
- normals or texture coordinates are present but do not match the position
  count;
- an index is outside the position array;
- a subset range is negative, empty, not triangle-aligned, or outside the index
  buffer;
- a subset has an empty material name.

`ModelFormatException` derives from `FormatException`. It is the expected failure
for malformed or unsupported source content and includes a message that
identifies the offending value or index where practical. Parsers must throw it
rather than returning partial or best-effort geometry.

Null arguments and other API misuse still use the standard .NET exceptions
(`ArgumentNullException`, `ArgumentException`, and so on). I/O errors from the
caller's stream may propagate unchanged.

Validation deliberately does not reject degenerate triangles, duplicate
positions, non-unit-length normals, self-intersections, missing subset coverage,
or overlapping subsets. Those are content-quality decisions for a specific
parser or later tooling, not basic structural invariants.

## Parser contract and stream ownership

`IModelParser` is the only contract between a format parser and the common model
data:

```csharp
public interface IModelParser
{
    ModelData Parse(Stream stream);
}
```

Rules:

- The caller opens the stream and owns its lifetime.
- The parser reads from the stream's current position.
- The parser must not dispose or close the stream, even when parsing fails.
- The parser returns a fully validated `ModelData` or throws
  `ModelFormatException`.
- The stream may be a file stream, memory stream, or another readable stream.
  A parser that needs seeking is responsible for buffering or documenting that
  requirement in its own implementation; the foundation does not require
  seekability.

Opening a file is ordinary caller code:

```csharp
IModelParser parser = CreateFormatParser();
using FileStream stream = File.OpenRead(path);
ModelData model = parser.Parse(stream);
```

There is no parser registry, reflection-based discovery, dependency-injection
container, plugin loader, or format auto-detection. Callers construct the parser
they need.

## Extension point for format parsers

Task 06.1 (Wavefront OBJ) adds a format-specific implementation of
`IModelParser` in `src/formats/Nymeria.Formats.Obj`. Task 06.2 (OMSI O3D) remains
a future task. Format-specific implementations:

1. reference `Nymeria.Core`;
2. must not reference `Nymeria.Graphics` or `Nymeria.Platform`;
3. convert source geometry into `MeshData`/`MeshSubset`/`ModelData` in the
   conventions above;
4. let the constructors reject invalid geometry;
5. keep format knowledge out of `Nymeria.Core` so it can later be extracted or
   versioned independently.

The OBJ parser is the first production parser. Its supported subset, conversions,
and limitations are documented in [OBJ.md](OBJ.md). The core test project still
contains a contract test double; format-specific behavior is tested headlessly in
`tests/Nymeria.Formats.Obj.Tests` without SDL3 or a GPU.

## Rendering integration

`Nymeria.Core` has no GPU code. The existing `HelloCube` example builds its cube
as `ModelData`, including normals, texture coordinates, six face subsets, and
material names. It converts that model to the interleaved position/color layout
and 16-bit index buffer already used by its shader, then uploads through the
existing `GpuDevice.CreateBuffer` path.

`HelloOBJ` does the same with an OBJ file parsed by `Nymeria.Formats.Obj`. It
converts the parsed model to its own position/color layout and a 32-bit index
buffer, then uses the existing `GpuDevice`, depth test, camera, and pipeline
patterns. Both conversions live in the examples because they are tied to each
example's shader and pipeline, not to the model data.

A future renderer should perform the same kind of explicit conversion for its
own vertex layout. Loading model data must remain independent of GPU resource
creation and disposal.

## Tests

`tests/Nymeria.Core.Tests` runs without SDL3, a window, or a GPU. It covers valid
geometry, optional attributes, each validation invariant, subset rules, error
type semantics, and a small `IModelParser` test double.

`tests/Nymeria.Formats.Obj.Tests` is also headless and covers the OBJ subset and
its diagnostics. It requires neither SDL3 nor a GPU. `Nymeria.Platform.Tests`
remains unchanged.
