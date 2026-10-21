# Text DirectX `.x` Import

`Nymeria.Formats.DirectX` implements the `Nymeria.Core.Models.IModelParser`
contract for a focused, independently authored subset of the text DirectX
`.x` format. It converts static triangle geometry into the format-independent
`ModelData`/`MeshData` types described in [MODEL_DATA.md](MODEL_DATA.md). The
project references only the base class library and `Nymeria.Core`; it has no
SDL3, GPU, window, or platform dependency.

## Provenance and licensing

The parser was written from published format facts, not by porting an existing
loader. Microsoft's DirectX 9 `.x` template documentation defines the header,
`Frame`, `FrameTransformMatrix`, `Mesh`, `MeshNormals`, `MeshTextureCoords`,
`MeshMaterialList`, and `Material` layouts. No loader source code, comments,
translation, fixtures, or assets were copied from another project. The
`HelloX` fixture and every test input are original project content and are
redistributable under the repository's BSD 3-Clause License.

## Usage

```csharp
using Nymeria.Core.Models;
using Nymeria.Formats.DirectX;

using FileStream stream = File.OpenRead("model.x");
var parser = new XModelParser("model.x");
ModelData model = parser.Parse(stream);
```

The caller opens and owns the stream. The parser reads from the stream's current
position and does not close or dispose it, including when parsing fails. The
optional source name appears in diagnostics and does not open a file. A
non-seekable readable stream is accepted; the parser replays its header probe
without seeking.

## Supported matrix

### Header

| Field | Supported values |
| --- | --- |
| Magic | `xof` |
| Version and format token | `0302txt`, `0303txt` (also accepted as `0302 txt`, `0303 txt`) |
| Float precision | `0032`, `0064` |

Whitespace, line endings, and `//` line comments are accepted around tokens and
before the header. A UTF-8 byte-order mark is accepted. `/ * ... * /` block
comments are not part of this subset; the `*` and `/` are not specially treated,
so such a file fails with a token diagnostic.

### Objects

| Object | Behavior |
| --- | --- |
| `template <Name> { ... }` | Accepted only at the top level, before geometry, and skipped structurally. Template declarations describe schemas and contain no scene data. |
| `Frame [name] { ... }` | Supported. Frames may contain `FrameTransformMatrix`, `Mesh`, and nested `Frame` objects. All geometry is flattened into the model. |
| `FrameTransformMatrix { ... }` | Supported, affine only. Must appear at most once per frame and before that frame's `Mesh` and child `Frame` objects. |
| `Mesh [name] { ... }` | Required source of geometry. Positions, polygon faces, and the optional elements below. |
| `MeshNormals { ... }` | Optional. Per-corner normal indices, transformed and normalized. |
| `MeshTextureCoords { ... }` | Optional. One coordinate per mesh vertex, with a top-left origin and `+V` down. |
| `MeshMaterialList { ... }` | Optional. Face material indices and named `Material` objects. |
| `Material [name] { ... }` | Diffuse, specular power, specular, and emissive values are validated for syntax and finite values. Only the instance name is preserved, as a subset label. |
| `TextureFilename { "..." }` | Validated as a quoted string, then ignored. The path is never resolved, opened, or checked. Other material properties are rejected. |
| `AnimationSet`, `Animation`, `AnimationKey`, `AnimTicksPerSecond` | Rejected as animation. |
| `SkinWeights`, `XSkinMeshHeader` | Rejected as skinning. |
| `MeshVertexColors`, `VertexDuplicationIndices`, `DeclData`, `FVFData` | Rejected as unsupported mesh elements. |
| Any other object or property | Rejected with the object name and line number. Unknown data is never skipped silently. |

Binary files (`xof ... bin ...`), compressed files (`tzip` and `bzip`), and
unsupported versions, precisions, or format types are rejected from the ASCII
header before a text reader is created, so they cannot be misread as text.

### Face and numeric grammar

- Counts and indices are invariant-culture integers. Negative counts, negative
  indices, and indices outside the declared array are rejected.
- Floats are invariant-culture, finite values. `NaN`, `Infinity`, overflow to
  infinity, trailing garbage, and decimal commas are rejected.
- Face lists separate indices with commas and terminate a face with `;`. Lists
  terminate with `;;` exactly as the text format specifies.
- Positions and normals use `x;y;z;` triples separated by commas; texture
  coordinates use `u;v;` pairs.
- A material's diffuse and emissive values use four and three components
  respectively, each terminated by `;`, followed by the list's extra `;`.

## Transform, coordinate, and winding conversion

Text `.x` geometry uses Direct3D's left-handed frame: X right, Y up, Z forward.
Nymeria uses a right-handed frame with `+Y` up and `-Z` forward.

| Attribute | Conversion |
| --- | --- |
| Position | Apply the accumulated frame transform, then `(x, y, z)` → `(x, y, -z)` |
| Normal | Apply the inverse transpose of the accumulated frame transform, then negate Z and normalize |
| Triangle winding | Reverse the source face order, unless the accumulated transform has a negative determinant |
| Texture coordinate | Preserved unchanged; Direct3D already uses a top-left origin with `+V` down |
| Units | Preserved; no rescaling |

