# Performance measurement

This document describes how Nymeria measures CPU frame cost today, how to run
the reproducible benchmark, and what the numbers do and do not mean. The first
recorded baseline and its findings are in
[PERFORMANCE_BASELINE_01.md](PERFORMANCE_BASELINE_01.md).

The scope is deliberately narrow. There is no engine-wide profiler, frame
capture integration, GPU timer, telemetry, or background collection. The only
reusable instrumentation is a bounded frame-timing window in `Nymeria.Core`.The `HelloBenchmark` example is the reproducible workload; `HelloLOD` also uses the
window for its once-per-second render-time average and p95, and `HelloStreaming`
uses separate windows for its streaming-update and render intervals.

## Instrumentation

`Nymeria.Core.Diagnostics.FrameTimingWindow` is a fixed-capacity rolling window
of frame durations in milliseconds:

```csharp
var window = new FrameTimingWindow(capacity: 600, warmupSamples: 120);
long start = Stopwatch.GetTimestamp();

// ... one frame ...

double milliseconds =
    (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
window.AddSample(milliseconds);

FrameTimingSummary summary = window.GetSummary();
```

The window:

- stores at most `capacity` samples and replaces the oldest sample when full;
- discards the first `warmupSamples` accepted samples, so startup, first uploads,
  shader compilation, and other warm-up costs are not part of steady state;
- rejects negative and non-finite samples without consuming warm-up, and counts
  them in `RejectedSamples`;
- accepts an exact zero as a valid sample, but reports a frame rate of zero
  rather than infinity;
- reports the average, nearest-rank p95, maximum, sample count, and FPS derived
  from the average duration.

`GetSummary` copies and sorts the retained samples. Call it once per reporting
interval, not once per frame. `AddSample` does not allocate. The type is not
thread-safe; give each producer its own window and merge only on the thread that
owns it.

Use `Stopwatch.GetTimestamp()` and `Stopwatch.Frequency` for monotonic timing.
Do not use wall-clock dates for durations. The benchmark divides by the
frequency only when converting to milliseconds, so the raw tick comparison never
depends on the current culture.

## Phase definitions

The benchmark records these non-overlapping CPU intervals:

| Phase | Boundary | Includes | Does not include |
| --- | --- | --- | --- |
| Events | start of frame to return from `Window.ProcessEvents(0)` | SDL event pumping and close handling | rendering |
| Render | return from events to return from `MaterialRenderer.Render` | resource-cache lookup, optional bounds/frustum/distance/LOD selection, model and normal-matrix math, uniform pushes, draw recording, command-buffer submission, swapchain acquisition | waiting for the GPU to finish, presentation latency, driver work after submission |
| Pacing | return from render to end of frame | `SDL.Delay` for the remainder of the 16 ms budget when pacing is enabled | work when pacing is disabled |
| Total frame | start of frame to end of frame | events, render, pacing, and OS scheduling between them | startup and warm-up frames |

`Render` is CPU wall-clock time. It does not report GPU execution time, and it
must not be used as one. A fast CPU submission can still be followed by a slow
GPU frame; the CPU timer cannot see that. Conversely, a swapchain acquisition can
block on presentation even though the code is nominally recording commands.

The benchmark also reports, from the existing visibility statistics:

- instances tested;
- visible, frustum-culled, distance-culled, and LOD-drawn instances, averaged
  over measured frames;
- actual indexed draw calls submitted, average and maximum;
- submitted triangles, average and maximum;
- drawn-instance counts per LOD level.

At the moment this renderer submits one indexed draw call per visible instance.
Keep those counters separate: `DrawCalls` is a property of the renderer, not of
the culling or LOD helper, and the instance counts are not GPU primitive counts.
`SubmittedTriangles` is the sum of index counts divided by three for submitted
meshes; it is not a fragment count and does not measure GPU execution.

Managed allocation is sampled with `GC.GetAllocatedBytesForCurrentThread()` over
the measured loop. It covers the benchmark thread only, not the SDL/Metal driver
or other threads, and its byte counts are rounded by the runtime. `GC.CollectionCount`
is process-wide. These values are useful for spotting per-frame garbage, not for
process memory or GPU-memory accounting. The benchmark does not report GPU
memory because SDL3 does not expose a reliable portable number for it here.

