# Materials and Base-Color Textures

This document is the authoritative description of Nymeria's format-independent
material data, validated CPU texture data, the bounded PNG decoder, and the
`HelloMaterials` example. Related contracts are [MODEL_DATA.md](MODEL_DATA.md),
[SCENE.md](SCENE.md), [ARCHITECTURE.md](ARCHITECTURE.md), and the
[UV conventions](MODEL_DATA.md#coordinate-and-index-conventions).

## Purpose and non-goals

A material answers one question: which base color and optional base-color texture
should a mesh instance be drawn with? The data lives in `Nymeria.Core` because it
is platform-independent and format-independent. GPU uploads, samplers, and shader
pipelines stay in `Nymeria.Graphics` and the examples.

This milestone deliberately excludes OBJ `MTL` parsing, OMSI material
configuration, physically based rendering, lighting, shadows, transparency
sorting, texture streaming, and mipmap generation. Alpha is decoded and preserved
but the example shader always writes an opaque fragment.

## `Material`

`Nymeria.Core.Materials.Material` is an immutable reference type:

```csharp
var material = new Material(
    baseColor: new Vector3(1.0f, 0.75f, 0.5f),
    baseColorTexture: texture,
    name: "brick");
```

- `BaseColor` is the display tint. Every component must be finite and between
  `0` and `1`; invalid values throw `ArgumentOutOfRangeException`.
- `BaseColorTexture` is an optional `TextureData` reference. It is referenced, not
  copied, and the material does not own it.
- `HasTexture` reports whether a texture is referenced.
- `Name` is an optional diagnostic label, normalized to `null` when blank.
- `Material.Default` is a shared, untextured white material. Scene objects use it
  when no material is supplied.

There is no equality or content-based caching: two `Material` instances that
describe the same surface are still distinct references. A renderer may cache GPU
resources by `TextureData` reference, so sharing one `TextureData` instance is the
intended way to avoid duplicate uploads.

`SceneObject` accepts an optional material in its constructor and exposes a
settable `Material` property. Both reference the material without copying it.
Assigning `null` throws `ArgumentNullException`; use `Material.Default` to clear a
material explicitly. Several scene objects may share one material.

## `TextureData`

`Nymeria.Core.Materials.TextureData` holds validated, tightly packed, 8-bit RGBA
pixels:

```csharp
var texture = new TextureData(width, height, rgbaPixels, "generated checkerboard");
```

- Pixels are stored row by row from the top row down, four bytes per pixel in
  red, green, blue, alpha order. This matches the model contract: `(0, 0)` is the
  top-left texel and `+V` points down.
- `RowPitch` is `Width * 4`; there is no padding, stride, or color-space
  conversion.
- The constructor takes ownership of the supplied memory instead of copying it.
  Callers must not mutate the array after construction.
- The type contains no GPU handle, SDL3, window, or file-format dependency.

Validation happens at construction:

| Limit | Value |
| --- | --- |
| Width and height | 1 through `TextureData.MaxDimension` (4096) each |
| Decoded pixel payload | `TextureData.MaxPixelBytes` (64 MiB) |
| Pixel layout | exactly `width * height * 4` bytes |

A 4096 × 4096 RGBA image is exactly 64 MiB, so the dimension and byte limits
bound memory together. Arithmetic uses checked or wider types. A mismatched
payload length throws `ArgumentException`; an unsupported dimension or an
oversized payload throws `ArgumentOutOfRangeException`.

## PNG decoding

`Nymeria.Formats.Png.PngImageDecoder` is a dependency-free decoder that produces
`TextureData`. It accepts one PNG image from a readable stream:

```csharp
using FileStream stream = File.OpenRead(path);
var decoder = new PngImageDecoder(path);
TextureData texture = decoder.Decode(stream);
```

The caller owns and disposes the stream; the decoder leaves it open. The decoder
does not open files, resolve relative paths, inspect the working directory, or
depend on SDL3, Metal, a GPU, or a window. It does not close the input on failure.

### Supported subset

- PNG signature and the required chunk ordering: `IHDR` first, at least one
  contiguous `IDAT`, and `IEND` last with no trailing bytes.
- Non-interlaced images only.
- Bit depth 8 only. Grayscale (0), RGB (2), indexed (3), grayscale-alpha (4), and
  RGBA (6) are converted to RGBA.
- Indexed images require a usable `PLTE` palette before `IDAT`; palette indices
  outside the palette are errors.
- All five PNG scanline filter types (None, Sub, Up, Average, Paeth).
- zlib decompression through the base class library's `ZLibStream`.

### Validation and limits

The decoder treats the stream as untrusted input:

| Limit or check | Behavior |
| --- | --- |
| Encoded input | Rejected above 32 MiB, for seekable and non-seekable streams |
| Width or height | Rejected above 4096 pixels, zero dimensions rejected |
| Decoded RGBA payload | Rejected above 64 MiB |
| Chunk structure | Declared length checked against remaining bytes before use |
| Chunk type | Letters only; the first letter's case identifies critical chunks |
| Checksums | Every chunk CRC is verified |
| Unknown chunks | Critical chunks are rejected; ancillary chunks are ignored |
| IHDR | Exactly 13 bytes, one header, valid compression/filter/interlace methods |
| `PLTE` | 1 to 256 RGB entries, before `IDAT`, at most once |
| `IDAT` | Must follow `IHDR`, must be contiguous, at least one chunk required |
| `IEND` | Empty, after image data, at the end of the input |
| Compressed data | Must expand to exactly the validated scanline payload |
| Scanline filter | Values outside 0 through 4 are rejected |

Ancillary chunks such as `tRNS`, `gAMA`, and `sRGB` are ignored. The decoded
payload preserves alpha when the source color type carries it, but the example
does not blend and performs no color-space conversion. The decoder reports
failures as `PngFormatException` with the optional source name and, where useful,
a byte or pixel diagnostic. Ordinary I/O errors from the caller's stream may
propagate.

The decoder is linear in accepted input. It does not attempt to repair malformed
files or return partially decoded pixels.

## Renderer integration

The GPU path remains in `Nymeria.Graphics` and the example:

- `GpuDevice.CreateTexture` uploads tightly packed RGBA pixels through a transfer
  buffer and a copy pass. It creates an `R8G8B8A8Unorm` 2D texture with one mip
  level and the `Sampler` usage flag. It does not generate mips, convert color
  space, or keep a CPU copy.
- `HelloMaterials` creates one sampler with linear minification and
  magnification, nearest mipmap mode, clamp-to-edge addressing on all axes, and a
  maximum LOD of zero.
- The fragment shader samples the base-color texture and multiplies its RGB value
  by the material tint: `texture.rgb * material.BaseColor`. The output alpha is
  `1.0`.
- A material without a texture binds a generated 1 × 1 opaque white texture, so
  its base color is shown unchanged.
- A mesh without texture coordinates also binds the white fallback texture. This
  avoids sampling an arbitrary texel from coordinate `(0, 0)`.

The `HelloMaterials` renderer caches one GPU upload per distinct `MeshData` and
one GPU texture per distinct `TextureData`, both keyed by reference. Uploads occur
before the frame command buffer is acquired; no mesh or texture is uploaded per
frame. The cache holds at most one upload per distinct reference and is cleared
when the renderer is disposed.

Textures use the model UV convention directly: the first pixel row is at `v = 0`
and `+V` points down. `HelloMaterials` maps the bottom-left cube corner to
`(0, 1)` and the top-left corner to `(0, 0)` as seen from outside, so a flipped U
or V axis is visible on a face.

The renderer releases textures and buffers only after `SDL_WaitForGPUIdle`, and
the device is disposed last. A failed upload releases the texture created so far.

## `HelloMaterials`

`examples/HelloMaterials` demonstrates the material path without requiring an
external asset:

```bash
dotnet run --project examples/HelloMaterials/HelloMaterials.csproj
dotnet run --project examples/HelloMaterials/HelloMaterials.csproj -- path/to/image.png
```

- With no argument, it generates a 128 × 128 checkerboard with four differently
  colored quadrants and an alternating brightness pattern. The quadrants make a
  flipped U or V axis visible.
- Two objects share one checkerboard `Material` and one cube `MeshData`, so the
  renderer uploads both once.
- A third object uses the same `TextureData` through a blue-tinted material,
  showing how the tint multiplies the sampled texture.
- A fourth object uses an untextured solid-orange material and the white fallback
  texture.
- A supplied PNG is decoded through `PngImageDecoder`. A missing, unreadable,
  oversized, or malformed texture prints a diagnostic and falls back to the
  generated checkerboard; the window still opens and the example keeps running.
- Camera input is the shared `FlyCameraControls`. The loop, frame budget, depth
  target, resize, minimize, zero-sized-drawable, and shutdown paths follow the
  other interactive examples.

The example is presentation code, not a material or asset pipeline. It does not
parse MTL, associate O3D material names with textures, blend transparency, stream
textures, reload them, or generate mipmaps.

## Tests

`tests/Nymeria.Core.Tests` covers the CPU data without a GPU or window:

- `Material` defaults, tint validation, texture-reference sharing, and blank-name
  normalization;
- `TextureData` payload validation, dimensions, row pitch, byte count, and limits;
- `SceneObject` default material, shared material references, replacement, and
  null rejection.

`tests/Nymeria.Formats.Png.Tests` covers the decoder with generated, in-memory
PNGs:

- all supported color types, filters, palette conversion, and top-down output;
- seekable and non-seekable inputs, stream ownership, and source diagnostics;
- invalid signatures, CRC failures, truncated or oversized chunks, duplicate or
  misplaced `IHDR`/`PLTE`/`IDAT`, trailing data, and non-letter chunk types;
- unsupported bit depths, color types, interlacing, filters, and palette indices;
- exact dimension, encoded-input, and decoded-payload boundaries;
- malformed or truncated compressed data and decompression overflow.

GPU uploads, sampling, fallback binding, visual orientation, resize, and cleanup
are validated manually with [SMOKE_TEST.md](SMOKE_TEST.md), not by headless CI.

## Intentionally unsupported

OBJ MTL parsing, OMSI material mapping, PBR, lighting, shadows, transparency
sorting, texture streaming, mipmap generation, hot reload, animation, an editor,
and a general asset pipeline are out of scope. Alpha is decoded but not blended.
