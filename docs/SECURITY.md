# Security baseline

This document records the security expectations that currently apply to
Nymeria. It is a baseline for contributors, not a claim that the project is
secure or fully audited. The first repository-wide review and its findings are
in [SECURITY_AUDIT_01.md](SECURITY_AUDIT_01.md).

## Trust model

| Input | Trust |
| --- | --- |
| Project source, embedded shaders, checked-in test fixtures | Trusted project content. |
| OBJ, O3D, text DirectX `.x`, PNG, JPEG, TGA, DDS, and BMP files, streams, and paths supplied at run time | Untrusted. |
| Package restore input and GitHub Actions metadata | Untrusted external data that the build must validate or pin. |
| SDL3, Metal, and the operating system | External native dependencies. Nymeria does not sandbox them. |

The parsers and the image decoders are headless libraries. They must not depend
on SDL3, Metal, a GPU, a window, or the current working directory. This keeps
parsing testable and keeps untrusted input away from the native boundary. The
JPEG decoder wraps the pure-managed `StbImageSharp` package; the wrap still owns
the input bound, structural validation, and dimension checks.

## Untrusted model input

The OBJ, O3D, and text DirectX `.x` parsers must:

- Validate magic values, versions, flags, keys, section tags, duplicate sections,
  declared counts, offsets, lengths, and remaining bytes before allocating or
  using data.
- Use checked arithmetic or wider types for sizes, offsets, and range checks.
- Reject malformed text, invalid indices, truncation, unsupported features,
  non-finite numbers, and counts beyond the documented limits.
- Fail with `ModelFormatException` and never return partial or silently repaired
  geometry. Ordinary I/O errors from the caller's stream may propagate.
- For text `.x`, validate the ASCII `xof` header before creating a UTF-8 reader
  so binary and compressed files are rejected as such, and bound the token
  count, token length, frame nesting, frame count, face size, and face-index
  count. Template declarations may be skipped structurally only at the top
  level; unknown objects, animation, skinning, and unsupported mesh elements
  must fail rather than be ignored.
- Read text as strict UTF-8. Invalid byte sequences are errors, not replacement
  characters.
- Reject input as soon as a limit is crossed, including while streaming a
  non-seekable source.
- Ignore external material and texture references. `mtllib`, `maplib`, and
  `usemap` are not opened or resolved, and O3D textures are not loaded. There is
  no model-relative path resolution and therefore no path traversal surface in
  the parsers.

The current defaults are shared by the parsers unless noted:

| Limit | Default |
| --- | --- |
| Input size | 64 MiB |
| Positions, texture coordinates, and normals | 2,000,000 values each |
| Deduplicated mesh vertices | 2,000,000 |
| Triangles | 4,000,000 |
| Material subsets | 65,536 |
| OBJ source line length | 1,048,576 characters |
| `.x` face vertices | 65,536 per face |
| `.x` face indices | 12,000,000 per face list |
| `.x` frame nesting | 64 levels |
| `.x` frames | 4,096 |
| `.x` token length | 1,048,576 characters |
| `.x` tokens | 32,000,000 |

These are practical viewer limits, not format requirements. A future service that
accepts untrusted models should lower them or isolate parsing in a memory-limited
process.

The examples add a separate GPU budget of 2,000,000 converted vertices and
6,000,000 indices. This budget belongs to the viewer, not to the format contract,
and is enforced before the model is uploaded.

## Untrusted image input

`Nymeria.Formats.Png.PngImageDecoder`,
`Nymeria.Formats.Jpeg.JpegImageDecoder`, `Nymeria.Formats.Tga.TgaImageDecoder`,
`Nymeria.Formats.Bmp.BmpImageDecoder`, and
`Nymeria.Formats.Dds.DdsTextureDecoder` are headless decoders for untrusted
image input. `Nymeria.Formats.Textures.TextureLoader` chooses a decoder by
signature (PNG, JPEG, DDS) or, for TGA only, by extension plus header validation;
an extension never selects a decoder whose signature is present. The PNG decoder
must:

- Verify the PNG signature, chunk structure, chunk ordering, and every chunk CRC
  before using the chunk.
- Validate `IHDR` (dimensions, bit depth, color type, compression, filter, and
  interlace methods), `PLTE` size and position, contiguous `IDAT` chunks, and an
  empty `IEND` with no trailing bytes.
- Reject unknown critical chunks, unsupported bit depths and color types,
  interlaced images, invalid palette indices, unsupported scanline filters, and
  malformed, truncated, or over-long compressed data.
- Bound the encoded input before reading it and enforce the same limit for
  seekable and non-seekable streams.
- Use checked or wider arithmetic for dimensions, row sizes, scanline sizes, and
  decoded payload sizes. Reject zero dimensions, dimensions above the limit, and
  payloads above the limit before allocating the RGBA output.
- Decompress only the exact scanline payload implied by the validated header and
  reject both short and extra output.
- Return `TextureData` or throw `PngFormatException`; never return partial or
  repaired pixels. The caller owns and disposes the stream, and the decoder must
  leave it open.

The current PNG limits are:

| Limit | Default |
| --- | --- |
| Encoded PNG input | 32 MiB |
| Width and height | 4096 pixels each |
| Decoded RGBA payload | 64 MiB |

The JPEG wrapper must verify the start-of-image marker, walk the segment chain,
reject a missing end-of-image marker or a truncated segment before decoding,
bound encoded input, read dimensions with `ImageInfo` before allocating pixels,
verify the decoded dimensions and payload length, and force opaque alpha. It must
not apply EXIF orientation or resolve any embedded path.