## Reproducible benchmark

`examples/HelloBenchmark` is a deterministic, auto-exiting workload. It creates
three shared cube detail variants (768, 108, and 12 triangles), four shared
untextured materials, and a fixed camera. It places one sixteenth of the
instances behind the camera and the rest on a deterministic grid that grows away
from the camera and past its side and far planes. The scene has rotation,
non-uniform scale, and negative scale, so the world-bounds path is exercised.
Every object carries one shared `MeshLod` with thresholds 18 and 45, maximum
distance 90, and hysteresis 0.5. No animation, input, texture decoding, or file
I/O runs during measurement.

Build once, then run each scene size in Release configuration:

```bash
dotnet build Nymeria.slnx --configuration Release

dotnet run --project examples/HelloBenchmark/HelloBenchmark.csproj \
  --configuration Release -- --instances 512 --warmup 120 --frames 600
dotnet run --project examples/HelloBenchmark/HelloBenchmark.csproj \
  --configuration Release -- --instances 2048 --warmup 120 --frames 600
dotnet run --project examples/HelloBenchmark/HelloBenchmark.csproj \
  --configuration Release -- --instances 8192 --warmup 120 --frames 600

# LOD comparison with culling held off
dotnet run --project examples/HelloBenchmark/HelloBenchmark.csproj \
  --configuration Release -- \
  --instances 2048 --culling off --lod both --warmup 120 --frames 600
```

Options:

| Option | Default | Meaning |
| --- | --- | --- |
| `--instances <count>` | `2048` | Scene size, from 1 through the scene's 100,000-object limit |
| `--culling <on\|off\|both>` | `both` | Culling comparison; `both` runs off first, then on, with a separate warm-up for each |
| `--lod <on\|off\|both>` | `off` | LOD comparison; `both` runs off first, then on for each culling mode, with a separate warm-up for each |
| `--no-distance-culling` | enabled | Disables every configured maximum render distance; used to isolate LOD from whole-instance distance culling |
| `--warmup <frames>` | `120` | Frames excluded before each measured mode |
| `--frames <count>` | `600` | Measured frames per mode |
| `--no-pacing` | pacing on | Removes the 16 ms frame-budget delay |
| `--help` | | Prints usage |

The benchmark runs on the main thread, opens a window, and exits with status
`0` after the requested modes complete. A closed window during warm-up or
measurement exits with status `2`; invalid arguments, a missing native library,
or an initialization failure exits with status `1`. No report file is written;
capture standard output if a record is needed.

`HelloStreaming` is a diagnostic demo, not an auto-exiting benchmark. It times
`SceneStreamer.Update`, including synchronous chunk callbacks and scene
mutation, as a CPU wall-clock interval. GPU cache pruning is timed separately and
includes waiting for `SDL_WaitForGPUIdle`; do not fold that wait into steady-state
streaming cost. Its render interval uses the same definition as the benchmark.
Compare `HelloStreaming` with streaming enabled and `--no-streaming` at the same
camera path only as a behavior demonstration. It does not establish that
streaming improves FPS, and a frozen baseline may draw a different amount of
geometry after a teleport by design.

For a comparable result, use the same machine, display scaling, resolution,
power state, and build configuration. Close GPU-heavy applications. Run each
configuration three times as separate processes and report the range, not only
the best run. Record the macOS version, architecture, GPU, runtime version,
logical and drawable window size, backend, pacing state, warm-up and sample
counts, and scene size alongside the numbers.

## Interpreting the output

- **Frame-cap limited.** With pacing enabled, total frame time is typically
  about 17.5–18.3 ms and FPS about 55–57 on a 60 Hz display. The 16 ms delay plus
  event/render overhead and scheduling account for the difference from a nominal
  60 FPS. The exact paced total can shift between sessions; this is not evidence
  of a GPU bottleneck. Use `render` and the instance counts to compare CPU work;
  use the total frame only to describe the paced loop.
