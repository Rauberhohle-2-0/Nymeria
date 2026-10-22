# Transform animation

This document is the authoritative contract for Nymeria's keyframe-based
transform animation. It covers the data model, interpolation and validation
rules, playback semantics, scene interaction, limits, and the `HelloAnimation`
example. It describes what is implemented; it is not a plan for skeletal
animation or an animation graph.

The scope is deliberately small: immutable clips of translation, rotation, and
scale tracks, one playback controller per target, and deterministic sampling
from explicit frame deltas. There is no skinning, bone hierarchy, blending,
state machine, inverse kinematics, morph target, animation import, or timeline
editor.

## Project and dependency direction

The public types live in `src/core/Nymeria.Core/Animation/` under the
`Nymeria.Core.Animation` namespace. Animation data and evaluation are headless:
they use `System.Numerics` and the base class library only. There is no SDL3,
GPU, window, format-parser, or platform dependency, and no separate project is
needed.

`Nymeria.Core.Animation` references `Nymeria.Core.Scenes` for `Transform` and
the optional `SceneObject` target binding. It does not own, mutate, or dispose
meshes, materials, textures, or GPU resources, and it does not read scene
membership.

## Public API

```csharp
using System.Numerics;
using Nymeria.Core.Animation;
using Nymeria.Core.Scenes;

var clip = new TransformAnimationClip(
    durationSeconds: 2.0f,
    translation: new Vector3Track(
    [
        new Vector3Keyframe(0.0f, new Vector3(0.0f, 0.0f, 0.0f)),
        new Vector3Keyframe(2.0f, new Vector3(4.0f, 1.0f, 0.0f)),
    ]),
    rotation: new QuaternionTrack(
    [
        new QuaternionKeyframe(0.0f, Quaternion.Identity),
        new QuaternionKeyframe(
            2.0f,
            Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2.0f)),
    ]),
    name: "moving prop");

var player = new AnimationPlayer(clip, sceneObject) { IsLooping = true };
player.Play();

// In the frame loop, after input and before rendering:
player.Update(deltaSeconds);
```

- `Vector3Keyframe(float Time, Vector3 Value)` and
  `QuaternionKeyframe(float Time, Quaternion Value)` are plain time/value pairs.
- `Vector3Track` and `QuaternionTrack` are immutable, validated, time-ordered
  keyframe lists.
- `TransformAnimationClip` combines up to one track per transform channel with a
  duration and an optional diagnostic name.
- `TransformPose` is the sampled result. A null channel means the clip does not
  animate that channel.
- `AnimationPlayer` owns playback state for one clip and one target transform.
- `AnimationPlaybackState` is `Stopped`, `Playing`, `Paused`, or `Completed`.

### Sampling without a player

`TransformAnimationClip.Sample(time)` returns a `TransformPose` without touching
a transform. `TransformAnimationClip.Apply(time, transform)` samples and writes
only the channels the clip animates:

```csharp
TransformPose pose = clip.Sample(1.0f);
if (pose.Translation is { } position)
{
    // use the sampled position
}

clip.Apply(0.5f, transform); // rotation/scale stay unchanged if not animated
```

Time must be finite and within `[0, DurationSeconds]`. Sampling itself requires
no device and no scene object.

## Track rules

A track must contain at least one keyframe and accepts at most
`MaxKeyframeCount` (100,000) entries.

- Times must be finite, nonnegative, strictly increasing, and no greater than
  `MaxTimeSeconds` (86,400 seconds, 24 hours).
- Duplicate or decreasing timestamps are rejected with `ArgumentException`.
  There is no implicit "last duplicate wins" rule.
- `Vector3` values must contain only finite components. Zero and negative scale
  are accepted, matching `Transform`; they are degenerate or mirrored, not
  silently corrected.
- Quaternions must contain only finite components and have nonzero length. The
  track normalizes each key when it is constructed, so every stored and sampled
  rotation is unit length.
- Tracks copy their input sequence once and are safe for concurrent reads
  afterwards.

Each track clamps at its own endpoints: a time at or before the first keyframe
returns the first value, and a time at or after the last keyframe returns the
last value. This is why a clip may animate a channel for only part of its
duration.

## Interpolation

Translation and scale use component-wise linear interpolation:

```text
value = lerp(a, b, (time - a.Time) / (b.Time - a.Time))
```

Rotation uses shortest-path spherical interpolation. When the dot product of
two normalized key quaternions is negative, one endpoint is negated before
blending, so the result never takes the long way around. Nearly identical
orientations (dot product above `0.9995`) use a normalized linear blend to avoid
dividing by a vanishing sine. The result is normalized again, and a degenerate
blend falls back to the previous key.

