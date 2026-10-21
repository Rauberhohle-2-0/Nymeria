# Wavefront OBJ Import

`Nymeria.Formats.Obj` implements the `Nymeria.Core.Models.IModelParser` contract
for a focused subset of Wavefront OBJ. It converts source geometry into the
format-independent `ModelData`/`MeshData` types described in
[MODEL_DATA.md](MODEL_DATA.md). The parser uses only the base class library and
`Nymeria.Core`; it has no SDL3, GPU, window, or platform dependency.

## Usage

```csharp
using Nymeria.Core.Models;
using Nymeria.Formats.Obj;

using FileStream stream = File.OpenRead("model.obj");
var parser = new ObjModelParser("model.obj");
ModelData model = parser.Parse(stream);
```

The caller opens and owns the stream. The parser reads from the stream's current
position and does not close or dispose it, including when parsing fails. The
optional parser source name appears in diagnostics and does not open a file.

## Supported subset

| Directive | Behavior |
| --- | --- |
| `v x y z [w]` | Reads a position. The optional fourth rational weight is validated but ignored for polygon output. |
| `vt u [v] [w]` | Reads a texture coordinate. `v` defaults to `0`; the optional third value is validated but ignored. |
| `vn x y z` | Reads a normal. |
| `f ...` | Reads a face with three or more vertices. |
| `usemtl name` | Starts a new mesh subset labeled with the opaque name. An empty `usemtl` clears the current label. |
| `o name` | The first object name becomes `ModelData.Name`. Objects do not create separate meshes. |
| `g`, `s`, `vp` | Recognized and ignored. They do not affect geometry. |
| `mtllib`, `maplib`, `usemap` | Recognized and ignored. No MTL or texture map is loaded. |
| `l`, `p`, `curv`, `curv2`, `surf` | Rejected as unsupported geometry. |
| Anything else | Rejected as an unsupported directive. |

Comments start with `#` and run to the end of the line. Blank lines, leading and
trailing whitespace, tabs, and `\n`, `\r\n`, and `\r` line endings are accepted.
Numbers are parsed with the invariant culture. Input must be valid UTF-8; a UTF-8
byte-order mark is accepted, but invalid byte sequences are rejected with
`ModelFormatException` instead of being replaced.

### Face references

All four standard forms are supported:

```text
f 1 2 3
f 1/1 2/2 3/3
f 1//1 2//2 3//3
f 1/1/1 2/2/2 3/3/3
```

Position, texture-coordinate, and normal indices are independent streams. A
corner key is the resolved `(position, texture coordinate, normal)` triple, so
shared corners are deduplicated only when all three indices match. A corner with
the same position but a different UV or normal becomes a separate mesh vertex.

### Index rules

- OBJ indices are one-based. Index `0` is invalid.
- Positive indices are absolute.
- Negative indices are relative to the number of values defined before the face
  line, as specified by OBJ. For example, `-1` is the most recently defined
  position, UV, or normal at that point in the file.
- Every resolved index must lie inside the corresponding stream. Out-of-range
  indices are rejected with the source line and offending reference.
- Referenced values must already be defined before the face line. Forward
  references are rejected, which keeps negative-index resolution unambiguous and
  matches ordinary polygonal OBJ files.

### Triangulation

Faces with three vertices become one triangle. Faces with four or more vertices
are fan-triangulated from the first vertex:

```text
f 1 2 3 4 5  ->  (1,2,3), (1,3,4), (1,4,5)
```

The strategy is deterministic and linear in the face size. It is correct for
convex, planar polygons. The parser does not detect concave or non-planar faces;
such input is outside the supported subset and may produce triangles outside the
surface or with the wrong orientation. It is not silently tessellated by a
general-purpose algorithm. The source winding is preserved.

### Missing texture coordinates and normals

The model representation requires each optional attribute stream to be either
empty or present for every vertex.

- If no face references `vt`, the mesh has no texture coordinates.
- If no face references `vn`, the mesh has no normals.
- If at least one corner references `vt` or `vn`, the corresponding stream is
  present for the whole mesh. Corners that omit that attribute receive the
  documented default: `(0, 0)` for texture coordinates and `Vector3.Zero` for
  normals.

