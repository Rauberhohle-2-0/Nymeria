# Performance baseline 01

Recorded on 2026-10-21 with `examples/HelloBenchmark`, Release configuration.
This is the first Nymeria performance baseline. It is a CPU-side measurement of
the scene and material submission path, not a GPU benchmark or a game workload.
The methodology and definitions are in [PERFORMANCE.md](PERFORMANCE.md).

## Summary

- The paced loop is frame-cap limited: the final session held total frame time
  near 18.2 ms and FPS near 55 on a 60 Hz display. The initial baseline session
  held near 17.5 ms and 57 FPS. Because the paced total shifted between sessions,
  `render` is the comparison metric, not total frame time or FPS.
- CPU render cost grows with submitted instances: about 0.83 ms at 512
  instances, 2.26 ms at 2048, and 4.51 ms at 8192 with culling off (final
  three-run means).
- Culling is a clear CPU win at large counts: at 8192 it removes 6949 of 8192
  instances and lowers mean render time from 4.51 ms to 3.03 ms. At 512 it is
  slightly slower than drawing everything, because there is little work to save.
- One avoidable per-frame allocation was found and removed. Culling-on
  allocation fell from 184 to 40 bytes per frame. This did not produce a
  measurable frame-time improvement, and is reported as an allocation and
  GC-pressure fix rather than a speedup.
- No GPU timing is available in this baseline. The data cannot establish whether
  the GPU is saturated. It does establish that the paced loop is not CPU-bound
  by render submission at these scene sizes.

## Test conditions

| Item | Value |
| --- | --- |
| Machine | MacBook Air (Mac16,12), Apple M4 |
| CPU | 10 cores (4 performance, 6 efficiency) |
| GPU | Apple M4, 8 cores, Metal 4 |
| Memory | 16 GB |
| OS | macOS 27.0.1 (build 26A434) |
| Runtime | .NET 10.0.13 |
| SDK | 10.0.402 |
| Build | Release |
| Graphics backend | SDL3 GPU API, Metal |
| Main display | SAMSUNG, 2304 × 1296 points at 60 Hz (4608 × 2592 physical) |
| Benchmark window | 960 × 540 logical, 1920 × 1080 drawable |
| Pacing | 16 ms target delay (enabled unless stated) |
| Warm-up | 120 frames per mode |
| Samples | 600 frames per mode |
| Culling order | Off first, then on, each with its own warm-up |

The power source, thermal state, and background process load were not controlled
or recorded. Run-to-run variance on this machine is therefore part of the result;
see the repeat measurements below. The initial baseline and the final
optimization runs were separate sessions. Their paced totals differed (about
17.5 ms/57 FPS initially, 18.2 ms/55 FPS finally), while `render` stayed in the
same range. Compare `render` and the counters across sessions, not total frame or
FPS.

## Workload

`HelloBenchmark` creates:

- one shared 24-vertex cube `MeshData`;
- four shared untextured `Material` tints;
- a fixed camera at `(0, 10, 26)` with a slight downward pitch and a 120-unit far
  plane;
- one sixteenth of the instances behind the camera, and the rest on a
  deterministic grid with rotation, non-uniform scale, and occasional negative
  scale;
- no animation, input, texture decode, file I/O, or resource churn during the
  measured interval.

Scene sizes were 512, 2048, and 8192 instances, with culling off and on. The
shared mesh is uploaded once per process; the benchmark creates the renderer
before warm-up, so upload cost is outside the measured interval.

## Method

Each mode is a separate run of 120 warm-up frames followed by 600 measured
frames. For every measured frame the benchmark records:

- total frame, event, render, and pacing CPU durations;
- instances tested, visible, and culled;
- actual indexed draw calls submitted;
- managed bytes allocated on the benchmark thread and process-wide GC counts.

`render` is the return-to-return duration of `MaterialRenderer.Render`. It
includes culling, matrix math, uniform pushes, draw recording, command-buffer
submission, and swapchain acquisition. It excludes GPU execution and
presentation latency. See [PERFORMANCE.md](PERFORMANCE.md) for the exact phase
definitions.

The 512, 2048, and 8192 scenes were each run three times after the final code
change. The 8192 case was also run twice before the change. The tables below
report individual runs and three-run means where available. Percentiles are the
nearest-rank p95 reported by `FrameTimingWindow`.

## Baseline before the optimization

These are the initial runs, before the renderer reused one frustum instance. They
are the comparison point for the allocation change. The 512 and 2048 cases have
one run each; the 8192 case has two.