Sampling is deterministic for a given clip and time. It does not read a clock,
frame counter, or global state.

## Clips

A clip requires at least one track. The duration must be finite, nonnegative,
and no greater than `MaxDurationSeconds` (86,400 seconds). Every track's last
keyframe must fall at or before the duration.

- A clip may animate translation, rotation, scale, or any combination.
- A missing track leaves the corresponding transform property untouched when
  the clip is applied.
- Zero-duration clips are allowed only with keyframes at time zero; they sample
  a constant pose.
- A clip's name is diagnostic only. It does not select or bind a target.

## Playback

`AnimationPlayer` is created for one clip and one target. Two constructors are
available:

```csharp
var byObject = new AnimationPlayer(clip, sceneObject); // writes sceneObject.Transform
var byTransform = new AnimationPlayer(clip, transform); // detached transform
```

`TargetObject` is the bound scene object for the first form and null for the
second. `TargetTransform` is always the transform that receives samples.

| Member | Behavior |
| --- | --- |
| `Play()` | Starts at time zero from `Stopped` or `Completed` and applies the start pose. A no-op while playing; throws while paused. |
| `Restart()` | Starts at time zero from any state and applies the start pose. |
| `Pause()` | Requires `Playing`; holds time and pose. |
| `Resume()` | Requires `Paused`; continues from the held time. |
| `Stop()` | Sets time to zero, applies the start pose, and enters `Stopped`. Idempotent. |
| `Update(float deltaSeconds)` | Advances and applies the sampled pose while playing. |
| `Time` | Clip-local time in `[0, DurationSeconds]`. |
| `Speed` | Finite, nonnegative multiplier; default 1. Zero freezes time without completing. |
| `IsLooping` | Wraps time at the duration; mutable at any time. |
| `State` | `Stopped`, `Playing`, `Paused`, or `Completed`. |

### Time advancement

`Update` advances by `deltaSeconds * Speed`. The delta must be finite and
nonnegative; negative or non-finite deltas throw. Time never moves backwards,
and playback speed cannot be negative, so the evaluator needs no reverse
sampling rules.

- **Non-looping:** time is clamped to the duration. Reaching it applies the
  final pose and enters `Completed`. Further updates do nothing until `Play` or
  `Restart` resets the player.
- **Looping:** time wraps into `[0, duration)` with one floating-point modulo.
  A very large delta completes any number of loops in constant work, and no
  per-loop events are raised. A looping player never enters `Completed`.
- **Zero duration:** a looping clip stays at time zero and keeps playing; a
  non-looping clip completes immediately at time zero.
- **Zero speed:** accepted. The player stays `Playing` and the pose is unchanged
  until the speed becomes positive again.
- **Completed:** enables looping afterwards does not restart the player. Use
  `Restart` or `Play`.

Stopping resets the target to the clip's time-zero pose rather than leaving the
last sampled pose. Properties the clip does not animate are never modified.

### Multiple players and conflicts

Players are independent and can target different objects. If several players
target the same transform, each update overwrites the animated channels, so the
last `Update` in a frame wins. Conflict resolution is therefore the application's
update order; there is no blending, additive layer, weight, or mask.

Players are not thread-safe. Create, control, update, and drop them on the same
thread that mutates the scene.

### Target lifecycle

- A player holds a managed reference to its target transform, not a native
  handle. There is nothing to dispose.
- Removing a scene object from a `Scene`, unloading it through a
  `SceneStreamer`, or never adding it at all does not invalidate the player.
  Updates keep writing the detached transform, which has no rendering or
  collision effect because those systems traverse current scene membership.
- Stop and drop the player when the object is unloaded. If the transform stays
  alive in application code, continuing to animate it is harmless but wasted
  work.
- The player never touches meshes, materials, textures, colliders, LOD state, or
  GPU caches. Animating cannot destroy shared assets.

## Scene interaction

Animation writes the ordinary `SceneObject.Transform` properties. Everything
that reads the transform at query or render time sees the new values:

- `Transform.CreateModelMatrix()` composes the new scale, rotation, and
  translation, so rendering and transformed bounds follow automatically.
- Frustum and distance culling, LOD selection, and picking read the current
  transform. Animation is independent of visibility: an animated object still
  updates while culled, off-screen, or minimized.
- Collision queries read the current model matrix, so a moving collider follows
  its object. Animation does not resolve contacts or move other objects.