The parser does not generate normals, compute tangents, smooth faces, or invent
lighting. A zero normal is data from the conversion policy, not a shading
decision.

## Coordinate and attribute conversions

OBJ is treated as right-handed with `+Y` up and `-Z` forward, which matches the
Nymeria model convention. No handedness, axis, or winding conversion is applied.
Units are preserved.

OBJ texture coordinates use a bottom-left origin. Nymeria model data uses a
top-left origin with `+V` down, so the parser stores `(u, 1 - v)`. Out-of-range
UV values are preserved.

## Model structure and materials

The parser produces exactly one mesh containing all faces. OBJ objects and groups
are not turned into separate meshes or subsets because the Task 06 representation
has one mesh and triangle-aligned draw ranges only. The first `o` name is used as
the model name when present; otherwise the parser source name, then `"OBJ"`, is
used.

`usemtl` names are preserved as `MeshSubset.MaterialName` labels, which is the
existing representation's natural material hook. `mtllib` is recognized but no
MTL file is opened, parsed, resolved, or validated. There are no material
properties, textures, colors, or lighting. The `HelloOBJ` example maps the
fixture's six labels to solid colors for visibility; that mapping is example
presentation, not material support in the parser or in `Nymeria.Core`.

## Diagnostics

Malformed input throws `ModelFormatException` and never returns partial geometry.
Messages include the optional source name and the one-based source line, for
example:

```text
broken.obj: line 4: face vertex '1/4/1' references texture coordinate 4, but 2 texture coordinate value(s) are defined.
```

Checked conditions include malformed directives and face tokens, missing or
invalid values, non-finite or non-invariant numbers, zero indices, out-of-range
indices, and faces with fewer than three vertices. Ordinary I/O errors propagate
from the caller's stream; the parser does not swallow them.

## Limits

The parser enforces these bounds before growing its collections:

| Limit | Value |
| --- | --- |
| Input size | 64 MiB |
| Each position, texture-coordinate, and normal stream | 2,000,000 values |
| Deduplicated mesh vertices | 2,000,000 |
| Triangles | 4,000,000 |
| Material subsets | 65,536 |
| Source line length | 1,048,576 characters |

The input limit is checked up front for seekable streams and while reading for
non-seekable streams. A line that exceeds the length limit fails before it is
parsed. The constants are exposed on `ObjModelParser`; the default parser
constructor always uses them. Files larger than these practical viewer limits
are rejected rather than partially loaded.

## Known limitations

- No MTL parsing, materials, textures, texture maps, or material properties.
- No lines, points, free-form curves, or surfaces.
- No smoothing groups, generated normals, tangents, or vertex colors.
- No concave or non-planar polygon tessellation, hole handling, or self-intersection
  detection.
- No separate meshes for objects or groups, and no scene hierarchy or transforms.
- No format auto-detection, registry, or asset cache.
- No unit conversion or automatic scale.

The parser targets ordinary polygonal OBJ files. Files that rely on the
unsupported directives fail with a diagnostic rather than being silently
mistranslated.

## Example and tests

[HelloOBJ](../examples/HelloOBJ/Program.cs) loads the checked-in
[Assets/cube.obj](../examples/HelloOBJ/Assets/cube.obj) fixture, or a path passed
on the command line, and renders it through the existing `Nymeria.Graphics` GPU
device, pipeline, depth test, and camera path. The fixture is original project
content and is redistributable with Nymeria.

`tests/Nymeria.Formats.Obj.Tests` runs headlessly and covers triangles, quads,
convex polygons, all face-reference forms, independent index streams, negative
indices, deduplication, missing attributes, comments and whitespace, invariant
culture parsing, malformed input, index errors, subsets, stream ownership, and
useful diagnostics. See [examples/README.md](../examples/README.md) for run
commands and [SMOKE_TEST.md](SMOKE_TEST.md) for the macOS rendering checklist.
