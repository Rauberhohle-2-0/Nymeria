using Nymeria.Core.Scenes;

namespace Nymeria.Core.Animation;

/// <summary>
/// Plays one <see cref="TransformAnimationClip"/> onto one transform.
/// </summary>
/// <remarks>
/// <para>
/// The player is a small per-object controller, not a scheduler. Call
/// <see cref="Update"/> once per frame with the engine's elapsed time; sampling
/// never reads a wall clock. Several players are independent and several may
/// target the same transform; the last <see cref="Update"/> in a frame wins, so
/// conflicts are deterministic for a fixed update order.
/// </para>
/// <para>
/// A player holds a managed reference to its target transform and never touches
/// GPU, mesh, material, or scene state. Removing the target object from a scene
/// does not invalidate the player: its detached transform keeps its last values
/// and further updates only change that managed object. The application should
/// call <see cref="Stop"/> and drop the player when the object is unloaded.
/// Animation continues regardless of frustum culling, LOD, or picking state.
/// </para>
/// <para>
/// Playback advances by <c>deltaSeconds * Speed</c>. Time is clamped to the clip
/// duration for non-looping playback and wrapped once with floating-point modulo
/// for looping playback, so a large delta cannot cause per-loop iteration.
/// Negative, non-finite, or (for loops) zero-length durations are handled
/// deterministically; see <see cref="Update"/>.
/// </para>
/// <para>
/// Players are not thread-safe. Create, control, update, and dispose them on the
/// same application thread that mutates the scene.
/// </para>
/// </remarks>
public sealed class AnimationPlayer
{
    private readonly Transform _targetTransform;
    private float _speed = 1.0f;

    /// <summary>
    /// Creates a player bound to a scene object's transform.
    /// </summary>
    /// <param name="clip">The clip to sample. It is referenced, not copied.</param>
    /// <param name="target">
    /// The scene object whose transform is written. The object is referenced, not
    /// owned; it may be removed from its scene while the player still exists.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="clip"/> or <paramref name="target"/> is null.
    /// </exception>
    public AnimationPlayer(TransformAnimationClip clip, SceneObject target)
        : this(clip, target?.Transform ?? throw new ArgumentNullException(nameof(target)))
    {
        TargetObject = target;
    }

    /// <summary>
    /// Creates a player bound to a transform that is not attached to a scene object.
    /// </summary>
    /// <param name="clip">The clip to sample. It is referenced, not copied.</param>
    /// <param name="target">The transform to write. It is referenced, not copied.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="clip"/> or <paramref name="target"/> is null.
    /// </exception>
    public AnimationPlayer(TransformAnimationClip clip, Transform target)
    {
        Clip = clip ?? throw new ArgumentNullException(nameof(clip));
        _targetTransform = target ?? throw new ArgumentNullException(nameof(target));
    }

    /// <summary>Gets the clip being played.</summary>
    public TransformAnimationClip Clip { get; }

    /// <summary>Gets the transform that receives the sampled pose.</summary>
    public Transform TargetTransform => _targetTransform;

    /// <summary>
    /// Gets the bound scene object, or null when the player was bound directly to
    /// a transform.
    /// </summary>
    public SceneObject? TargetObject { get; }

    /// <summary>Gets the current clip-local time in seconds.</summary>
    public float Time { get; private set; }

    /// <summary>Gets the current lifecycle state.</summary>
    public AnimationPlaybackState State { get; private set; } =
        AnimationPlaybackState.Stopped;

    /// <summary>Gets whether the player is currently advancing time.</summary>
    public bool IsPlaying => State == AnimationPlaybackState.Playing;

    /// <summary>
    /// Gets whether a non-looping clip reached its duration. Restarting or
    /// playing again clears this state.
    /// </summary>
    public bool IsCompleted => State == AnimationPlaybackState.Completed;

    /// <summary>Gets or sets whether playback wraps to the start at the duration.</summary>
    /// <remarks>
    /// The flag can change at any time. A completed player stays completed until
    /// it is restarted, even if looping is enabled afterwards.
    /// </remarks>
    public bool IsLooping { get; set; }

