# Materials and Textures

This document is the authoritative description of Nymeria's format-independent
material data, validated CPU texture data, the PNG, JPEG, TGA, DDS, and BMP
decoders, the texture loader, and the `HelloMaterials` example. Related contracts
are [MODEL_DATA.md](MODEL_DATA.md), [SCENE.md](SCENE.md),
[ARCHITECTURE.md](ARCHITECTURE.md), and the
[UV conventions](MODEL_DATA.md#coordinate-and-index-conventions).

## Purpose and non-goals

A material answers one question: which base color and optional base-color texture
should a mesh instance be drawn with? The data lives in `Nymeria.Core` because it
is platform-independent and format-independent. GPU uploads, samplers, and shader
pipelines stay in `Nymeria.Graphics` and the examples; the format projects decode
bytes and never touch SDL3 or the GPU.

This milestone deliberately excludes OBJ `MTL` parsing, OMSI material
configuration, physically based rendering, shadows, transparency sorting,
texture streaming, and mipmap generation. Ambient and Lambert diffuse lighting is
described in [LIGHTING.md](LIGHTING.md); it reuses this material data but is an
optional scene-level feature, not a property of `Material`. Alpha is decoded and
preserved but the example shader always writes an opaque fragment. No decoder
decompresses block-compressed data.

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
- `BaseColorTexture` is an optional `Texture` reference. It may be decoded RGBA
  data or compressed blocks. It is referenced, not copied, and the material does
  not own it.
- `HasTexture` reports whether a texture is referenced.
- `Name` is an optional diagnostic label, normalized to `null` when blank.
- `Material.Default` is a shared, untextured white material. Scene objects use it
  when no material is supplied.

There is no equality or content-based caching: two `Material` instances that
describe the same surface are still distinct references. A renderer may cache GPU
resources by `Texture` reference, so sharing one texture instance is the intended
way to avoid duplicate uploads.

`SceneObject` accepts an optional material in its constructor and exposes a
settable `Material` property. Both reference the material without copying it.
Assigning `null` throws `ArgumentNullException`; use `Material.Default` to clear a
material explicitly. Several scene objects may share one material.

## Texture data model

`Nymeria.Core.Materials.Texture` is the shared abstract base for CPU texture
data:

| Property | Meaning |
| --- | --- |
| `Width`, `Height` | Base-level dimensions in pixels |
| `MipLevelCount` | Stored levels, including the base level |
| `Name` | Optional source name used in diagnostics |
| `GetMipWidth(n)`, `GetMipHeight(n)` | Dimensions of level `n`, clamped to one pixel |
| `GetMaxMipLevelCount(w, h)` | Largest valid chain for a size |

Every constructor validates the dimensions, the mip count, and the payload
before the object exists. Limits are shared by every format:

| Limit | Value |
| --- | --- |
| Width and height | 1 through `Texture.MaxDimension` (4096) each |
| Stored mip levels | 1 through the full halving chain |
| Total payload across levels | `Texture.MaxPixelBytes` (64 MiB) |
| RGBA bytes per pixel | 4 |

### `TextureData`

`TextureData` holds tightly packed, 8-bit RGBA pixels with one or more mip
levels:

```csharp
var texture = new TextureData(width, height, rgbaPixels, "generated checkerboard");
var mipped = new TextureData(width, height, mipLevels, "dds rgba chain");
```

- Level 0 is stored row by row from the top row down, four bytes per pixel in
  red, green, blue, alpha order. This matches the model contract: `(0, 0)` is the
  top-left texel and `+V` points down.
- `RowPitch` is `Width * 4` for the base level. There is no padding, stride, or
  color-space conversion.
- `Pixels` returns level 0; `GetMipPixels(n)` returns another level.
- `ByteCount` is the total across all stored levels.
- The constructor takes ownership of the supplied memory instead of copying it.
  Callers must not mutate an array after construction.

A mismatched payload length throws `ArgumentException`; an unsupported dimension
or oversized payload throws `ArgumentOutOfRangeException`.

### `CompressedTextureData`

`CompressedTextureData` holds block-compressed payloads exactly as stored in the
source file:

```csharp
var texture = new CompressedTextureData(
    width,
    height,
    CompressedTextureFormat.Bc7,
    isSrgb: true,
    mipLevels,
    "facade.dds");
```

| Format | Block size | Channels |
| --- | --- | --- |
| `Bc1` | 8 bytes | RGB, optional one-bit alpha |
| `Bc2` | 16 bytes | RGB plus four-bit alpha |
| `Bc3` | 16 bytes | RGB plus interpolated alpha |
| `Bc4` | 8 bytes | One linear channel |
| `Bc5` | 16 bytes | Two linear channels |
| `Bc7` | 16 bytes | RGBA |

Blocks are stored four-by-four, row-major, top row first, with no padding. Each
mip level must contain exactly the block payload implied by its dimensions
(`ceil(width / 4) * ceil(height / 4) * blockBytes`). `IsSrgb` records that the
source declared an sRGB-encoded variant; it does not change the stored bytes.

The type never decompresses blocks, generates mipmaps, or uploads anything. A
renderer maps the format to the backend and checks capability.

## Texture loading

`Nymeria.Formats.Textures.TextureLoader` identifies a format and delegates
decoding:

```csharp
using FileStream stream = File.OpenRead(path);
Texture texture = new TextureLoader().Load(stream, path);
```

| Detection | Rule |
| --- | --- |
| PNG | Signature `89 50 4E 47 0D 0A 1A 0A` |
| JPEG | Signature `FF D8 FF` |
| DDS | Signature `DDS ` |
| BMP | Signature `42 4D` (`BM`) |
| TGA | Source name ends in `.tga` or `.targa`, then header validation |

Signatures take precedence over file extensions, so a JPEG named `.png` decodes
as JPEG. TGA has no signature, so its extension only selects the decoder; the TGA
header and every packet are still validated. Content that matches no format
throws `TextureFormatException`; a malformed file of a recognized format throws
that decoder's exception (`PngFormatException`, `JpegFormatException`,
`TgaFormatException`, `BmpFormatException`, or `DdsFormatException`). The loader
never opens a file, resolves a path, keeps a cache, or makes a GPU call.

