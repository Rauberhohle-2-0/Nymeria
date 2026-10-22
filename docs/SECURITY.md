# Security baseline

This document records the security expectations that currently apply to
Nymeria. It is a baseline for contributors, not a claim that the project is
secure or fully audited. The repository-wide reviews and their findings are in
[SECURITY_AUDIT_01.md](SECURITY_AUDIT_01.md) and
[SECURITY_AUDIT_02.md](SECURITY_AUDIT_02.md).

## Trust model

| Input | Trust |
| --- | --- |
| Project source, embedded shaders, checked-in test fixtures | Trusted project content. |
| OBJ, O3D, text DirectX `.x`, PNG, JPEG, TGA, DDS, BMP, and WAV files, streams, and paths supplied at run time | Untrusted. |
| Package restore input and GitHub Actions metadata | Untrusted external data that the build must validate or pin. |
| SDL3, CoreAudio or the platform audio device, Metal, and the operating system | External native dependencies. Nymeria does not sandbox them. |

The model parsers, image decoders, and WAV decoder are headless libraries. They
must not depend on SDL3, Metal, a GPU, a window, or the current working
directory. This keeps parsing testable and keeps untrusted input away from the
native boundary. The JPEG decoder wraps the pure-managed `StbImageSharp`
package; the wrap still owns the input bound, structural validation, and
dimension checks.

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
pixels, honor both origin flags, interpret RLE packet header bit 7 per the format
(set means a repeated run, clear means a raw packet), validate every RLE packet
against the remaining pixel count before writing, reject truncated run and raw
packets, and reject unexpected trailing data while allowing only the documented
TGA 2.0 footer. The checked-in RLE fixture and its tests must use that standard
encoding so the tests cannot validate a private, inverted convention.

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

## Untrusted audio input

`Nymeria.Formats.Wav.WavAudioDecoder` is a headless decoder for untrusted WAV
input. It must:

- Verify the `RIFF` signature and `WAVE` form type, and require the declared RIFF
  size to match the available bytes exactly. Truncation and trailing bytes fail.
- Walk chunks with wider arithmetic, enforce the RIFF boundary for every chunk,
  honor RIFF padding, and reject an incomplete chunk header or missing odd-size
  padding.
- Require exactly one `fmt ` chunk before exactly one `data` chunk, and reject
  duplicate or missing chunks.
- Validate the format tag, channel count, sample rate, bit depth, block
  alignment, and byte rate before interpreting samples. Only uncompressed PCM
  format 1 at 8/16/24/32 bits and IEEE float format 3 at 32 bits, with one or two
  channels and a sample rate from 8000 through 192000 Hz, are accepted.
  `WAVE_FORMAT_EXTENSIBLE`, compressed formats, and wider channel layouts are
  rejected explicitly, not approximated.
- Require the data payload length to be a whole number of sample frames and
  enforce the frame and decoded-payload limits before allocating the float
  output.
- Reject non-finite float samples.
- Return `PcmAudioData` or throw `WavFormatException`; never return partial or
  repaired audio. The caller owns the stream and the decoder leaves it open.
- Bound the encoded input for seekable and non-seekable streams alike, and stop
  reading as soon as the input limit is crossed.

The current WAV limits are:

| Limit | Default |
| --- | --- |
| Encoded input | 32 MiB |
| Decoded frame count | 4,000,000 |
| Decoded float payload | 32 MiB (8,388,608 samples) |
| Resampled float payload | 32 MiB (8,388,608 samples) |
| Channels | 2 |
| Sample rate | 8000–192000 Hz |

These are practical viewer limits. A service that accepts untrusted audio should
lower them or isolate decoding in a memory-limited process. Linear resampling to
the mixer rate can multiply the sample count by up to 24, so the playback layer
rejects a resampled payload above 32 MiB instead of allocating it. Nymeria plays
audio only; it does not open a recording device, request microphone access, send
telemetry, or contact a network service for playback.

## Mesh bounds and visibility culling

`MeshData.Bounds` is derived from the same positions that `MeshData` already
validates as finite. `BoundingBox` rejects non-finite or inverted corners, and
`BoundingBox.Transform` performs its eight-corner arithmetic in double precision
and saturates results to finite extremes. `Frustum.Create` and
`Frustum.Intersects` fail open on a non-finite or singular matrix, a degenerate
plane, invalid bounds, or a non-finite distance, so malformed visibility data
cannot discard potentially visible geometry. `Frustum` remains a class with its
existing factory and query API; a renderer can own one scratch instance and call
`Update` to refresh its six planes without allocating a new frustum each frame.
Culling reads scene data only: it does not remove objects, release resources, or
modify transforms. The per-frame cost is bounded by the scene object limit
(`Scene.MaxObjectCount`, 100,000), uses fixed-size counters, and creates no
per-object allocation or vertex traversal. It adds no native handle, GPU
resource, or third-party dependency.