| Instances | Culling | Total avg | Render avg | Render p95 | Render max | Managed | Visible / culled | Draw calls |
| ---: | :---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 512 | off | 17.46 | 0.84 | 1.25 | 2.58 | 40 B/frame | 512 / 0 | 512 |
| 512 | on | 17.46 | 0.88 | 1.31 | 2.36 | 184 B/frame | 445 / 67 | 445 |
| 2048 | off | 17.51 | 2.26 | 3.42 | 4.63 | 40 B/frame | 2048 / 0 | 2048 |
| 2048 | on | 17.43 | 1.87 | 2.92 | 4.48 | 184 B/frame | 1144 / 904 | 1144 |
| 8192 | off | 17.43 | 4.37 | 5.01 | 5.92 | 40 B/frame | 8192 / 0 | 8192 |
| 8192 | on | 17.46 | 3.06 | 4.40 | 5.55 | 184 B/frame | 1243 / 6949 | 1243 |
| 8192 (repeat) | off | 17.40 | 4.31 | 4.93 | 6.86 | 40 B/frame | 8192 / 0 | 8192 |
| 8192 (repeat) | on | 17.54 | 2.55 | 4.07 | 5.97 | 184 B/frame | 1243 / 6949 | 1243 |

All events were about 0.1–0.3 ms, and no run recorded a Gen0, Gen1, or Gen2
collection during the 600 measured frames. FPS was 57.0–57.5 in every paced run.

## Final measurements after the optimization

The final code reuses one frustum per renderer. The measurements below were
taken after that change, in three separate processes per size.

### 512 instances

| Run | Culling | Total avg | Render avg | Render p95 | Render max | Managed | FPS |
| --- | :---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | off | 18.29 | 0.84 | 1.35 | 2.42 | 40 B/frame | 54.7 |
| 1 | on | 18.27 | 0.85 | 1.33 | 3.28 | 40 B/frame | 54.7 |
| 2 | off | 18.26 | 0.84 | 1.34 | 3.02 | 40 B/frame | 54.8 |
| 2 | on | 18.25 | 0.86 | 1.36 | 2.70 | 40 B/frame | 54.8 |
| 3 | off | 18.25 | 0.81 | 1.22 | 2.68 | 40 B/frame | 54.8 |
| 3 | on | 18.24 | 0.87 | 1.39 | 2.76 | 40 B/frame | 54.8 |
| **Mean** | **off** | **18.27** | **0.83** | — | — | **40 B/frame** | **54.8** |
| **Mean** | **on** | **18.25** | **0.86** | — | — | **40 B/frame** | **54.8** |

Visible/culled counts were 512 / 0 with culling off and 445 / 67 with culling
on, unchanged from the baseline. Draw calls were 512 and 445 respectively.

### 2048 instances

| Run | Culling | Total avg | Render avg | Render p95 | Render max | Managed | FPS |
| --- | :---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | off | 18.19 | 2.26 | 3.52 | 4.89 | 40 B/frame | 55.0 |
| 1 | on | 18.21 | 1.80 | 2.83 | 4.34 | 40 B/frame | 54.9 |
| 2 | off | 18.24 | 2.21 | 3.63 | 5.71 | 40 B/frame | 54.8 |
| 2 | on | 18.21 | 1.81 | 3.09 | 4.82 | 40 B/frame | 54.9 |
| 3 | off | 18.20 | 2.30 | 3.58 | 5.71 | 40 B/frame | 55.0 |
| 3 | on | 18.19 | 1.81 | 3.04 | 4.34 | 40 B/frame | 55.0 |
| **Mean** | **off** | **18.21** | **2.26** | — | — | **40 B/frame** | **54.9** |
| **Mean** | **on** | **18.20** | **1.81** | — | — | **40 B/frame** | **54.9** |

All three 2048 runs were consistent. Visible/culled counts were 2048 / 0 and
1144 / 904, unchanged.

### 8192 instances

| Run | Culling | Total avg | Render avg | Render p95 | Render max | Managed | FPS |
| --- | :---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | off | 18.45 | 4.45 | 5.18 | 6.89 | 40 B/frame | 54.2 |
| 1 | on | 18.26 | 3.07 | 4.43 | 5.79 | 40 B/frame | 54.8 |
| 2 | off | 18.43 | 4.53 | 5.19 | 6.51 | 40 B/frame | 54.3 |
| 2 | on | 18.25 | 3.02 | 4.44 | 5.96 | 40 B/frame | 54.8 |
| 3 | off | 18.43 | 4.56 | 5.20 | 6.74 | 40 B/frame | 54.3 |
| 3 | on | 18.21 | 3.00 | 4.37 | 5.60 | 40 B/frame | 54.9 |
| **Mean** | **off** | **18.44** | **4.51** | — | — | **40 B/frame** | **54.3** |
| **Mean** | **on** | **18.24** | **3.03** | — | — | **40 B/frame** | **54.8** |