    /// <summary>
    /// Gets or sets the playback rate multiplier. The default is 1.
    /// </summary>
    /// <remarks>
    /// The value must be finite and nonnegative. Zero freezes time while the
    /// player stays in <see cref="AnimationPlaybackState.Playing"/>; negative
    /// values, which would require reverse sampling and boundary rules, are
    /// rejected. Speed is applied to each update's delta, not to the stored time
    /// of an existing pose.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The value is negative or not finite.
    /// </exception>
    public float Speed
    {
        get => _speed;
        set
        {
            if (!float.IsFinite(value) || value < 0.0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    value,
                    "The playback speed must be finite and nonnegative.");
            }

            _speed = value;
        }
    }

    /// <summary>
    /// Starts playback at time zero and applies the clip's start pose.
    /// </summary>
    /// <remarks>
    /// A stopped or completed player restarts from zero. A paused player must be
    /// resumed with <see cref="Resume"/>; calling <see cref="Play"/> on one throws.
    /// Calling this on a playing player is a no-op.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The player is paused.</exception>
    public void Play()
    {
        switch (State)
        {
            case AnimationPlaybackState.Playing:
                return;
            case AnimationPlaybackState.Paused:
                throw new InvalidOperationException(
                    "The animation is paused. Call Resume instead of Play.");
            default:
                StartFromZero();
                return;
        }
    }

    /// <summary>
    /// Restarts playback at time zero from any state and applies the start pose.
    /// </summary>
    public void Restart() => StartFromZero();

    /// <summary>
    /// Pauses a playing animation and holds the current pose and time.
    /// </summary>
    /// <exception cref="InvalidOperationException">The player is not playing.</exception>
    public void Pause()
    {
        if (State != AnimationPlaybackState.Playing)
        {
            throw new InvalidOperationException(
                "Only a playing animation can be paused.");
        }

        State = AnimationPlaybackState.Paused;
    }

    /// <summary>
    /// Resumes a paused animation from its held time.
    /// </summary>
    /// <exception cref="InvalidOperationException">The player is not paused.</exception>
    public void Resume()
    {
        if (State != AnimationPlaybackState.Paused)
        {
            throw new InvalidOperationException(
                "Only a paused animation can be resumed.");
        }

        State = AnimationPlaybackState.Playing;
    }

    /// <summary>
    /// Stops playback, resets time to zero, and applies the clip's start pose.
    /// </summary>
    /// <remarks>
    /// Stopping is idempotent. The target's unanimated channels are preserved.
    /// </remarks>
    public void Stop()
    {
        Time = 0.0f;
        State = AnimationPlaybackState.Stopped;
        Clip.Apply(0.0f, _targetTransform);
    }

    /// <summary>
    /// Advances playback and applies the sampled pose.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A delta of zero is accepted and only re-applies the current pose. The
    /// delta must be finite and nonnegative; time never moves backwards.
    /// </para>
    /// <para>
    /// Looping playback wraps time into <c>[0, duration)</c> with one modulo, so
    /// a very large delta completes many loops in constant work and no loop
    /// events are raised. Non-looping playback reaches the duration exactly,
    /// applies the final pose, and enters
    /// <see cref="AnimationPlaybackState.Completed"/>. A zero-duration looping
    /// clip stays at time zero; a zero-duration non-looping clip completes.
    /// </para>
    /// <para>
    /// Updates while stopped, paused, or completed do not change the target.
    /// </para>
    /// </remarks>
    /// <param name="deltaSeconds">Elapsed seconds since the previous update.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="deltaSeconds"/> is negative or not finite.
    /// </exception>
    public void Update(float deltaSeconds)
    {
        if (!float.IsFinite(deltaSeconds) || deltaSeconds < 0.0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(deltaSeconds),
                deltaSeconds,
                "The animation delta must be finite and nonnegative.");
        }

        if (State != AnimationPlaybackState.Playing || _speed == 0.0f)
        {
            return;
        }

        float duration = Clip.DurationSeconds;

        if (duration == 0.0f)
        {
            if (IsLooping)
            {
                Time = 0.0f;
                Clip.Apply(0.0f, _targetTransform);
            }
            else
            {
                Time = 0.0f;
                Clip.Apply(0.0f, _targetTransform);
                State = AnimationPlaybackState.Completed;
            }

            return;
        }

        double advance = (double)deltaSeconds * _speed;
        double nextTime = Time + advance;

        if (IsLooping)
        {
            double wrapped = nextTime % duration;

            if (wrapped < 0.0)
            {
                wrapped += duration;
            }

            Time = (float)wrapped;
            Clip.Apply(Time, _targetTransform);
            return;
        }

        if (nextTime >= duration)
        {
            Time = duration;
            Clip.Apply(Time, _targetTransform);
            State = AnimationPlaybackState.Completed;
            return;
        }

        Time = (float)nextTime;
        Clip.Apply(Time, _targetTransform);
    }

    private void StartFromZero()
    {
        Time = 0.0f;
        State = AnimationPlaybackState.Playing;
        Clip.Apply(0.0f, _targetTransform);
    }
}
