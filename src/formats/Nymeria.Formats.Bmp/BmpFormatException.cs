namespace Nymeria.Formats.Bmp;

/// <summary>
/// The expected failure for malformed, unsupported, or unsafe BMP input.
/// </summary>
/// <remarks>
/// The decoder throws this type rather than returning partial pixel data. It
/// derives from <see cref="FormatException"/> so callers can treat image format
/// failures like model format failures.
/// </remarks>
public sealed class BmpFormatException : FormatException
{
    /// <summary>Creates an exception with a diagnostic message.</summary>
    public BmpFormatException(string message)
        : base(message)
    {
    }

    /// <summary>Creates an exception with a diagnostic message and inner cause.</summary>
    public BmpFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