Visible/culled counts were 8192 / 0 and 1243 / 6949, unchanged. Draw calls were
8192 and 1243.

### Variance

| Case | Render mean | Render range | Spread |
| --- | ---: | ---: | ---: |
| 512 off | 0.83 ms | 0.81–0.84 ms | 0.03 ms |
| 512 on | 0.86 ms | 0.85–0.87 ms | 0.02 ms |
| 2048 off | 2.26 ms | 2.21–2.30 ms | 0.09 ms |
| 2048 on | 1.81 ms | 1.80–1.81 ms | 0.01 ms |
| 8192 off | 4.51 ms | 4.45–4.56 ms | 0.11 ms |
| 8192 on | 3.03 ms | 3.00–3.07 ms | 0.07 ms |

The final 8192 off case varied by about 2.4% between runs. Repeated 8192 runs
during this work, including an intermediate implementation, spanned roughly 10%
(4.12–4.56 ms), so a fixed variance should not be assumed. Differences smaller
than the range for the same configuration are not treated as improvements.

## Findings

### 1. The paced loop is frame-cap limited

The final session reported about 18.2–18.45 ms total frame time and 54.2–55.0
FPS; the initial session reported about 17.4–17.6 ms and 56.8–57.5 FPS. Pacing
consumed 13.9–17.3 ms in the final runs depending on render cost; the remainder
was events, render, and scheduling. The paced total shifted between sessions, so
it is not a code-to-code comparison. This is the expected behavior of a 16 ms
delay on a 60 Hz display, not a claim that the renderer's ceiling is 55 FPS.

The useful signal is `render`. At 8192 instances it averaged 4.51 ms with
culling off and 3.03 ms with culling on, both well below the 16 ms frame budget.
The CPU submission path is therefore not the limiting factor of the paced loop
at these sizes. GPU load was not measured, so a GPU bottleneck is neither proven
nor ruled out.

### 2. Render cost scales with submitted instances

Mean render time grew from 0.83 ms at 512 to 2.26 ms at 2048 and 4.51 ms at
8192 with culling off. The growth is not a single constant per instance: fixed
per-frame work, cache effects, and the changing visible fraction all contribute.
The marginal cost is roughly 0.4–0.8 µs per additional submitted instance in
this workload. Do not generalize that figure to other meshes, materials,
pipelines, or backends.

### 3. Culling pays off at large counts and costs a little at small counts

At 8192 instances, culling rejected 6949 of 8192 instances (84.8%) and reduced
mean render time by 1.48 ms, about 33%. At 2048 it rejected 904 of 2048 (44.1%)
and reduced render time by 0.45 ms, about 20%. At 512 it rejected 67 instances
(13.1%) and render time was 0.03 ms higher with culling on; the test and bounds
work cost slightly more than the 67 skipped draws.

This is the correct conservative result: culling is an optimization for scenes
with substantial out-of-view geometry, not a universal win. The example keeps
the toggle so both cases remain measurable.

### 4. The measured per-frame allocation was avoidable

With culling on, every frame allocated 184 bytes; with culling off it allocated
40 bytes. The difference was the class-based `Frustum` plus its six-plane array,
created by `Frustum.Create` once per frame. The renderer now owns one scratch
`Frustum` and calls `Frustum.Update` to refresh its planes, so the culling path
allocates no frustum object per frame. Culling-on allocation fell to 40 bytes
per frame. `Frustum.Create` remains the public factory for independent instances.

The residual 40 bytes per frame was present with culling off before the change
and was not attributed to a specific call in this work. It did not cause a
Gen0 collection during 600 measured frames, and it is small enough that the
benchmark's allocation sampling is the limiting factor in interpreting it. It
should not be described as zero-allocation rendering.

### 5. No GPU-bound conclusion

The benchmark does not use GPU timestamps. `render` stops at command-buffer
submission, and total frame time is dominated by the pacing delay. Instruments or
Metal System Trace could distinguish GPU time from presentation blocking, but
neither was run for this baseline. The report therefore classifies the result as
frame-cap limited with CPU render cost measured, not GPU-bound or CPU-bound
overall.

