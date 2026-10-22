# Audio and basic spatial sound

This document is the authoritative contract for Nymeria's audio foundation. It
covers the public playback API, the supported WAV subset, gain and spatial
semantics, ownership and thread rules, device-failure behavior, and the known
limitations. It describes what is implemented; it is not a plan for a larger
audio engine.

The scope is deliberately basic: decoded PCM playback, independent overlapping
voices, linear distance attenuation, left/right stereo panning, and a
configurable positional stereo strength. It does not include HRTF, binaural
rendering, reverb, occlusion, Doppler, surround sound, or an audio graph.

## Projects and dependency direction

| Project | Responsibility | References |
| --- | --- | --- |
| `Nymeria.Core.Audio` (namespace in `Nymeria.Core`) | Validated PCM clip data, listener state, and pure distance/pan math. Headless. | Base class library, `System.Numerics` |
| `Nymeria.Formats.Wav` | Bounded, validated uncompressed WAV decoder returning `PcmAudioData`. Headless. | `Nymeria.Core` |
| `Nymeria.Audio` | SDL3 playback device, bounded stereo mixer, clips, voices, and lifecycle. | `Nymeria.Core`, `Nymeria.Formats.Wav`, `SDL3-CS` |
| `examples/HelloAudio` | Interactive demonstration with generated audio. | `Nymeria.Audio`, `Nymeria.Core`, `Nymeria.Graphics`, `Nymeria.Platform` |

`Nymeria.Core` gains no SDL3, GPU, window, or format dependency.
`Nymeria.Formats.Wav` follows the existing decoder pattern: it reads a
caller-owned stream, never opens a file, and never touches a device.
`Nymeria.Audio` is the only project that names an SDL audio type. Its mixer is
managed code and is tested without a device.

## Public API

```csharp
using System.Numerics;
using Nymeria.Audio;
using Nymeria.Core.Audio;

using AudioSystem audio = AudioSystem.Create();          // throws AudioDeviceException
using AudioClip clip = audio.LoadWav("assets/sound.wav"); // WavFormatException on bad input
using AudioVoice voice = audio.Play(clip, loop: true);    // non-spatial (2D)

voice.Position = new Vector3(5.0f, 0.0f, 0.0f);          // ignored by 2D voices

using AudioVoice spatial = audio.PlaySpatial(
    clip,
    new Vector3(5.0f, 1.0f, 0.0f),
    minDistance: 1.0f,
    maxDistance: 30.0f,
    loop: true);

audio.Listener.Position = camera.Position;
audio.Listener.Forward = camera.Forward;
audio.MasterGain = 0.8f;
audio.StereoEffectStrength = 0.5f;   // 0 centers, 1 is the default full pan
audio.Update(deltaSeconds);
```

### Device creation and failure

- Referencing `Nymeria.Audio` does not create a device. A device exists only
  after `AudioSystem.Create` or `AudioSystem.TryCreate` succeeds.
- `AudioSystem.Create` throws `AudioDeviceException` (an
  `InvalidOperationException`) when the process is not on the main thread, SDL
  audio initialization fails, the default playback device cannot be opened, or
  the stream does not open as the requested float32 stereo format.
- `AudioSystem.TryCreate(out AudioSystem? audio, out string? error)` returns
  `false` with a non-empty diagnostic instead of throwing for those device
  failures. It is the recommended path for applications that must keep running
  without sound.
- Clip loading, `Play`, `PlaySpatial`, `Update`, and `Dispose` require a live
  system. They throw `ObjectDisposedException` after disposal.
- Pure `Nymeria.Core.Audio` math and `PcmAudioData` need no device.

### Playback surface

| Member | Behavior |
| --- | --- |
| `AudioSystem.Listener` | The `AudioListener` used by spatial voices. |
| `AudioSystem.MasterGain` | Linear master amplitude, `0` through `1`. Default `1`. |
| `AudioSystem.StereoEffectStrength` | Positional stereo effect strength, `0` through `1`. Default `1`. See below. |
| `AudioSystem.Update(float)` | Per-frame housekeeping: reaps finished voices and observes device loss. It does not feed samples. |
| `AudioSystem.ActiveVoiceCount` | Voice slots in use, including finished voices not yet reaped. |
| `AudioSystem.IsDeviceLost` | True after SDL reports no bound device or the callback fails. |
| `AudioSystem.LoadWav(string/Stream)` | Decodes and resamples one clip. |
| `AudioSystem.Play(clip, loop)` | Starts a non-spatial voice. |
| `AudioSystem.PlaySpatial(clip, position, min, max, loop)` | Starts a spatial voice. |
| `AudioVoice.Gain` | Linear per-voice amplitude, `0` through `1`. Default `1`. |
| `AudioVoice.IsLooping` | Wraps to frame zero after the last frame. Mutable while playing. |
| `AudioVoice.Position` | World-space source position. Ignored by non-spatial voices. |
| `AudioVoice.MinDistance` / `MaxDistance` | Spatial rolloff range in world units. |
| `AudioVoice.Pause/Resume/Stop/Dispose` | Lifecycle operations described below. |
| `AudioClip.DurationSeconds`, `Channels`, `SampleRate` | Decoded clip metadata. |

