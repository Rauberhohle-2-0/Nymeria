# OMSI O3D Import

`Nymeria.Formats.O3d` implements the `Nymeria.Core.Models.IModelParser` contract
for a focused, independently authored subset of OMSI's binary `.o3d` mesh format.
It converts static triangle geometry into the format-independent
`ModelData`/`MeshData` types described in [MODEL_DATA.md](MODEL_DATA.md). The
project references only the base class library and `Nymeria.Core`; it has no
SDL3, GPU, window, or platform dependency.

## Provenance and licensing

The importer was written from verified format facts, not by porting code. The
reference material was read on 2026-10-21:

- [openOMSI repository](https://github.com/openOMSI-org/openOMSI)
- [openOMSI format notes](https://github.com/openOMSI-org/openOMSI/blob/main/docs/FORMATS.md),
  section "o3d (binary mesh) - unit `mc_o3dfiles`"
- [openOMSI O3D reader](https://github.com/openOMSI-org/openOMSI/blob/main/crates/omsi-o3d/src/lib.rs),
  inspected to confirm header widths, section tags, record layouts, and the
  pivot-matrix and scrambling notes

openOMSI is AGPL-licensed. No openOMSI source code, comments, translation, or
assets were copied into Nymeria. Only factual format observations were used, and
the C# parser, tests, and fixtures are original project code. If a future change
needs to reuse upstream implementation code or assets, stop and request a
licensing decision first.

The checked-in `HelloO3D` fixture is synthetic. It is not an OMSI game asset and
is redistributable with Nymeria under the project's BSD 3-Clause License.

## Usage

```csharp
using Nymeria.Core.Models;
using Nymeria.Formats.O3d;

using FileStream stream = File.OpenRead("model.o3d");
var parser = new O3dModelParser("model.o3d");
ModelData model = parser.Parse(stream);
```

The caller opens and owns the stream. The parser reads from the stream's current
position and does not close or dispose it, including when parsing fails. The
optional source name appears in diagnostics and does not open a file. A
non-seekable readable stream is accepted; the parser buffers it up to the input
limit before decoding.

## Supported matrix

### Versions and header

| Version | Counts | Index width | Key | Status |
| --- | --- | --- | --- | --- |
| 1 | `u16` section counts | `u16` | none | Supported, synthetic tests only |
| 2 | `u16` section counts | `u16` | none | Supported, layout rule; not observed in upstream stock corpus |
| 3 | `u32` section counts | flag bit 0 | none | Supported |
| 4 | `u32` section counts | flag bit 0 | `u32` | Supported when the key is `0xFFFFFFFF` |
| 5 | `u32` section counts | flag bit 0 | `u32` | Supported when the key is `0xFFFFFFFF` |
| 6 | `u32` section counts | flag bit 0 | `u32` | Supported, layout rule; not observed in upstream stock corpus |
| 7 | `u32` section counts | flag bit 0 | `u32` | Supported when the key is `0xFFFFFFFF` |

The header is:

| Field | Size | Condition | Meaning |
| --- | --- | --- | --- |
| Signature | 2 bytes | always | `84 19` |
| Version | 1 byte | always | `1` through `7` |
| Flags | 1 byte | version ≥ 3 | Bit 0 selects 32-bit triangle indices. Bit 1 is a scrambling hint and is inert for unscrambled files. No other bits are accepted. |
| Key | 4 bytes, little-endian | version ≥ 4 | `0xFFFFFFFF` means unscrambled. Any other value means scrambled vertices and is rejected. |

Upstream notes report versions 1, 3, 4, 5, and 7 in stock content. Versions 2 and
6 are accepted because the documented layout is gated only by version ≥ 3 for
counts and version ≥ 4 for the key, but they have not been observed in the
upstream corpus and are covered only by synthetic tests here.

All multi-byte integers and floats are little-endian. Floats are IEEE 754
single-precision.

### Sections

Sections are read in any order and at most once each. The documented stock order
is `17 49 26 79 [54]`, but this importer does not require it.

| Tag | Section | Behavior |
| --- | --- | --- |
| `0x17` | Vertices | Count followed by `count × 8` floats: position XYZ, normal XYZ, UV. Required. |
| `0x49` | Triangles | Count followed by `count × { 3 indices, material u16 }`. Required. Indexes are `u16` or `u32` according to flag bit 0. |
| `0x26` | Materials | `u16` count, then each material's 11 floats (diffuse RGBA, specular RGB, emissive RGB, power) and a `u8`-length Windows-1252 name. Only the name is preserved. Optional. |
| `0x79` | Pivot matrix | 16 finite floats, row-major, row 3 translation. Validated but not applied. Optional. |
| `0x54` | Bones | Recognized and rejected. Bones/skinning are not supported. |
| other | Unknown | Rejected with the offset and tag. |

openOMSI's loader passes unknown bytes over one at a time to tolerate protected
files. This importer deliberately fails instead, because resynchronizing could
silently misread geometry. Files that rely on that tolerance are outside the
supported subset.

## Coordinate, winding, and transform conversion

O3D geometry uses Direct3D's left-handed frame: X right, Y up, Z forward.
Nymeria uses a right-handed frame with +Y up and -Z forward. The parser converts
every vertex as follows:

| Attribute | Conversion |
| --- | --- |
| Position | `(x, y, z)` → `(x, y, -z)` |
| Normal | `(nx, ny, nz)` → `(nx, ny, -nz)` |
| Triangle winding | Each stored triangle `(i0, i1, i2)` becomes `(i0, i2, i1)` |
| Texture coordinate | Preserved unchanged |
| Units | Preserved; no rescaling |

Negating Z reflects the mesh, so reversing the winding keeps front faces
counter-clockwise in Nymeria's convention. The source winding is not stated
explicitly in the upstream format notes; this conversion follows the direction
used by openOMSI's importer for right-handed files and standard Direct3D 9
conventions. It has not been checked against a real OMSI asset here.

The upstream notes do not state the O3D texture-coordinate origin. O3D is a
Direct3D 9-era format whose texture coordinates conventionally use a top-left
origin with +V down, which already matches Nymeria. Coordinates are therefore
passed through unchanged. Textures and material rendering are out of scope, so
this behavior is not visually validated.

The `0x79` matrix is the mesh's pivot frame, used by OMSI's `origin_from_mesh`
animation. Vertices are stored in the parent object or vehicle frame; the matrix
must not be applied when rendering. Nymeria's model representation has no
transform metadata, so the matrix is validated and then discarded. Geometry is
neither transformed nor re-parented by the importer.

## Materials and subsets

O3D triangles carry a material index. Consecutive triangles with the same index
become one `MeshSubset`, preserving the triangle-aligned draw range. The
material's name is preserved as the subset's opaque `MaterialName`; the 11
material floats are read to keep the stream aligned but are not exposed. An
empty name becomes `null`, because `MeshSubset` rejects an empty label.

If the file has no materials section, triangles may reference material `0` only;
any other index is rejected. If a materials section is present, every triangle
index must be inside it. Material names that refer to texture files do not load,
resolve, or validate any texture.

## Limits and diagnostics

Malformed input throws `ModelFormatException` and never returns partial
geometry. Messages include the optional source name, the byte offset, and the
offending value where practical, for example:

```text
broken.o3d: offset 0x00000012: unexpected end of file while reading the vertex records: 96 byte(s) required, 64 remain.
```

The parser enforces these limits before allocating large buffers:

| Limit | Value |
| --- | --- |
| Input size | 64 MiB |
| Vertices | 2,000,000 |
| Triangles | 4,000,000 |
| Material subsets | 65,536 |
| Materials | `u16` count, as stored by the format |
| Material name length | `u8`, as stored by the format |

Checked conditions include a wrong signature, unsupported versions, unknown flag
bits, scrambled keys, unknown or duplicate sections, empty required sections,
counts beyond the file, counts beyond the configured limits, non-finite vertex or
matrix values, out-of-range triangle indices, material indices without a
matching material, and more material subsets than the limit.

Ordinary I/O errors propagate from the caller's stream.

## Known limitations and uncertainties

- **No real-world compatibility claim.** The automated tests and the
  `HelloO3D` fixture are synthetic. No OMSI installation or third-party model was
  available or redistributed, so ordinary stock content has not been validated.
- **Scrambled vertices are unsupported.** Any version 4+ key other than
  `0xFFFFFFFF` fails clearly instead of being heuristically decoded.
- **Bones, skinning, and animation are unsupported.** A `0x54` section fails
  clearly.
- **Materials are names only.** No texture loading, material properties,
  lighting, alpha modes, or shader effects.
- **The pivot matrix is not applied.** It is treated as animation metadata, not
  a render transform.
- **Unknown sections fail.** The importer does not reproduce the original
  loader's byte-by-byte resynchronization.
- **Versions 2 and 6 are not observed upstream.** Their layout follows the
  documented version gates but is tested only synthetically.
- **Winding and UV origin are inferred.** The upstream notes describe the axes
  and the pivot matrix but not the front-face winding or UV origin explicitly.
- **One mesh only.** The representation has no scene hierarchy, per-mesh
  transforms, multiple objects, LODs, or material slots beyond subsets.

## Example and tests

[HelloO3D](../examples/HelloO3D/Program.cs) loads its checked-in
[Assets/cube.o3d](../examples/HelloO3D/Assets/cube.o3d) fixture, or a path passed
on the command line, and renders it through the shared `HelloOBJ`/`HelloO3D`
GPU renderer, depth test, camera, and input path. The shared example runner lives
in `examples/Shared`.

`tests/Nymeria.Formats.O3d.Tests` runs headlessly without SDL3, Metal, a window,
or a GPU. It covers:

- every supported version and flag combination, including 16-bit and 32-bit
  indices;
- known position, normal, UV, winding, subset, and material values;
- Windows-1252 material names, empty names, material grouping, and section
  ordering;
- the pivot matrix being validated and ignored;
- wrong signatures, truncated headers and sections, unexpected EOF, unknown
  versions, flags, keys, tags, and bones;
- duplicate and missing sections, empty sections, oversized counts, counts
  beyond the file, bad indices, bad material indices, and non-finite data;
- stream ownership, current-position parsing, non-seekable streams, null and
  non-readable streams, and diagnostics;
- the checked-in synthetic cube fixture and an independently rebuilt version of
  the same bytes.

See [examples/README.md](../examples/README.md) for run commands and
[SMOKE_TEST.md](SMOKE_TEST.md) for the macOS rendering checklist.
