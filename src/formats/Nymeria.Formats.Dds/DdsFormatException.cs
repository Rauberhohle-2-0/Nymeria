namespace Nymeria.Formats.Dds;

/// <summary>
/// The expected failure for malformed, unsupported, or unsafe DDS input.
/// </summary>
/// <remarks>
/// The decoder throws this type rather than returning partial texture data. It
/// derives from <see cref="FormatException"/> so callers can treat image format
/// failures like model format failures.
/// </remarks>
public sealed class DdsFormatException : FormatException
{
    /// <summary>Creates an exception with a diagnostic message.</summary>
    public DdsFormatException(string message)
        : base(message)
    {
    }

    /// <summary>Creates an exception with a diagnostic message and inner cause.</summary>
    public DdsFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