`FrameTransformMatrix` values are read row-major, and composition follows
`System.Numerics` row vectors: a child's accumulated matrix is
`local * parent`. The matrix's last row must be `0, 0, 0, 1` within `1e-6`;
other values are rejected as non-affine. A singular transform is accepted for
a mesh without normals (the geometry may collapse), but is rejected when
`MeshNormals` is present because normals cannot be transformed safely.

Each face is fan-triangulated: corners `0, 1, 2`, then `0, 2, 3`, and so on.
This is deterministic and preserves source ordering and winding for the convex
polygons that `.x` exporters produce. The parser does not test convexity,
planarity, self-intersection, or degenerate area, and it does not attempt an
ear-clipping fallback. Faces with fewer than three corners or more than the
documented corner limit are rejected. A face that cannot be represented by the
fan rule is therefore still triangulated deterministically; callers should not
assume that a concave polygon is covered correctly.

## Materials and subsets

`MeshMaterialList` assigns one material index to each mesh face. Consecutive
faces with the same index become one `MeshSubset`, preserving a triangle-aligned
draw range. The material instance name (`Material front { ... }`) becomes the
subset's opaque `MaterialName`. An unnamed material produces a subset with a
`null` name. If the mesh has no material list, it receives one subset covering
all indices with no name.

The material's diffuse, specular, and emissive colors, specular power, and
texture filename have no representation in `ModelData` and are not exposed.
They are validated so malformed values still fail, but the importer does not
claim to have imported them. No texture is loaded or searched for, and there is
no model-relative path resolution.

## Limits and diagnostics

Malformed input throws `ModelFormatException` and never returns partial
geometry. Messages include the optional source name, the line number, and the
offending token where practical, for example:

```text
broken.x: line 14: face 2 corner 1 is 7, but valid indices are 0 through 5.
```

The parser enforces these limits:

| Limit | Value |
| --- | --- |
| Input size | 64 MiB |
| Source vertices across the model | 2,000,000 |
| Combined mesh vertices across the model | 2,000,000 |
| Triangles across the model | 4,000,000 |
| Material subsets | 65,536 |
| Vertices in one face | 65,536 |
| Indices in one face list | 12,000,000 |
| Frame nesting depth | 64 |
| Frames | 4,096 |
| One token | 1,048,576 characters |
| Tokens | 32,000,000 |

Input is read through a bounded stream and a streaming token reader, so a
seekable or non-seekable source stops at the input limit and the token limit is
enforced before a hostile file can build an unbounded token collection. The
parser does not recurse without a depth limit and allocates index and vertex
buffers only after the corresponding counts and transforms are validated.

Ordinary I/O errors propagate from the caller's stream.

## Known limitations and uncertainties

- **Text only.** Binary and compressed `.x` files are rejected, not decoded.
- **Static geometry only.** Skinning, bones, animation sets, animation keys, and
  effects are rejected.
- **No hierarchy in the model.** Frame transforms are baked. `ModelData` has no
  node hierarchy, per-mesh transform, LODs, or named objects, so the original
  frame structure cannot be recovered from the result.
- **Materials are names only.** Colors, power, texture filenames, and effects are
  validated but not represented; no texture is loaded.
- **Fan triangulation only.** Concave or non-planar polygons may be triangulated
  incorrectly, although deterministically. There is no robust polygon
  triangulator.
- **Transform order.** A `FrameTransformMatrix` after geometry in the same frame
  is rejected rather than reordered or applied retroactively.
- **No real-world exporter corpus.** The checked-in fixture and tests are
  synthetic. They cover common D3DX text grammar, not every exporter's quirks.
- **No block comments and no escaped strings.** `/ * ... * /` is unsupported, and
  a quoted string ends at the next `"`; there is no escape processing.
- **Duplicate or unknown template instances fail.** Template declarations are
  skipped, but an unknown object inside a frame is an error rather than being
  ignored.

## Example and tests

[HelloX](../examples/HelloX/Program.cs) loads its checked-in
[Assets/cube.x](../examples/HelloX/Assets/cube.x) fixture, or a path passed on
the command line, and renders it through the shared `HelloOBJ`/`HelloO3D` GPU
renderer, depth test, camera, and input path.

`tests/Nymeria.Formats.DirectX.Tests` runs headlessly without SDL3, Metal, a
window, or a GPU. It covers:

- header variants, BOMs, comments, and template declarations;
- positions, winding, polygon fan triangulation, shared corners, and multiple
  meshes;
- normals, independent normal indices, normalization, and inverse-transpose
  transforms under non-uniform scale;
- texture coordinates and material subsets, including consecutive grouping and
  ignored texture filenames;
- baked parent and child frame transforms, mirrored winding, and affine
  validation;
- binary, compressed, and unsupported headers;
- missing meshes, empty meshes, empty face lists, undersized faces, invalid
  indices, negative counts, non-finite numbers, and mismatched or malformed
  attributes and materials;
- animation, skinning, unsupported mesh elements, unknown objects, unclosed
  frames, meshes, and templates;
- every configured limit, truncated prefixes, random byte mutations, invalid
  UTF-8, stream ownership, current-position parsing, and non-seekable streams;
- the checked-in cube fixture.

See [examples/README.md](../examples/README.md) for run commands and
[SMOKE_TEST.md](SMOKE_TEST.md) for the macOS rendering checklist.
