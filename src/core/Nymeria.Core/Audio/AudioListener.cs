using System.Numerics;

namespace Nymeria.Core.Audio;

/// <summary>
/// The position and orientation used to place spatial audio sources.
/// </summary>
/// <remarks>
/// <para>
/// The conventions match <c>Nymeria.Graphics.Camera</c>: world space is
/// right-handed with <c>+Y</c> up, and the default forward direction is
/// <c>-Z</c>. <see cref="Right"/> is derived as
/// <c>normalize(cross(Forward, Up))</c>, so a source on that side of the
/// listener pans right.
/// </para>
/// <para>
/// Position, forward, and up must be finite. Setting <see cref="Forward"/> or
/// <see cref="Up"/> normalizes the value and rejects a zero-length vector.
/// <see cref="Right"/> returns <see cref="Vector3.Zero"/> when forward and up
/// are parallel; <see cref="AudioMath.StereoPan"/> treats that degenerate basis
/// as a centered pan rather than producing NaN.
/// </para>
/// <para>
/// Properties are synchronized internally so the audio mixing thread can read a
/// listener while the application thread updates it. Each property read is
/// atomic, but a mixer callback may observe position and orientation from
/// adjacent updates. Do not set forward parallel to up.
/// </para>
/// </remarks>
public sealed class AudioListener
{
    private readonly object _sync = new();

    private Vector3 _position;
    private Vector3 _forward = -Vector3.UnitZ;
    private Vector3 _up = Vector3.UnitY;

    /// <summary>Gets or sets the listener position in world space.</summary>
    /// <exception cref="ArgumentException">The value is not finite.</exception>
    public Vector3 Position
    {
        get
        {
            lock (_sync)
            {
                return _position;
            }
        }

        set
        {
            RequireFinite(value, nameof(value));

            lock (_sync)
            {
                _position = value;
            }
        }
    }

    /// <summary>Gets or sets the normalized viewing direction.</summary>
    /// <exception cref="ArgumentException">
    /// The value is not finite or has zero length.
    /// </exception>
    public Vector3 Forward
    {
        get
        {
            lock (_sync)
            {
                return _forward;
            }
        }

        set
        {
            Vector3 normalized = Normalize(value, nameof(value));

            lock (_sync)
            {
                _forward = normalized;
            }
        }
    }

    /// <summary>Gets or sets the normalized up direction.</summary>
    /// <exception cref="ArgumentException">
    /// The value is not finite or has zero length.
    /// </exception>
    public Vector3 Up
    {
        get
        {
            lock (_sync)
            {
                return _up;
            }
        }

        set
        {
            Vector3 normalized = Normalize(value, nameof(value));

            lock (_sync)
            {
                _up = normalized;
            }
        }
    }

    /// <summary>
    /// Gets the normalized right direction derived from forward and up, or
    /// <see cref="Vector3.Zero"/> when the basis is degenerate.
    /// </summary>
    public Vector3 Right
    {
        get
        {
            lock (_sync)
            {
                Vector3 right = Vector3.Cross(_forward, _up);

                if (!IsFinite(right) || right.LengthSquared() <= 1e-12f)
                {
                    return Vector3.Zero;
                }

                return Vector3.Normalize(right);
            }
        }
    }

    private static Vector3 Normalize(Vector3 value, string parameterName)
    {
        RequireFinite(value, parameterName);

        float lengthSquared = value.LengthSquared();

        if (!float.IsFinite(lengthSquared) || lengthSquared <= 1e-12f)
        {
            throw new ArgumentException(
                "The direction must have a nonzero finite length.",
                parameterName);
        }

        return value / MathF.Sqrt(lengthSquared);
    }

    private static void RequireFinite(Vector3 value, string parameterName)
    {
        if (!IsFinite(value))
        {
            throw new ArgumentException(
                "The value must contain only finite components.",
                parameterName);
        }
    }

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) &&
        float.IsFinite(value.Y) &&
        float.IsFinite(value.Z);
}
