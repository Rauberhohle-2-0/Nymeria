namespace Nymeria.Platform.Tests;

public sealed class WindowOptionsTests
{
    [Fact]
    public void Defaults_match_the_documented_window()
    {
        var options = new WindowOptions();

        Assert.Equal("Nymeria", options.Title);
        Assert.Equal(960, options.Width);
        Assert.Equal(540, options.Height);
    }

    [Fact]
    public void Accepts_custom_values()
    {
        var options = new WindowOptions("Preview", 1280, 720);

        Assert.Equal("Preview", options.Title);
        Assert.Equal(1280, options.Width);
        Assert.Equal(720, options.Height);
    }

    [Fact]
    public void Rejects_a_null_title()
    {
        Assert.Throws<ArgumentNullException>(() => new WindowOptions(null!));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Rejects_an_empty_or_whitespace_title(string title)
    {
        Assert.Throws<ArgumentException>(() => new WindowOptions(title));
    }

    [Theory]
    [InlineData(0, 540)]
    [InlineData(-1, 540)]
    [InlineData(960, 0)]
    [InlineData(960, -1)]
    public void Rejects_non_positive_dimensions(int width, int height)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new WindowOptions(width: width, height: height));
    }
}