Gain is amplitude, not decibels. There is no separate volume curve, fade, or
limiter.

### Voice lifecycle

| State | Meaning |
| --- | --- |
| `Playing` | Mixing; the frame position advances. |
| `Paused` | Silent; the frame position and the clip reference are held. |
| `Stopped` | Explicitly stopped. Terminal; create a new voice to play again. |
| `Ended` | A non-looping clip reached its end. Terminal; the clip reference is released by `Update`. |
| `Disposed` | The voice or its audio system was disposed. |

- `Pause` requires `Playing`; `Resume` requires `Paused`. Any other transition
  throws `InvalidOperationException`.
- `Stop` and `Dispose` are idempotent and release the clip reference exactly
  once. `Stop` is terminal.
- A finished non-looping voice is silent immediately. Its slot and clip
  reference are released by the next `Update`, or by the next `Play` when slots
  are full. `AudioVoice.State` already reports `Ended` after mixing reaches the
  end.
- Disposing the audio system stops and disposes every voice, releases every clip
  reference, destroys the device stream, and shuts the audio subsystem down.
- Dispose voices before the system, and the system before the window. The
  window's disposal shuts SDL down.

### Clip ownership and world streaming

A clip owns managed sample memory only. It has no native handle. Several voices,
including voices for different chunks, may reference the same clip.

`AudioClip.Dispose` is refused with `InvalidOperationException` while any
playing or paused voice still references the clip. Stopping or disposing those
voices releases their references. Disposing an already disposed clip is a no-op.
This makes a use-after-free of sample memory impossible and makes shared chunk
ownership explicit:

- `SceneStreamer` unload removes scene objects. It never disposes meshes,
  materials, textures, or audio; audio is not part of the scene layer.
- A chunk's release callback is the place to stop and dispose the voices that
  the chunk created, then dispose clips that no other chunk shares. If a clip is
  still referenced elsewhere, `AudioClip.Dispose` throws instead of destroying
  shared state.
- Do not couple sound audibility to frustum or distance culling. Audio is
  independent of what the camera can see.

## Supported WAV input

The decoder accepts little-endian RIFF/WAVE with exactly one `fmt ` chunk and
exactly one `data` chunk:

| Property | Supported |
| --- | --- |
| Container | `RIFF`, form type `WAVE`, little-endian |
| Format tags | `1` integer PCM; `3` IEEE float |
| PCM bit depths | 8-bit unsigned, 16-bit signed, 24-bit signed, 32-bit signed |
| Float bit depth | 32-bit only |
| Channels | 1 (mono) or 2 (stereo) |
| Sample rate | 8000 through 192000 Hz |
| Chunks | One `fmt ` and one `data`; unknown chunks are skipped with RIFF padding |

Rejected explicitly: `WAVE_FORMAT_EXTENSIBLE`, compressed formats (including
ADPCM and MP3-in-WAV), more than two channels, zero-length audio, byte or block
rates that disagree with the declared format, duplicate or missing format/data
chunks, data before format, non-finite float samples, odd payloads that do not
describe whole frames, and any declared size that does not match the actual
input. The caller owns and disposes the stream; the decoder leaves it open.

### Validation and limits

RIFF and chunk sizes, offsets, and payload lengths are checked with wider
arithmetic before allocation. A truncated chunk, a chunk beyond the RIFF
boundary, trailing bytes, or an inconsistent declared size fails with
`WavFormatException`. The decoder never returns partial audio.

| Limit | Default |
| --- | --- |
| Encoded input | 32 MiB |
| Frames per clip | 4,000,000 |
| Decoded float payload | 32 MiB (8,388,608 samples) |
| Resampled float payload | 32 MiB (8,388,608 samples) |
| Channels | 2 |
| Sample rate | 8000–192000 Hz |
| Mixing sample rate | 8000–192000 Hz; default 48000 |
| Voices per system | 1–256; default 32 |

