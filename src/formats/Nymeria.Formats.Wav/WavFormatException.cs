namespace Nymeria.Formats.Wav;

/// <summary>
/// The expected failure for malformed, unsupported, or unsafe WAV input.
/// </summary>
/// <remarks>
/// The decoder throws this type rather than returning partial audio. It derives
/// from <see cref="FormatException"/> so callers can treat audio format failures
/// like the repository's model and image format failures.
/// </remarks>
public sealed class WavFormatException : FormatException
{
    /// <summary>Creates an exception with a diagnostic message.</summary>
    public WavFormatException(string message)
        : base(message)
    {
    }

    /// <summary>Creates an exception with a diagnostic message and inner cause.</summary>
    public WavFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