- **CPU-bound evidence.** A rising `render` time as instance count grows, with
  little change in culling counts, points at CPU submission or scene traversal.
  It still does not prove the GPU is idle.
- **Culling trade-off.** Culling helps only when the cost of testing instances
  plus the reduced submission cost is lower than submitting everything. At small
  scene sizes the test can cost slightly more than the draws it removes; at large
  sizes the saved per-instance submission dominates. Compare both modes at the
  same size rather than assuming culling always wins.
- **LOD trade-off.** LOD reduces submitted triangles but not the draw-call count
  in this renderer. It adds bounds transformation and selection work per object.
  Compare `lod off` and `lod on` at the same instance count and culling mode; do
  not attribute a GPU saving to the CPU `render` time. Distance culling is a
  separate effect because it removes whole draw submissions. See
  [LOD.md](LOD.md) for the recorded comparison and its conditions.
- **GPU-bound cases are inconclusive here.** A slow total frame with a small
  `render` time may indicate presentation or GPU limits, but this instrumentation
  cannot distinguish them. Use Instruments, Metal System Trace, or a GPU timer
  only when that distinction matters; those tools are not part of the build.
- **`--no-pacing` is not a GPU benchmark.** Without pacing, swapchain
  presentation can block or skip frames. The benchmark labels skipped frames and
  warns that totals are not a throughput measure. Do not compare an unpaced run
  to a paced one as if only the delay changed.
- **Ignore differences smaller than run-to-run variance.** On the recorded Apple
  M4 machine, repeated 8192-instance runs during this work varied by up to
  roughly 10% in `render` time; the final three-run set varied by about 2.4%. A
  change smaller than the observed spread needs more repetitions and a
  controlled environment before it is called an improvement.

## Measurement workflow

1. Read the current baseline and note the exact workload, build, and machine.
2. Run the benchmark at least three times per size and mode. Keep the paced
   comparison as the default; use `--no-pacing` only to investigate presentation
   behavior.
3. Identify the phase that moves. Check counts and allocations before changing
   code. Do not optimize a phase that did not move.
4. Make one small change at a time, rebuild, and repeat the same runs.
5. Keep the change only if correctness tests pass and the difference is larger
   than the observed variance or removes a measured, avoidable cost such as
   per-frame garbage. Report the raw numbers either way.
6. Update this document and the baseline when the workload, instrumentation, or
   measurement environment changes.

## Limitations

- CPU wall-clock timings only; no GPU timestamps, occupancy, bandwidth, or
  frame-capture data.
- No automatic CI benchmark. CI builds and tests the code but does not launch a
  window or GPU device, so these numbers are manual and machine-specific.
- The benchmark's fixed camera and scene layout are intentionally simple. They
  are not a game workload and do not cover texture upload, shader compilation,
  file parsing, streaming IO, streaming content callbacks, audio, physics, or
  input latency. `HelloStreaming` reports streaming update and prune time but has
  no automated benchmark or recorded throughput baseline.
- The culling comparison changes only the renderer's optional frustum-culling
  flag. The LOD comparison changes only `LodEnabled`. Neither changes the scene,
  materials, transforms, or uploads; all three variants are uploaded during
  warm-up in every mode.
- Managed allocation sampling is current-thread and coarse. It does not measure
  native allocations, driver allocations, or GPU memory.
- The repository has no Task 10.1 diagnostics or Discord RPC integration, so the
  benchmark does not integrate with either.

## Related documents

- [PERFORMANCE_BASELINE_01.md](PERFORMANCE_BASELINE_01.md) — recorded baseline,
  findings, and the one implemented optimization.
- [CULLING.md](CULLING.md) — bounds, frustum, statistics, and culling limits.
- [LOD.md](LOD.md) — LOD contract, counters, and the recorded LOD comparison.
- [STREAMING.md](STREAMING.md) — chunk streaming diagnostics, ownership, and
  the observed streaming/prune costs.
- [ARCHITECTURE.md](ARCHITECTURE.md) — project scope and dependency direction.
- [SMOKE_TEST.md](SMOKE_TEST.md) — manual GUI checks, including the benchmark.
