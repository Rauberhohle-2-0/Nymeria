namespace Nymeria.Formats.Jpeg;

/// <summary>
/// The expected failure for malformed, unsupported, or unsafe JPEG input.
/// </summary>
/// <remarks>
/// The decoder throws this type rather than returning partial pixel data. It
/// derives from <see cref="FormatException"/> so callers can treat image format
/// failures like model format failures.
/// </remarks>
public sealed class JpegFormatException : FormatException
{
    /// <summary>Creates an exception with a diagnostic message.</summary>
    public JpegFormatException(string message)
        : base(message)
    {
    }

    /// <summary>Creates an exception with a diagnostic message and inner cause.</summary>
    public JpegFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