The default limits are constants on each decoder. An internal limits object
allows the tests to lower the encoded-input, dimension, and payload bounds and
exercise the rejection paths without allocating maximum-size images; the public
constructors always use the documented defaults.

Every decoder:

- reads the stream as untrusted input and bounds the encoded byte count;
- uses checked arithmetic for dimensions, pitches, and payload sizes;
- validates its header before allocating the decoded payload;
- leaves the caller's stream open and never disposes it;
- returns fully validated data or throws, never partial pixels.

| Decoder | Encoded input | Dimensions | Payload | Formats |
| --- | --- | --- | --- | --- |
| PNG | 32 MiB | 4096 | 64 MiB decoded | 8-bit non-interlaced |
| JPEG | 32 MiB | 4096 | 64 MiB decoded | common JPEG |
| TGA | 32 MiB | 4096 | 64 MiB decoded | true-color 24/32-bit |
| BMP | 32 MiB | 4096 | 64 MiB decoded | uncompressed 8/24/32-bit |
| DDS | 32 MiB | 4096 | 64 MiB encoded | BC1–BC5, BC7, RGBA8 |

### PNG

`Nymeria.Formats.Png.PngImageDecoder` is a dependency-free implementation for
non-interlaced, 8-bit images with grayscale, RGB, indexed, grayscale-alpha, or
RGBA color. It verifies the signature, chunk ordering, every chunk CRC, palette
indices, scanline filters, and the exact decompressed size; it rejects unknown
critical chunks, 16-bit, interlaced, and malformed images. Other ancillary chunks
such as `gAMA` and `sRGB` are ignored. Alpha is preserved but not blended and no
color-space conversion is applied.

`tRNS` transparency is decoded for the color types that permit it:

- **Indexed (3):** the alpha values apply to the leading palette entries in
  order. Entries beyond the supplied alpha list stay fully opaque. The chunk must
  follow `PLTE` and provide one to the palette entry count values.
