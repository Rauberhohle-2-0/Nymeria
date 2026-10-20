namespace Nymeria.Core.Models;

/// <summary>
/// Reports model input that a parser cannot turn into valid geometry.
/// </summary>
/// <remarks>
/// Parsers throw this exception for malformed or unsupported source data. It is
/// a <see cref="FormatException"/> so callers can handle any invalid model data
/// without depending on a specific format.
/// </remarks>
public sealed class ModelFormatException : FormatException
{
    /// <summary>
    /// Creates an exception for malformed or unsupported model data.
    /// </summary>
    /// <param name="message">A description that identifies the problem.</param>
    public ModelFormatException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Creates an exception for malformed or unsupported model data with an
    /// underlying cause.
    /// </summary>
    /// <param name="message">A description that identifies the problem.</param>
    /// <param name="innerException">The exception that caused the failure.</param>
    public ModelFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