The limits are practical viewer limits, not format requirements. A service that
accepts untrusted audio should lower them or isolate decoding in a size-limited
process.

### Decoding and resampling policy

- Integer samples are normalized by their full-scale value. 8-bit unsigned
  samples subtract 128. Float samples are used as stored, and finite values
  outside `[-1, 1]` are preserved until the final mix clip.
- `AudioSystem.LoadWav` resamples once to the system rate with linear
  interpolation. The source payload is then released. This is deterministic and
  bounded, but not a band-limited resampler. Resampling an 8000 Hz clip to a
  192000 Hz system rate can multiply the sample count by 24, so the resampled
  payload is rejected with `InvalidOperationException` when it would exceed 32
  MiB (8,388,608 samples). Load such clips on a system with a lower sample rate.
- Mono clips are copied to both mixer channels. Stereo clips keep their
  channels. There is no channel matrix, downmix, or surround mapping.

## Basic spatialization

### Listener conventions

`AudioListener` matches `Nymeria.Graphics.Camera`: right-handed world space,
`+Y` up, `-Z` forward at rest, and `Right = normalize(cross(Forward, Up))`.
`Position`, `Forward`, and `Up` must be finite; forward and up are normalized,
and a zero-length direction throws. If forward and up are parallel, `Right`
returns zero and pan is centered. Listener properties are synchronized
internally so the audio thread can read them while the application updates them.

### Distance attenuation

```text
attenuation = 1                                   when distance <= minDistance
attenuation = (maxDistance - distance)
              / (maxDistance - minDistance)       when min < distance < max
attenuation = 0                                   when distance >= maxDistance
```

The result is finite and clamped to `[0, 1]`. A coincident source or a negative
distance is treated as zero distance and plays at full gain. A NaN or infinite
distance is silent. An invalid configuration (negative or non-finite minimum, or
a maximum that is not greater than the minimum) throws.

### Stereo panning

```text
toSource = normalize(sourcePosition - listenerPosition)
pan      = clamp(dot(toSource, normalize(listenerRight)), -1, 1)
leftGain  = cos((pan + 1) * pi / 4)
rightGain = sin((pan + 1) * pi / 4)
```

`-1` is fully left, `0` is centered, and `+1` is fully right. Centered sources
use `1/sqrt(2)` on both channels (equal power). A coincident source, a
degenerate listener basis, or a non-finite input yields a centered pan. Elevation
does not produce a separate effect: a source directly above or below the
listener is centered.

The audible source gain is
`voice gain * master gain * attenuation * pan gain`. Contributions from all
voices are summed and the final stereo bus is hard-clipped to `[-1, 1]`. There is
no compressor or limiter.

### Positional stereo effect strength

`AudioSystem.StereoEffectStrength` scales how strongly a positional source is
separated left and right. It is normalized to `0` through `1` and defaults to
`1`.

```text
effectivePan = pan * stereoEffectStrength
leftGain     = cos((effectivePan + 1) * pi / 4)
rightGain    = sin((effectivePan + 1) * pi / 4)
```

- `1` is the full positional pan described above. The default preserves the
  existing behavior exactly.
- `0` makes `effectivePan` zero for every source, so a mono positional source is
  centered at `1/sqrt(2)` in both channels. It has no position-dependent
  left/right separation, while distance attenuation, voice and master gain,
  frame position, looping, and listener orientation are unchanged.
- Intermediate values move the pan toward the center. Scaling the pan keeps the
  equal-power law, so total power stays constant at every strength and there is
  no loudness jump when the setting changes.

The property can change at any time, including while voices play. The mixer
reads it once per mix block; the change does not reopen the device, restart a
voice, reset its frame position, or allocate. Values that are non-finite or
outside `0` through `1` are rejected with `ArgumentOutOfRangeException`, matching
`MasterGain` and `AudioVoice.Gain`. The pure `AudioMath.SpatialGains` helper
clamps and centers instead, like `EqualPowerGains`, so defensive runtime math
cannot produce NaN or infinity.

The setting is global to one audio system. There is no per-voice override: the
system already owns the one listener and master gain, and the strength follows
that ownership instead of adding a second configuration hierarchy.

### Positional panning versus stereo-file width

These are different operations, and Nymeria implements only the first:

- **Positional panning** derives one pan from the source position relative to
  the listener and applies it to the voice. Mono clips copy their sample to both
  mixer channels. Stereo clips keep their own left and right content, and each
  channel is multiplied by its side's positional gain. `StereoEffectStrength`
  scales only that positional gain pair.