LOD configurations are application-owned data validated at construction.
`MeshLodLevel` rejects a null mesh, a negative or non-finite activation distance,
and `MeshLod` additionally rejects empty or oversized lists, null entries, a
first level that does not activate at zero, non-increasing distances, a
non-finite or nonpositive maximum render distance, and non-finite or negative
hysteresis. `SceneObject.Lod` requires the highest-detail mesh to be the object's
primary `MeshData` reference. `LodMath.DistanceToBounds` uses double precision,
saturates finite extremes, and fails safe to zero for a non-finite point or
invalid bounds. The distance and LOD path computes conservative bounds from
validated mesh data, keeps its eight-level usage counters fixed-size, and does
not parse files or load assemblies. A malformed configuration throws before a
frame is submitted; it is never silently repaired.

LOD selection state is stored per `SceneObject` and is not shared by a reused
configuration. Assigning a configuration or disabling LOD resets that state.
Selection reads transforms, distance, and frustum results only; it does not
modify scene geometry or resource ownership.

## Scene chunk streaming

Chunk definitions and their content callbacks are application-supplied code and
data, not a sandboxed plugin system. `SceneChunkDefinition` validates its
identifier length, whitespace, and control characters, and `BoundingBox`
validates finite ordered bounds. `SceneStreamingConfiguration` rejects
non-finite or negative distances, an unload distance that is not greater than
the load distance, a load budget below one, and a non-finite or negative retry
delay. `SceneStreamer.Update` rejects a non-finite focus and a negative,
non-finite, or overflowing elapsed time before mutating the scene.

A content callback is isolated to its chunk. The streamer caps the known chunk
count at 10,000 and rejects null content, null objects, duplicate object
references, objects already present in the scene, and content above
`Scene.MaxObjectCount` before committing the chunk. If adding
objects fails part-way through, it removes everything it added. Ordinary
callback exceptions are caught and converted to a bounded diagnostic; a failed
chunk is not retried until the configured delay has elapsed. `OutOfMemoryException`
is not swallowed. Release
callbacks run only after removal, and their ordinary exceptions are counted and
reported. The streamer does not open files, resolve paths, discover assemblies,
use reflection, or execute descriptor strings as code. A callback that reads
assets must apply the path, format, size, and failure checks documented in this
baseline; the streamer cannot enforce them for arbitrary application code.

The streamer mutates one `Scene` on the calling thread. It must not run
concurrently with rendering, culling, or another scene mutation. It owns scene
membership only; CPU assets remain application-owned and GPU resources remain
renderer-owned. `MaterialRenderer.PruneUnusedResources` waits for
`SDL_WaitForGPUIdle` before releasing an unreferenced cache entry and releases
nothing when the wait fails. Unloading a chunk alone is not evidence that CPU or
GPU memory was reclaimed.

## Raycasting and picking

`Nymeria.Core.Raycasting` is a CPU-only query layer. It performs no file or
network access, allocation in response to a query, native interop, GPU call, or
telemetry. `Ray` rejects non-finite origins and zero-length, non-finite, or
non-normalizable directions. `RayIntersections` rejects invalid boxes and invalid
maximum distances as a safe miss, uses double precision, and never returns a NaN
hit point or distance. `RaycastHit` validates and normalizes its public
constructor inputs. `ViewportRay` rejects zero or negative viewport dimensions,
non-finite pixels or matrices, and non-invertible view or view-projection
matrices instead of producing a NaN ray.

`SceneRaycaster` scans the current scene in insertion order, so its work is
bounded by `Scene.MaxObjectCount` (100,000). It reads bounds and transforms; it
does not read mesh triangles, allocate a spatial index, or cache derived data.
One click therefore cannot trigger unbounded mesh traversal. The query runs on
the caller's thread and must not overlap scene mutation or a streaming update.

Picking does not consult frustum or distance culling and does not modify
visibility or GPU state. `SceneObject.IsPickable` excludes an object without
changing rendering. Unloaded streamed objects are absent from the scene and
cannot produce hits, but a previously returned hit holds an ordinary managed
reference that can become stale as a selection. Applications must validate it
with `Scene.Contains` and must not treat it as a native handle. See
[PICKING.md](PICKING.md).

## Collision detection

`Nymeria.Core.Collision` is a CPU-only overlap-query layer with no file or
network access, native interop, GPU call, reflection, or telemetry. `BoundingBox`
and `BoundingSphere` validate finite values and ordered bounds at construction;
`BoundingSphere` also rejects negative or non-finite radii. `CollisionShape` can
contain only those validated primitives, and a default empty shape is a safe
no-op. Box, sphere, and mixed overlap tests use double precision and inclusive
contact semantics.

