namespace Nymeria.Formats.Textures;

/// <summary>
/// The expected failure when texture input matches none of the supported
/// formats.
/// </summary>
/// <remarks>
/// A malformed file of a recognized format fails with that format's own
/// exception, such as <c>PngFormatException</c> or <c>DdsFormatException</c>.
/// This type is only for input whose format cannot be identified.
/// </remarks>
public sealed class TextureFormatException : FormatException
{
    /// <summary>Creates an exception with a diagnostic message.</summary>
    public TextureFormatException(string message)
        : base(message)
    {
    }

    /// <summary>Creates an exception with a diagnostic message and inner cause.</summary>
    public TextureFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