- **Stereo-file width** would change the left/right content of a stereo clip
  itself, for example by mixing its channels toward or away from mono. Nymeria
  has no such width control, channel matrix, or spatial downmix.

Setting `StereoEffectStrength` to `0` centers a positional source's placement;
it does not merge a stereo clip's channels into mono. The clip keeps its distinct
channel content, scaled by the centered gain. An OMSI-style stereo-width control
would be a separate future feature and is not conflated with this setting.

### What is not modeled

Distance and panning are the whole spatial model. There is no HRTF or binaural
filter, interaural time delay, occlusion, obstruction, reverb, environmental
acoustics, Doppler shift, air absorption, propagation delay, surround or height
channel, or physics-driven acoustics. Voices are audible even when their source
object is outside the camera frustum.

The mixer always produces float32 stereo and has no mono output mode. If the
backend or SDL's device conversion reduces the stream to mono, positional
separation cannot be heard; Nymeria does not pretend to spatialize a mono bus.

## Mixing and threading

- The stream is always float32 stereo at the configured sample rate. SDL opens
  the default playback device and converts the stream to the device format.
- SDL's audio callback pulls data. The callback mixes in bounded blocks of at
  most 2048 frames, writes the result into a preallocated scratch buffer, and
  calls only `SDL_PutAudioStreamData`. Nymeria's mixing path performs no file
  I/O, logging, or blocking wait, and the headless mixer test asserts zero managed
  allocation per block after warm-up.
- One private lock protects voice slots, clip reference counts, the listener
  snapshot, and master gain. The callback holds it only while reading state and
  mixing samples. Control methods and `Update` take the same lock.
- Create, control, and dispose the system on the application main thread. SDL
  audio initialization and the window share macOS main-thread requirements. The
  callback runs on SDL's audio thread and must never re-enter the public API.
- `Update(float deltaSeconds)` is per-frame housekeeping. Playback timing is
  driven by the device's sample clock, so frame rate and frame stalls do not
  change playback speed. The delta is validated but does not schedule mixing.

## Device loss

The system opens the default playback device. SDL's audio stream follows the
system default device where the platform supports it. If the stream reports no
bound device, or a callback cannot queue mixed frames:

1. `AudioSystem.IsDeviceLost` becomes true.
2. The next `Update` stops all voices and releases their clip references.
3. `Play` and `PlaySpatial` throw `AudioDeviceException`.
4. The system does not reopen a device automatically. Dispose it, recreate it,
   and reload or reuse clips after the new system exists.

## Example: HelloAudio

`examples/HelloAudio` generates its own seamlessly looping test audio, so it
needs no checked-in sound asset and uses no OMSI content. It renders a ground
plane and two colored source markers. The camera is the listener; its position
and forward direction are copied into `AudioSystem.Listener` every frame.

Run it from the repository root:

```bash
dotnet run --project examples/HelloAudio/HelloAudio.csproj
```

Controls:

- Hold the right mouse button and use `WASD`, `Space`/`Left Control`, and
  `Shift` to move and turn the listener.
- `P` pauses or resumes the 2D ambient loop.
- `O` pauses or resumes spatial source A.
- `X` stops source A and starts a new voice on the same clip.
- `[` and `]` change master gain. `-` and `=` change source A gain.
- `,` and `.` change the positional stereo effect strength by 10%.
- `T` cycles the strength through 0%, 50%, and 100%.
- `S` starts or stops the sweep: source A circles the listener on a horizontal
  ring at a constant 4-unit radius, so its pan moves from left to right while
  distance, attenuation, and gains stay fixed. Leave the camera still for a
  repeatable stationary-listener demonstration.
- `R` resets the listener position and orientation.

The console prints a status line once per second with the listener transform,
device state, voice count, master gain, the current positional stereo strength,
and each voice's state, position, distance, attenuation, pan, pan gains, and
gain. Pressing `T` while the sweep runs makes the difference between 0%, 50%,
and 100% visible in both the reported gains and the stereo output. If no audio
device is available, the example prints the diagnostic and keeps rendering with
an `audio unavailable` status.

## Integration with scene transforms

There is no automatic sound component on `SceneObject`. Applications keep an
explicit association:

