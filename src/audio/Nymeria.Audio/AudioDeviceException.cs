namespace Nymeria.Audio;

/// <summary>
/// The expected failure when a live audio device is required but cannot be used.
/// </summary>
/// <remarks>
/// <see cref="AudioSystem.Create"/> throws this type when SDL cannot initialize
/// its audio subsystem, open the default playback device, or configure the
/// requested stream format. <see cref="AudioSystem.TryCreate"/> returns the same
/// information as a result instead. Playback and clip loading require a live
/// device; pure <c>Nymeria.Core.Audio</c> math does not.
/// </remarks>
public sealed class AudioDeviceException : InvalidOperationException
{
    /// <summary>Creates an exception with a diagnostic message.</summary>
    public AudioDeviceException(string message)
        : base(message)
    {
    }

    /// <summary>Creates an exception with a diagnostic message and inner cause.</summary>
    public AudioDeviceException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