- **Grayscale (0):** one 16-bit sample names the fully transparent gray value;
  matching pixels get alpha 0 and all others stay opaque.
- **Truecolor (2):** three 16-bit samples name the fully transparent RGB value;
  matching pixels get alpha 0 and all others stay opaque.

Samples are compared against the decoded 8-bit pixel values, before any
conversion, and must be in the 8-bit range. `tRNS` is rejected for grayscale-alpha
(4) and RGBA (6), which already carry alpha, and for duplicate, misplaced,
mis-sized, out-of-range, or truncated transparency metadata.

### JPEG

`Nymeria.Formats.Jpeg.JpegImageDecoder` uses
[StbImageSharp](https://www.nuget.org/packages/StbImageSharp) 2.30.16, a
pure-managed C# port of `stb_image` with no native code and no transitive
packages. Its license is **Unlicense OR MIT**, which is compatible with this
repository's BSD 3-Clause license. It decodes common baseline and progressive
JPEG.

Nymeria adds the safety boundary the library does not provide:

- the encoded input is bounded and the JPEG start-of-image marker is verified;
- the segment chain is walked before decoding, so a missing end-of-image marker
  or a truncated segment is a `JpegFormatException` instead of synthesized
  pixels (stb_image alone tolerates truncation);
- `ImageInfo` reads the dimensions first, so an oversized image is rejected
  before the decoded RGBA buffer is allocated;
- decoded dimensions must match the header, and the payload must be exactly
  `width * height * 4` bytes;
- alpha is forced to `255`, because JPEG has no alpha channel.

EXIF orientation metadata is **not** applied. Decoded rows are top-down, matching
the model UV convention; a JPEG that relies on an EXIF rotation appears in its
stored orientation.

### TGA

`Nymeria.Formats.Tga.TgaImageDecoder` supports the documented true-color subset:

- image types 2 (uncompressed) and 10 (RLE-compressed);
- 24-bit BGR and 32-bit BGRA pixels;
- both origin flags, so the result is always top-down and left-to-right;
- alpha for 32-bit images with eight declared alpha bits; 32-bit images that
  declare zero alpha bits are treated as opaque.

Rejected explicitly: color maps, color-mapped or grayscale image types,
15/16-bit pixels, interleaving, 24-bit pixels that declare alpha, and any other
alpha-bit count. RLE packets are validated before pixels are written: a packet
may not exceed the remaining pixel count, and a truncated run or raw packet
fails. As the TGA format specifies, bit 7 of the packet header selects a run
packet (one pixel repeated) when set and a raw packet (one entry per pixel) when
clear. An optional TGA 2.0 footer (`TRUEVISION-XFILE.`) is accepted; any other
trailing data is rejected.

### BMP

`Nymeria.Formats.Bmp.BmpImageDecoder` decodes a documented Windows BMP/DIB
subset and returns top-down RGBA `TextureData`:

- the `BM` file signature and a 40-byte `BITMAPINFOHEADER`, 108-byte
  `BITMAPV4HEADER`, or 124-byte `BITMAPV5HEADER`;
- uncompressed (`BI_RGB`) 24-bit BGR and 32-bit BGRA/BGRX pixels;
- uncompressed 8-bit indexed pixels with a Windows palette (`biClrUsed` entries,
  or 256 when the count is zero);
- 32-bit `BI_BITFIELDS` pixels with the standard BGRA masks and an optional
  alpha mask;
- bottom-up rows (positive height) and top-down rows (negative height);
- the four-byte row padding required by the format.

Alpha is preserved only when the header declares a standard alpha mask
(`0xFF000000`), either in a V4/V5 header or as the fourth `BI_BITFIELDS` mask.
A 32-bit image with no alpha mask is treated as BGRX and every pixel is opaque,
which matches the Windows convention for the reserved fourth byte. 24-bit and
8-bit images are always opaque.

The decoder validates the signature, the declared file size when non-zero, the
DIB header size and position, dimensions, plane count, bit depth, compression
type, channel masks, palette size and placement, row stride, `biSizeImage` when
non-zero, the pixel array offset, the exact pixel payload length, and the
decoded RGBA size before allocating or copying pixels. The pixel array must end
exactly at the end of the input; trailing profile or metadata bytes are rejected
rather than skipped.

Rejected explicitly: 1-bit, 4-bit, and 16-bit pixels; `BI_RLE8` and `BI_RLE4`
compression; embedded `BI_JPEG` and `BI_PNG` streams; `BI_ALPHABITFIELDS`; OS/2
`BITMAPCOREHEADER` (12-byte) and other DIB header sizes; non-standard channel or
alpha masks; zero or negative dimensions; and dimensions, strides, or payloads
above the documented limits.

### DDS

`Nymeria.Formats.Dds.DdsTextureDecoder` parses the 128-byte header, the optional
20-byte DX10 extension header, and the payload. It validates the signature,
header and pixel-format sizes, required flags, caps, caps2, depth, dimensions,
mip count, and DX10 resource dimension, array size, and cubemap flag before using
the payload.

Supported block-compressed formats:

| Source | FourCC or DXGI format | Result |
| --- | --- | --- |
| Legacy | `DXT1` | `Bc1` |
| Legacy | `DXT3` | `Bc2` |
| Legacy | `DXT5` | `Bc3` |
| Legacy | `ATI2`, `BC5U` | `Bc5` |
| Legacy | `BC4U` | `Bc4` |
| DX10 | 71 / 72 | `Bc1`, sRGB for 72 |
| DX10 | 74 / 75 | `Bc2`, sRGB for 75 |
| DX10 | 77 / 78 | `Bc3`, sRGB for 78 |
| DX10 | 80 | `Bc4` |
| DX10 | 83 | `Bc5` |
| DX10 | 98 / 99 | `Bc7`, sRGB for 99 |

Supported uncompressed formats are 24-bit and 32-bit RGB/RGBA with the common
channel masks (BGR/BGRA byte order or RGB/RGBA byte order), plus DX10 format 28
(`R8G8B8A8_UNORM`) and 87 (`B8G8R8A8_UNORM`). Uncompressed payloads are converted
to top-down RGBA; a payload without the alpha-pixels flag is opaque. Padded row
pitches are not supported: the declared pitch must equal the tightly packed row
size, and the payload must contain exactly the declared mip chain with no
trailing bytes.

Rejected explicitly: cubemaps, volumes, 3D textures, texture arrays, BC6H,
BC4/BC5 signed-normalized variants, 16-bit and other uncompressed layouts,
unknown fourCCs and DXGI formats, an sRGB uncompressed DXGI format, a mip count
that exceeds the halving chain, a declared chain without the `MIPMAPCOUNT` flag,
and a payload that does not match the declared sizes exactly.

An unsupported format error names the source format and the accepted set, for
example:

```text
The DDS texture 'facade.dds' is invalid or unsupported: the DXGI format 95 is not
supported. Supported formats are BC1, BC2, BC3, BC4, BC5, and BC7 (including sRGB
where available), plus 32-bit R8G8B8A8 and B8G8R8A8. Check that the texture was
exported as BC or RGBA8 rather than BC6H, BC7 SNORM, or an array/cubemap.
```

A developer diagnosing a rejected DDS should check the DX10 `dxgiFormat` value,
the legacy fourCC, the mip count against the dimensions, and whether the payload
length equals the sum of the block or row sizes for every level.

## GPU upload

`GpuDevice.CreateTexture` in `Nymeria.Graphics` has two forms:

- the RGBA8 single-level overload used by generated fallback textures;
- a mip-chain overload that takes an explicit `GPUTextureFormat`, dimensions, one
  `TextureUploadRegion` per level, and one payload. It supports `R8G8B8A8Unorm`,
  its sRGB variant, and the BC1–BC7 formats and sRGB variants.

The mip-chain overload validates every region's dimensions against the halving
chain, requires an exact encoded length per region, and checks
`SDL_GPUTextureSupportsFormat` for the format with sampler usage. An unsupported
format throws `NotSupportedException` with a message that tells the caller to
check capability first; `GpuDevice.SupportsTextureFormat` exposes the check
without creating a resource.

Levels are copied into one transfer buffer with each level's offset padded to
512 bytes, then uploaded in one copy pass. Rows stay tightly packed and SDL
derives the pitch from each region's dimensions. Every texture is uploaded once;
no upload happens per frame.

Block-compressed regions must be block aligned: width and height must be
multiples of four. SDL's D3D12 backend requires that alignment while its Vulkan
backend expects logical dimensions, and the two disagree below 4 × 4 (SDL issue
#15348). Nymeria therefore uploads only the block-aligned prefix of a compressed
mip chain. Levels smaller than 4 × 4 remain in CPU data and are never sampled.
The example creates a sampler with `MaxLod = 0` for single-level textures and a
second sampler with linear mip filtering for uploaded chains, so a texture never
requests a level the GPU does not hold.

The shared material renderer in `examples/Shared` caches one GPU texture per
distinct `Texture` reference and binds a generated 1 × 1 white texture for
materials without a texture or meshes without UVs. The fragment shader multiplies
sampled RGB by the material tint: `texture.rgb * material.BaseColor`. When the
scene has lighting, the result is then multiplied by the ambient plus Lambert
diffuse term; an unlit scene uses a white ambient term so the output is
unchanged. See [LIGHTING.md](LIGHTING.md). Output alpha is `1.0`; alpha is decoded
but not blended.

## Color space

Nymeria does not apply color-space conversion in any CPU decoder:

- PNG, JPEG, TGA, BMP, and uncompressed DDS pixels keep their stored encoded
  values. They are uploaded as `R8G8B8A8Unorm` and sampled without conversion,
  preserving the behavior of earlier milestones.
- Compressed DDS textures whose source declares an sRGB variant are uploaded as
  the matching SRGB GPU format (`BC1RGBAUnormSRGB`, `BC2RGBAUnormSRGB`,
  `BC3RGBAUnormSRGB`, or `BC7RGBAUnormSRGB`), so the backend decodes sampled
  values to linear space.
- BC4 and BC5 are linear data formats, intended for single- and two-channel
  data. They have no sRGB variants; the base-color shader shows their channels
  directly.
- Uncompressed sRGB DXGI formats are rejected rather than silently treated as
  linear, because `TextureData` carries no color-space flag. Export such a
  texture as RGBA8 UNORM, or use a compressed sRGB format.

There is no automatic sRGB conversion for PNG, JPEG, or TGA. A future normal-map
or data-texture path can use the unorm upload without change.

## `HelloMaterials`

`examples/HelloMaterials` demonstrates the material and texture path without
requiring an external asset:

```bash
dotnet run --project examples/HelloMaterials/HelloMaterials.csproj
dotnet run --project examples/HelloMaterials/HelloMaterials.csproj -- examples/HelloMaterials/Assets/checker.jpg
dotnet run --project examples/HelloMaterials/HelloMaterials.csproj -- examples/HelloMaterials/Assets/checker.tga
dotnet run --project examples/HelloMaterials/HelloMaterials.csproj -- examples/HelloMaterials/Assets/checker-rle.tga
dotnet run --project examples/HelloMaterials/HelloMaterials.csproj -- examples/HelloMaterials/Assets/checker.bmp
dotnet run --project examples/HelloMaterials/HelloMaterials.csproj -- examples/HelloMaterials/Assets/checker-32.bmp
dotnet run --project examples/HelloMaterials/HelloMaterials.csproj -- examples/HelloMaterials/Assets/checker-bc1.dds
dotnet run --project examples/HelloMaterials/HelloMaterials.csproj -- examples/HelloMaterials/Assets/checker-rgba.dds
```

- With no argument, it generates a 128 × 128 checkerboard with four differently
  colored quadrants and an alternating brightness pattern. The quadrants make a
  flipped U or V axis visible.
- Two objects share one checkerboard `Material` and one cube `MeshData`, so the
  renderer uploads both once.
- A third object uses the same `Texture` through a blue-tinted material, showing
  how the tint multiplies the sampled texture.
- A fourth object uses an untextured solid-orange material and the white
  fallback texture.
- A supplied path is decoded by `TextureLoader`. A missing, unreadable,
  malformed, oversized, or unsupported texture prints a diagnostic and falls back
  to the generated checkerboard; the window still opens.
- If the GPU backend cannot sample a compressed DDS format, the example reports
  the texture description and falls back to the checkerboard instead of failing
  during a frame.
- Camera input is the shared `FlyCameraControls`. The loop, frame budget, depth
  target, resize, minimize, zero-sized-drawable, and shutdown paths follow the
  other interactive examples.

The checked-in fixtures under `examples/HelloMaterials/Assets` are original
project content: a baseline JPEG, an uncompressed 32-bit bottom-origin TGA, an
RLE-compressed 24-bit TGA, a 24-bit bottom-up BMP, a 32-bit top-down BMP, a BC1
DDS with a mip chain, and an uncompressed RGBA DDS. Their provenance and layout
are documented in that directory's `README.md`.

The example is presentation code, not a material or asset pipeline. It does not
parse MTL, associate O3D material names with textures, blend transparency, stream
textures, reload them, or generate mipmaps.

## Tests

`tests/Nymeria.Core.Tests` covers the CPU data without a GPU or window:

- `Material` defaults, tint validation, texture-reference sharing, and blank-name
  normalization;
- `TextureData` payload validation, dimensions, row pitch, byte count, mip
  chains, and limits;
- `CompressedTextureData` block sizes, format validation, mip layout, and limits;
- `SceneObject` default material, shared material references, replacement, and
  null rejection.

`tests/Nymeria.Formats.Png.Tests` covers the PNG subset, malformed input, and
decompression limits. `tests/Nymeria.Formats.Jpeg.Tests` covers decoded pixels
and alpha, quadrant orientation, truncated segments, a missing end-of-image
marker, oversized dimensions, non-seekable input, and stream ownership, using a
checked-in JPEG fixture. `tests/Nymeria.Formats.Tga.Tests` builds fixtures in
memory and covers channel order, origins, alpha, RLE run and raw packets,
truncation, packet overrun, headers, unsupported variants, and limits.
`tests/Nymeria.Formats.Dds.Tests` builds fixtures in memory and also decodes
checked-in 8 × 8 BC1, BC2, BC3, BC4, BC5, BC7, and BC1-sRGB files, covering
legacy fourCCs, DX10 formats and sRGB flags, mip chains, uncompressed channel
masks, pitch and linear-size validation, truncation, trailing bytes,
array/cubemap/3D rejection, unsupported formats, and an unrepresentable mip
count. `tests/Nymeria.Formats.Bmp.Tests` builds fixtures in memory and covers
24-bit and 32-bit channel order, top-down and bottom-up rows, row padding, V4
and `BI_BITFIELDS` alpha masks, indexed palettes, malformed and truncated
headers, compression and bit-depth rejection, invalid offsets and sizes, exact
payload lengths, truncation at every prefix, resource limits, stream ownership,
and the two checked-in BMP fixtures. `tests/Nymeria.Formats.Textures.Tests`
covers signature-first dispatch,
extension-based TGA detection, format-specific errors, configured input limits,
and unknown or empty input.

GPU uploads, sampling, fallback binding, visual orientation, resize, and cleanup
are validated manually with [SMOKE_TEST.md](SMOKE_TEST.md), not by headless CI.

## Intentionally unsupported

OBJ MTL parsing, OMSI material mapping, PBR, shadows, transparency sorting,
texture streaming, mipmap generation, hot reload, animation, an editor, and a
general asset pipeline are out of scope. Lighting is limited to the single
ambient plus directional Lambert model in [LIGHTING.md](LIGHTING.md). Alpha is
decoded but not blended. BC decompression on the CPU, BC6H, ASTC, ETC, cubemaps,
texture arrays, 3D
textures, and volume textures are not supported. The supported DDS subset is not
a claim of universal DDS compatibility. BMP support is also a documented subset:
RLE, 1/4/16-bit, OS/2 headers, embedded JPEG/PNG data, and non-standard channel
masks are rejected rather than approximated.