- `SceneStreamer` only adds and removes objects. It never stops players or
  mutates transforms, so the chunk release callback is the place to stop and
  drop players that belong to the chunk.
- Audio is independent. Spatial audio positions are application-owned and are
  not derived from animation.

There is no parent-child scene hierarchy in Nymeria, so "local" animation means
the object's own transform, which is also its world transform. Rotating around
an arbitrary pivot works by authoring the mesh with its local origin at that
pivot and rotating the object, as `HelloAnimation` does for the door hinge and
wiper base. Supporting a pivot that is not the mesh origin would require a new
scene-graph or offset feature, which is out of scope.

## Limits

| Limit | Default |
| --- | --- |
| Tracks per clip | 3 (translation, rotation, scale) |
| Keyframes per track | 100,000 |
| Clip duration | 0 through 86,400 seconds |
| Keyframe time | 0 through 86,400 seconds |
| Playback speed | Any finite value from 0 upward |

Animation data is supplied by application code, not parsed from files, so there
is no untrusted input surface in this layer. The limits still bound memory and
prevent accidental unbounded clips. A clip with three full tracks can hold
300,000 keyframes; `Vector3Keyframe` is 16 bytes and `QuaternionKeyframe` is 20
bytes on a 64-bit runtime, so a maximum-size clip is a few megabytes.

## Tests and verified behavior

Headless `Nymeria.Core.Tests` cover:

- exact keyframes, mid-segment interpolation, and clamping before and after a
  track;
- one-keyframe and zero-duration clips;
- quaternion shortest-path sign handling, normalization, and near-identical
  orientations;
- invalid times, values, quaternions, orderings, clip durations, and track ends;
- keyframe-count limits;
- play, pause, resume, stop, restart, and completed states;
- looping wraparound with large deltas, and non-loop completion;
- equal results from one large delta and several small deltas;
- zero speed, invalid delta and speed values;
- multiple players on one transform and deterministic last-writer behavior;
- scene-object binding, removal from the scene, and model-matrix updates.

The implementation was built and exercised on macOS 27.0.1 on Apple Silicon
(M4, arm64):

- The Release solution builds with zero warnings.
- All headless tests pass.
- `HelloAnimation` ran for five seconds with the Metal renderer and printed live
  door angles (0 to about 109 degrees), wiper angles (about -37 to +32 degrees),
  mover scale (0.70 to 1.25), looping state, and roughly 57 frames per second.
  The animation was observed through the console diagnostics and rendering
  statistics; no automated visual assertion or human walkthrough of the window
  was performed in this run.

Windows and Linux build and run the headless tests in CI. The example itself is
macOS-only because the repository ships only the macOS native SDL3 package and
Metal shader path.

## Example: HelloAnimation

`examples/HelloAnimation` builds a flat scene with eight objects and three
independent players:

- a door whose mesh local origin is the hinge, rotated by a four-key clip that
  opens, holds, and closes;
- a wiper whose mesh local origin is its base, rotated by a three-key clip that
  sweeps about the Z axis;
- a hovering cube whose translation and scale tracks move and resize it.

The door frame posts and the two yellow pivot markers are static. They show that
the pivot is the object's own origin, not a separate pivot object.

Run it from the repository root:

```bash
dotnet run --project examples/HelloAnimation/HelloAnimation.csproj
```

Controls:

- `P` pauses or resumes all animations.
- `R` restarts every animation at time zero.
- `L` toggles looping on all players.
- `-` and `=` change the shared playback speed multiplier from x0.25 to x3.00.
- Shared camera controls: `W`/`A`/`S`/`D`, `Space`/`Left Control`, `Shift`, and
  the held right mouse button.

The console prints one summary per second with each player's state and time, the
door and wiper angles in degrees, the mover's X scale, speed, looping, pause
state, frame rate, and visible instances. The window title mirrors the three
angles and the pause state.

## Known limitations

- Transform animation only. No skeletal/skinned meshes, bones, inverse
  kinematics, morph targets, or animation import from `.x` or any other format.
- No blending, additive layers, weights, masks, state machines, or animation
  graphs. Multiple players on one transform are last-writer.
- No reverse playback, event markers, loop counters, or callbacks.
- No sub-frame interpolation for rendering; sampling happens once per update.
- No animation serialization, editor, or asset pipeline.
- No parent-child hierarchy or arbitrary pivot offset; pivots come from the
  authored mesh local origin.
- No thread safety. Keep playback on the scene/update thread.
- No automatic stop when a scene object leaves a scene; the application owns
  that decision.