### 6. Candidates not pursued

- **Normal-matrix recomputation.** The renderer computes an inverse-transpose
  normal matrix for every visible instance. An exploratory microbenchmark
  measured roughly 31–38 ns per operation, which would be about 0.25 ms at 8192
  instances if all were visible. That is within the render variance of the real
  scene and below the evidence threshold for changing the documented handling of
  singular transforms. It was not changed.
- **Binding or batching caches.** No batching, instanced draw API, or cached
  binding layer was added. Those are larger renderer changes and were not
  justified by the measured CPU cost; they would need separate approval.
- **World-bounds caching.** Bounds are recomputed per instance per frame by
  design. The scene layer has no change notification, and the measured culling
  path was cheaper than submitting the instances it removed at large counts.
  Adding invalidation machinery would be speculative.

## Implemented optimization

`Frustum` gained an allocation-free `Update(Matrix4x4)` path while keeping its
existing class, `Create`, `IsUnbounded`, and `Intersects` API. The shared material
renderer creates one `Frustum` when it is constructed and refreshes that instance
before traversing the scene. `SceneVisibilityCounter.Test` still receives a
nullable `Frustum` and has the same behavior. A failed update leaves the
reused frustum unbounded, so a degenerate matrix keeps every instance visible;
headless regression tests cover both a successful refresh and the fail-open
reset.

Before/after on the same machine and workload:

| Case | Before allocation | After allocation | Before render | After render |
| --- | ---: | ---: | ---: | ---: |
| 512 off | 40 B/frame | 40 B/frame | 0.84 ms | 0.83 ms |
| 512 on | 184 B/frame | 40 B/frame | 0.88 ms | 0.86 ms |
| 2048 off | 40 B/frame | 40 B/frame | 2.26 ms | 2.26 ms |
| 2048 on | 184 B/frame | 40 B/frame | 1.87 ms | 1.81 ms |
| 8192 off | 40 B/frame | 40 B/frame | 4.37 / 4.31 ms | 4.51 ms |
| 8192 on | 184 B/frame | 40 B/frame | 3.06 / 2.55 ms | 3.03 ms |

The render-time changes are not claimed as improvements. They are within the
observed run-to-run range and the paced total shifted between sessions, and the
after table uses three-run means while the before table has fewer runs. The
proven effect is the removal of 144 bytes of
per-frame garbage on the culling path. At 60 FPS that is about 8.4 KiB/s, or
roughly 30 MiB/hour if the loop ran continuously, but no GC collection was
observed before or after in a 600-frame run. The change is kept because it
removes an avoidable allocation and simplifies the scratch frustum's lifetime
without changing behavior.

## Verification

- `dotnet build Nymeria.slnx --configuration Release` succeeds.
- `dotnet test Nymeria.slnx --configuration Release` passes 631 tests, including
  the frame-timing tests and the frustum-reuse regression tests.
- `HelloBenchmark` completed nine final measurement runs (three processes for
  each of 512, 2048, and 8192 instances) and exited with status `0` per process.
  Its smoke run with 128 instances, 10 warm-up frames, and 60 samples also
  exited with status `0`.
- Culling counts and draw-call counts were unchanged by the optimization:
  445/67, 1144/904, and 1243/6949 for the three scene sizes.
- GPU rendering and interactive input are not covered by CI. The manual
  checklist is in [SMOKE_TEST.md](SMOKE_TEST.md); this baseline does not claim a
  full GUI regression pass beyond the benchmark windows above.

## Limitations

- Single machine and a single GPU backend. Apple M4 results do not predict Intel
  Macs, discrete GPUs, Windows, or Linux.
- No GPU timing, frame capture, or Instruments trace.
- Power source and thermal state were not controlled.
- One fixed camera and one shared cube mesh. The absolute times are specific to
  this workload.
- The 512 and 2048 before/after comparisons have fewer samples than the 8192
  case, and the 8192 comparison mixes two before runs with three after runs.
- The benchmark is manual and not a CI gate.

## Reproduce

```bash
dotnet build Nymeria.slnx --configuration Release

for size in 512 2048 8192; do
  for run in 1 2 3; do
    dotnet run --project examples/HelloBenchmark/HelloBenchmark.csproj \
      --configuration Release -- \
      --instances "$size" --warmup 120 --frames 600
  done
done
```

Run on AC power if possible, close other GPU-heavy applications, and record the
machine and display state with the results. Use [PERFORMANCE.md](PERFORMANCE.md)
for the metric definitions and interpretation guidance.
