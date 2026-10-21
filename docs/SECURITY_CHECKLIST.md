# Security checklist

Use this checklist when reviewing a change that handles untrusted input, touches
native or GPU resources, changes dependencies, or modifies repository automation.
It complements [SECURITY.md](SECURITY.md) and the
[first audit](SECURITY_AUDIT_01.md). A checked list is evidence of review, not a
guarantee.

## Scope and trust boundaries

- [ ] The change identifies which data is untrusted and where it crosses into
      managed or native code.
- [ ] Parsers and the PNG decoder remain headless: no SDL3, Metal, GPU, window, or
      working-directory dependency.
- [ ] The change does not add a model-relative path lookup or material-file
      access. An explicit image path supplied by the user was reviewed for
      traversal and sandboxing.
- [ ] New public API or architecture was approved by the maintainer.
- [ ] No speculative scaffolding, placeholder sources, or unrequested features
      were added.

## Binary format parsing

- [ ] Magic values, versions, flags, keys, and section tags are validated before
      use.
- [ ] Duplicate or unknown sections are rejected, not ignored, unless the format
      documentation explicitly permits them.
- [ ] Declared counts, offsets, lengths, and strides are checked against the
      remaining input before allocation.
- [ ] Size and offset arithmetic uses checked arithmetic or a wider type and
      cannot overflow.
- [ ] Every read is bounded by the available bytes; truncation fails with a
      diagnostic.
- [ ] Non-finite floats, out-of-range indices, and unsupported features are
      rejected.
- [ ] Input, vertex, triangle, subset, and line limits are enforced before the
      corresponding collection grows.
- [ ] Failures throw `ModelFormatException`; no partial geometry or silent
      fallback is returned.
- [ ] New limits are documented in the format document and have boundary tests.

## Text format parsing

- [ ] Decoding is strict UTF-8; invalid byte sequences are errors, not `U+FFFD`.
- [ ] Lines and tokens are bounded. A line limit fails before the line is parsed.
- [ ] Numeric parsing uses invariant culture, rejects non-finite values, and
      rejects trailing garbage.
- [ ] Zero, negative, and out-of-range indices fail with line-based diagnostics.
- [ ] Seekable and non-seekable inputs both enforce the input limit.
- [ ] Streaming reads stop as soon as a limit is crossed.

## Image decoding

- [ ] The image signature, chunk order, chunk lengths, and every chunk checksum are
      validated before the chunk is used.
- [ ] Image dimensions and the decoded payload are checked against documented
      limits before allocation.
- [ ] Encoded input is bounded for both seekable and non-seekable streams.
- [ ] Unsupported color types, bit depths, interlacing, filters, palette indices,
      and unknown critical chunks are rejected.
- [ ] Decompression is limited to the exact scanline payload implied by the
      validated header; short and extra output both fail.
- [ ] Decoding returns validated texture data or throws; it never returns partial
      or repaired pixels.
- [ ] The caller owns the stream and the decoder leaves it open.
- [ ] New limits are documented and have boundary and malformed-input tests.
- [ ] Format selection uses signatures where the format has one; an extension may
      only select a decoder that has no signature, and its header is still fully
      validated.
- [ ] JPEG segment lengths and the end-of-image marker are validated before
      decoding, so a truncated file cannot be accepted as a complete image.
- [ ] TGA RLE packets are checked against the remaining pixel count and the input
      length before any pixel is written.
- [ ] DDS header, DX10 extension, mip chain, block/row sizes, and the exact payload
      length are validated with checked arithmetic; unsupported formats fail
      explicitly instead of being approximated.
- [ ] A new image dependency was reviewed for maintenance, license, transitive
      packages, native code, and NuGet advisories, and its version is pinned.

## Native and GPU resources

- [ ] Handles are owned by one wrapper and are not destroyed elsewhere.
- [ ] Disposed wrappers cannot return stale native handles.
- [ ] The window outlives the device, and the device outlives its resources.
- [ ] Every acquisition has a matching release exactly once, in reverse order.
- [ ] GPU resources are released only after pending work that references them has
      completed, including in `catch` paths.
- [ ] After swapchain acquisition, the command buffer is submitted, not
      cancelled. Cancellation is limited to the no-texture path.
- [ ] Zero-sized or unusable drawables submit an empty command buffer instead of
      leaking an acquired one.
- [ ] SDL3 return values are checked and failures produce actionable errors.
- [ ] Buffer sizes, strides, and bindings match the shader layout and use the
      buffer's actual element size.
- [ ] Texture payload size, row pitch, mip count, compressed block layout, and
      declared sampler/resource counts match the upload and shader layout.
- [ ] A compressed format is capability-checked before upload, and only mip levels
      that exist and are block aligned are uploaded or sampled.
- [ ] A shared mesh, material, or texture reference is not released by a renderer
      that did not create the corresponding GPU resource, and a GPU handle release
      does not dispose shared managed data.
- [ ] `unsafe`, custom P/Invoke, and new native dependencies were explicitly
      approved and reviewed.

## Dependencies and automation

- [ ] No dependency was added or upgraded without maintainer approval.
- [ ] `dotnet restore` succeeds with `NuGetAudit` enabled (it fails on reported
      vulnerabilities because warnings are errors).
- [ ] `dotnet list Nymeria.slnx package --vulnerable --include-transitive`
      reports no known vulnerable package.
- [ ] GitHub Actions remain pinned to full commit SHAs with least-privilege
      permissions.
- [ ] No untrusted event value is interpolated into a shell command.
- [ ] No credential, token, private key, environment file, or build output is
      committed.
- [ ] Workflow policy and repository settings were not changed without approval.

## Tests and verification

- [ ] Negative tests cover each new rejection path.
- [ ] Boundary tests cover the exact limit and the first value over it.
- [ ] Bounded mutation or truncation tests cover binary structure changes where
      practical.
- [ ] Tests are deterministic, headless, and use the existing test projects.
- [ ] Valid behavior and public API compatibility are preserved.
- [ ] Release build has zero warnings and all headless tests pass.
- [ ] Documentation and the audit register are updated when limits or behavior
      change.
- [ ] The change is committed in small logical units, pushed, and the actual
      GitHub Actions result is confirmed. A queued or assumed result is not a
      pass.
- [ ] No force-push or published-history rewrite was performed without explicit
      approval.

## Known verification gaps

- GPU and window behavior is validated by the manual [smoke test](SMOKE_TEST.md),
  not by CI.
- There is no general fuzzing campaign, address sanitizer run, SBOM, license scan,
  or third-party binary analysis.
- The image decoders have targeted malformed and boundary tests, not a fuzzing
  campaign. Their worst accepted case still allocates the documented payload.
- JPEG decoding depends on `StbImageSharp`. NuGet advisory checks cover the pinned
  package, but Nymeria does not audit or fuzz its internals. The structural
  validation wrap reduces, but does not eliminate, that trust.
- NuGet advisories do not prove the absence of unknown vulnerabilities.