```csharp
// Once, at load time:
AudioClip engineClip = audio.LoadWav("assets/engine.wav");
AudioVoice engineVoice = audio.PlaySpatial(engineClip, position, 2.0f, 60.0f, loop: true);

// Each frame, after updating scene transforms:
engineVoice.Position = engineObject.Transform.Position;
audio.Listener.Position = camera.Position;
audio.Listener.Forward = camera.Forward;
audio.Update(deltaSeconds);

// On scene-object removal or chunk unload:
engineVoice.Dispose();
// Dispose the clip only when no other chunk or voice references it.
engineClip.Dispose();
```

Updating a position each frame does not create a device, re-decode a clip, or
allocate. Attaching sounds to only some objects, or to none, is a valid choice;
the engine does not infer audio from scene content.

## Tests and verified platforms

Headless tests cover:

- attenuation at zero, inside, at, and beyond the range, and NaN/infinite input;
- panning left, right, center, coincident, and with a rotated listener;
- positional stereo strength at 0, 0.5, and 1 for left, right, and center
  sources, left/right symmetry, constant power, monotonic interpolation,
  clamping and centering of invalid input, independence from distance and gain,
  runtime changes while a voice plays, and unchanged stereo clip channels;
- WAV PCM and float decoding, truncation, malformed chunks, unsupported
  formats, size overflow, inconsistent rates and alignments, duplicate or
  missing chunks, trailing bytes, and every documented limit;
- voice play, pause, resume, stop, looping, natural end, overlapping voices,
  gain, master gain, clipping, voice limits, shared clips, clip-disposal refusal,
  and repeated disposal;
- concurrent mixing and control, and zero allocation in the mixer's `Mix`
  path per block after warm-up;
- `AudioSystemOptions` validation.

Device-dependent checks are explicitly skipped in the headless suite with a
reason. They are covered manually.

The implementation was built and exercised on macOS 27.0.1 on Apple Silicon
(M4, arm64) with SDL3 3.4.18 and the CoreAudio driver:

- The full Release solution builds with zero warnings.
- All headless tests pass, including the zero-allocation mix test.
- `HelloAudio` ran for eight seconds: SDL opened the default CoreAudio device as
  48000 Hz float32 stereo, all three voices stayed `playing`, and the status
  printed finite distance, attenuation, and left/right pan values for both
  spatial sources while the ambient 2D voice remained active. A human listener
  did not assess audibility in this environment; mixer output and spatial gains
  are covered by the headless tests, and the example's controls were not driven
  by synthetic input in this run.
- Repeated `AudioSystem.Create`/`Dispose` cycles in one process, each loading a
  clip and playing, pausing, resuming, and stopping a voice, completed without an
  exception or crash; disposing a clip still referenced by a voice was refused as
  documented. That run did not measure process memory, so it is not a leak audit.
- With `SDL_AUDIODRIVER` set to an invalid driver, `HelloAudio` printed
  `Audio unavailable:` with SDL's diagnostic, opened its window, and kept
  rendering until it was killed by the observation timeout.
- The WAV decoder, mixer, and math were exercised without a device by the test
  suite.

Windows and Linux are not verified for audio. The repository currently ships
only the macOS native SDL3 package, so the example and device path are
macOS-only in practice. The engine projects compile on all three CI platforms,
and the headless tests run there.

## Known limitations

- Basic stereo panning and linear attenuation only; no HRTF, occlusion, reverb,
  Doppler, surround, or propagation. Positional stereo strength is global; there
  is no per-voice override, mono mixing mode, or stereo-file width control.
- No compressed formats, resampling filters, audio capture, microphone input,
  network streaming, mixing graph, effects, or per-voice filters.
- No streaming of clip samples from disk; a loaded clip is fully decoded and
  resampled in memory.
- No automatic device switching or reopen after loss. Recovery is explicit.
- No audio/video synchronization clock, subtitle track, or timeline.
- Gains are linear and clamped; there is no dB control, limiter, or loudness
  normalization.
- A valid WAV at the documented limits still allocates up to the decoded payload
  limit, a bounded resampled payload, and the SDL stream buffer.

## Security notes

WAV input is untrusted. Decoding validates signatures, chunk sizes and order,
single format and data chunks, padding, sample rate, channel count, bit depth,
block alignment, byte rate, frame alignment, frame count, and decoded payload
size before use, with wider arithmetic for offsets and sizes. Float samples must
be finite. The decoder does not open paths, follow metadata, execute descriptors,
or allocate from unvalidated counts. See
[SECURITY.md](SECURITY.md).

Nymeria does not record audio, request microphone access, send telemetry, or
contact a network service for playback. The only external native dependency is
the pinned SDL3 runtime already used for windowing and GPU access.
