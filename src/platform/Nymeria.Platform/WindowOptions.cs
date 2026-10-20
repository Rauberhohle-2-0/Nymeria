namespace Nymeria.Platform;

/// <summary>
/// Describes the initial title and client size of a native window.
/// </summary>
public sealed class WindowOptions
{
    public const string DefaultTitle = "Nymeria";
    public const int DefaultWidth = 960;
    public const int DefaultHeight = 540;

    /// <summary>
    /// Creates window options with a non-empty title and positive dimensions.
    /// </summary>
    /// <exception cref="ArgumentException">The title is null, empty, or whitespace.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The width or height is zero or negative.</exception>
    public WindowOptions(
        string title = DefaultTitle,
        int width = DefaultWidth,
        int height = DefaultHeight)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        Title = title;
        Width = width;
        Height = height;
    }

    public string Title { get; }

    public int Width { get; }

    public int Height { get; }
}
