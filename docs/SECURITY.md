# Security baseline

This document records the security expectations that currently apply to
Nymeria. It is a baseline for contributors, not a claim that the project is
secure or fully audited. The first repository-wide review and its findings are
in [SECURITY_AUDIT_01.md](SECURITY_AUDIT_01.md).

## Trust model

| Input | Trust |
| --- | --- |
| Project source, embedded shaders, checked-in test fixtures | Trusted project content. |
| OBJ and O3D files, streams, and model paths supplied at run time | Untrusted. |
| Package restore input and GitHub Actions metadata | Untrusted external data that the build must validate or pin. |
| SDL3, Metal, and the operating system | External native dependencies. Nymeria does not sandbox them. |

The parsers are headless libraries. They must not depend on SDL3, Metal, a GPU,
a window, or the current working directory. This keeps parsing testable and keeps
untrusted input away from the native boundary.

## Untrusted model input

The OBJ and O3D parsers must:

- Validate magic values, versions, flags, keys, section tags, duplicate sections,
  declared counts, offsets, lengths, and remaining bytes before allocating or
  using data.
- Use checked arithmetic or wider types for sizes, offsets, and range checks.
- Reject malformed text, invalid indices, truncation, unsupported features,
  non-finite numbers, and counts beyond the documented limits.
- Fail with `ModelFormatException` and never return partial or silently repaired
  geometry. Ordinary I/O errors from the caller's stream may propagate.
- Read text as strict UTF-8. Invalid byte sequences are errors, not replacement
  characters.
- Reject input as soon as a limit is crossed, including while streaming a
  non-seekable source.
- Ignore external material and texture references. `mtllib`, `maplib`, and
  `usemap` are not opened or resolved, and O3D textures are not loaded. There is
  no model-relative path resolution and therefore no path traversal surface in
  the parsers.

The current defaults are shared by both parsers unless noted:

| Limit | Default |
| --- | --- |
| Input size | 64 MiB |
| Positions, texture coordinates, and normals | 2,000,000 values each |
| Deduplicated mesh vertices | 2,000,000 |
| Triangles | 4,000,000 |
| Material subsets | 65,536 |
| OBJ source line length | 1,048,576 characters |

These are practical viewer limits, not format requirements. A future service that
accepts untrusted models should lower them or isolate parsing in a memory-limited
process.

The examples add a separate GPU budget of 2,000,000 converted vertices and
6,000,000 indices. This budget belongs to the viewer, not to the format contract,
and is enforced before the model is uploaded.

## Native and GPU ownership

- Native handles are owned by the managed wrapper that created them. Do not
  destroy a handle obtained from another object.
- `Window.Handle` and `GpuDevice.Handle` throw `ObjectDisposedException` after
  disposal so a stale native pointer cannot be passed to SDL3.
- The window must outlive the GPU device, and the device must outlive renderer
  resources.
- Wait for the GPU to become idle before releasing resources that pending work
  may reference, including in failure paths.
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

Every change that touches parsing, native ownership, GPU resources, dependencies,
or automation must:

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
license scan, or third-party binary analysis. Parser work is linear in accepted
input, so a local attacker can still consume the documented maximum. The O3D
parser's worst case is bounded but memory-amplifying. SDL3, Metal, and the
operating system are trusted native dependencies and are not sandboxed. The GPU
examples are not exercised in CI.