World conversion uses the object's current affine model matrix. Boxes transform
all eight corners, so rotation and non-uniform or negative scale are covered
conservatively. Spheres require a finite projective-free basis with orthogonal
rows; shear and non-finite or projective transforms are rejected rather than
under-reporting extent. Singular zero-scale transforms remain valid and can
produce flat boxes or point spheres. Extreme finite results saturate instead of
becoming NaN or infinite.

`SceneCollision` reads only `Scene.Objects`, `SceneObject.Collider`, and
`SceneObject.Transform`. It appends results to caller-owned collections, does not
retain scene objects, and rejects a target that is not currently in the queried
scene. Removed or unloaded streamed objects are absent from the next query; a
stored `CollisionPair` holds ordinary managed references and must be validated
with `Scene.Contains` before reuse. Pair queries are intentionally O(n²) and
bounded by `Scene.MaxObjectCount`; they have no broad phase, cached world bounds,
parallel work, or background thread. Callers must not run collision queries
concurrently with scene mutation or a streaming update. See
[COLLISION.md](COLLISION.md).

## Native audio ownership and threading

- `AudioSystem` owns the one `SDL_AudioStream` that also owns the playback
  device. `DestroyAudioStream` closes the device; do not close or rebind the
  device elsewhere.
- The managed callback delegate is rooted for the life of the system. Its
  `GCHandle` is freed only after the stream has been destroyed, so a queued
  native callback cannot observe a collected system.
- The callback runs on SDL's audio thread. It holds one short managed lock while
  reading voice state and mixing into a preallocated scratch buffer, calls only
  `SDL_PutAudioStreamData`, and performs no file I/O, logging, or blocking wait.
  Nymeria's mix path is allocation-free after warm-up. A managed exception is
  contained and treated as device loss rather than escaping into the native
  callback.
- Device creation, playback control, `Update`, and disposal run on the process
  main thread. The callback never calls the public API. The public API exposes
  no SDL type, so `Nymeria.Core` and `Nymeria.Formats.Wav` remain device-free.
- A voice holds a clip reference while it can still play. `AudioClip.Dispose`
  fails instead of freeing samples that a playing or paused voice can still read;
  stopping or disposing the voice releases the reference exactly once. Disposing
  the system stops and disposes every voice before destroying the stream.
- Listener, voice, and master-gain values are validated before they reach the
  mixer. Non-finite positions and gains are rejected; spatial math fails to
  silence or a centered pan when a runtime value is non-finite.
- Device loss or a failed queue stops voices on the next `Update` and refuses new
  playback. The system does not silently reopen a device.

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
  managed `MeshData`, `Material`, `TextureData`, and optional `MeshLod`; the
  renderer owns and releases one upload per distinct mesh, including every
  distinct LOD variant, and one GPU texture per distinct texture. Removing an
  object or changing its LOD configuration must not dispose data that another
  object still references. Each variant is uploaded once and cached by mesh
  reference; selecting a level never allocates or releases a GPU buffer.
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

## Performance instrumentation

`Nymeria.Core.Diagnostics.FrameTimingWindow` is opt-in, headless, and bounded by
its configured capacity. It accepts only finite, non-negative samples, discards
a configured number of warm-up samples, and retains no more than its fixed
capacity. `GetSummary` allocates one copy for sorting; callers must not call it
per frame. The type has no file, network, native, or GPU access.

`examples/HelloBenchmark` is a local diagnostic. It creates a window and GPU
device through the same validated code paths as the other examples, uses only
project-owned scene data, and writes only console output. It does not disable
validation, add native code, collect telemetry, or accept untrusted paths or
formats. Benchmark and profiling modes must not weaken the checks described in
this document. CI runs the headless timing tests but never launches the
benchmark window or claims a GPU measurement.

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

Audio changes additionally require negative WAV tests for every new rejection
path, limit boundary tests, and mixer ownership and lifecycle tests. Device
behavior that cannot run headlessly must be recorded as a manual verification or
as an explicit skipped test with a reason.

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
SDL3, Metal, CoreAudio, and the operating system are trusted native dependencies
and are not sandboxed. The GPU examples, including texture upload, mip upload,
compressed format capability, and sampling, are not exercised in CI. The WAV
decoder is not fuzzed; a valid file at its limits can still request 32 MiB of
decoded float samples per clip, and loaded clips remain in memory until disposed.
The audio callback's lock is short and allocation-free by design, but real-time
scheduling and device behavior remain the platform's responsibility. Device
creation and playback are verified manually and are not claimed as a CI result;
hot-unplug follows the documented device-loss path but was not exercised.