The TGA decoder must accept only image types 2 and 10, 24-bit and 32-bit pixels,
no color map, and no interleaving. It must validate the header before reading
pixels, honor both origin flags, validate every RLE packet against the remaining
pixel count before writing, reject truncated run and raw packets, and reject
unexpected trailing data while allowing only the documented TGA 2.0 footer.

The BMP decoder must verify the `BM` signature, the declared file size when
non-zero, the supported DIB header size, dimensions, plane count, bit depth,
compression type, channel and alpha masks, palette count and placement, row
stride, `biSizeImage` when non-zero, the pixel offset, and the exact payload
length before allocating or copying pixels. It must reject OS/2 core headers,
RLE, embedded JPEG/PNG streams, 1/4/16-bit pixels, non-standard masks, trailing
bytes, and dimensions or payloads above the documented limits. Alpha is
preserved only when the header declares a standard alpha mask; otherwise the
fourth byte of a 32-bit pixel is reserved and the image is opaque.

The DDS decoder must validate the signature, header and pixel-format sizes,
required flags, caps and caps2, depth, dimensions, mip count, DX10 resource
dimension, array size, and cubemap flag. It must derive every level's exact block
or row size with checked arithmetic, require the payload to match the declared
chain exactly, and reject unsupported formats explicitly. It must not decompress
blocks or follow metadata paths.

Each decoder applies its documented defaults through an internal limits object
that tests can lower; this keeps the boundary checks exercised without building
maximum-size inputs. `TextureData` and `CompressedTextureData` independently
validate dimensions, mip layout, block sizes, and payload lengths at
construction, so an invalid texture cannot reach the GPU path through a
different caller. GPU-side texture creation
adds a defensive 16384-pixel and 256 MiB limit, but normal CPU data is smaller.

The `HelloMaterials` example accepts one explicit PNG, JPEG, TGA, DDS, or BMP
path and does not resolve textures relative to a model or search the working
directory. A missing,
unreadable, oversized, or malformed file prints a diagnostic and uses a generated
checkerboard instead of exiting. That fallback is example presentation, not a
parser result.

## Native and GPU ownership

- Native handles are owned by the managed wrapper that created them. Do not
  destroy a handle obtained from another object.
- `Window.Handle` and `GpuDevice.Handle` throw `ObjectDisposedException` after
  disposal so a stale native pointer cannot be passed to SDL3.
- The window must outlive the GPU device, and the device must outlive renderer
  resources.
- Wait for the GPU to become idle before releasing resources that pending work
  may reference, including in failure paths.
- Scene data owns no native or GPU resources. A `SceneObject` references shared
  managed `MeshData`, `Material`, and `TextureData`; the renderer owns and
  releases one upload per distinct mesh and one GPU texture per distinct texture.
  Removing an object must not dispose data that another object still references.
- A material or texture reference is not GPU ownership. Releasing a GPU texture
  or sampler releases only that native handle, never the managed `TextureData`,
  `CompressedTextureData`, or `Material` object that other callers may share.
  Compressed levels are uploaded as blocks; the CPU payload is never decompressed
  or retained by the device.
- After `SDL_AcquireGPUSwapchainTexture` returns a texture, submit the command
  buffer. Do not cancel an acquired command buffer. A null texture or a failed
  acquisition may still be cancelled.
- Release every acquired resource exactly once and in reverse acquisition order.
- Check SDL3 return values. Do not continue with an invalid handle or silently
  ignore a failed creation or submission.
- Keep `unsafe` code and custom P/Invoke out of the repository unless the
  maintainer approves them. The SDL3 binding is the only native boundary today.

GPU and window behavior is macOS-only and is verified by the manual
[smoke test](SMOKE_TEST.md), not by headless CI.

## Dependencies and automation

- `Directory.Build.props` enables `NuGetAudit` for all direct and transitive
  packages. Because warnings are errors, a reported vulnerability fails restore
  and therefore the repository checks.
- Review dependency changes before merging them. Run:

  ```bash
  dotnet list Nymeria.slnx package --vulnerable --include-transitive
  ```

- Keep GitHub Actions pinned to full commit SHAs and use least-privilege
  `permissions`. Do not interpolate untrusted event values into shell commands.
- Do not commit credentials, tokens, private keys, environment files, or build
  output.
- The Dependabot auto-merge workflow is the only approved automatic merge. Do not
  extend it without maintainer approval.

NuGet advisories are not a complete vulnerability database. A clean audit means
that the configured feed reported no known vulnerable package at that time.

## Verification expectations

Every change that touches parsing, image decoding, native ownership, GPU
resources, dependencies, or automation must:

1. Build in Release with zero warnings.
2. Pass all headless tests.
3. Add negative or boundary tests for new rejection paths where the behavior can
   be exercised without a native window or GPU device.
4. Update the relevant format or architecture documentation when behavior or
   limits change.
5. Be pushed and checked against the actual GitHub Actions result.

Contributors should follow [SECURITY_CHECKLIST.md](SECURITY_CHECKLIST.md) during
review. Report suspected vulnerabilities privately to the repository maintainer
instead of opening a public issue with exploit details.

## Residual risk

Nymeria does not run a general fuzzing campaign, address sanitizer, SBOM or
license scan, or third-party binary analysis. Parser and decoder work is linear in
accepted input, so a local attacker can still consume the documented maximum. The
O3D parser's worst case is bounded but memory-amplifying. The image decoders bound
encoded, dimension, and decoded payload sizes, but a valid image at the limits can
still request 64 MiB of decoded pixels or encoded blocks plus GPU memory. JPEG
decoding additionally relies on `StbImageSharp`, a third-party pure-managed
library; its version is pinned and audited by NuGet, but Nymeria does not fuzz it.
SDL3, Metal, and the operating system are trusted native dependencies and are not
sandboxed. The GPU examples, including texture upload, mip upload, compressed
format capability, and sampling, are not exercised in CI.
