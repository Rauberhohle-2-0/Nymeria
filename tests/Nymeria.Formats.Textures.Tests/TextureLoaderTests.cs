using Nymeria.Core.Materials;
using Nymeria.Formats.Dds;
using Nymeria.Formats.Png;
using Nymeria.Formats.Tga;

namespace Nymeria.Formats.Textures.Tests;

public sealed class TextureLoaderTests
{
    private static readonly TextureLoader Loader = new();

    [Fact]
    public void Load_UsesPngSignatureInsteadOfExtension()
    {
        byte[] png = TestImageBuilder.Png(1, 2, 3, 4);

        Texture texture = Loader.Load(new MemoryStream(png), "misleading.jpg");

        var rgba = Assert.IsType<TextureData>(texture);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, rgba.Pixels.ToArray());
        Assert.Equal("misleading.jpg", rgba.Name);
    }

    [Fact]
    public void Load_UsesJpegSignatureInsteadOfExtension()
    {
        Texture texture = Loader.Load(
            new MemoryStream(TestImageBuilder.JpegFixture),
            "misleading.png");

        var rgba = Assert.IsType<TextureData>(texture);
        Assert.Equal(128, rgba.Width);
    }

    [Fact]
    public void Load_UsesDdsSignatureInsteadOfExtension()
    {
        byte[] dds = TestImageBuilder.DdsUncompressedRgba(9, 8, 7, 6);

        Texture texture = Loader.Load(new MemoryStream(dds), "misleading.png");

        var rgba = Assert.IsType<TextureData>(texture);
        Assert.Equal(new byte[] { 9, 8, 7, 6 }, rgba.Pixels.ToArray());
    }

    [Theory]
    [InlineData("texture.tga")]
    [InlineData("TEXTURE.TARGA")]
    public void Load_RecognizesTgaByExtensionAndValidatesHeader(string sourceName)
    {
        byte[] tga = TestImageBuilder.TgaUncompressedBgr(11, 12, 13);

        Texture texture = Loader.Load(new MemoryStream(tga), sourceName);

        var rgba = Assert.IsType<TextureData>(texture);
        Assert.Equal(new byte[] { 11, 12, 13, 255 }, rgba.Pixels.ToArray());
    }

    [Fact]
    public void Load_RejectsTgaWithoutTgaExtension()
    {
        byte[] tga = TestImageBuilder.TgaUncompressedBgr(1, 2, 3);

        TextureFormatException exception = Assert.Throws<TextureFormatException>(
            () => Loader.Load(new MemoryStream(tga), "texture.png"));

        Assert.Contains("TGA", exception.Message);
    }

    [Fact]
    public void Load_ReportsTheFormatDecoderErrorForMalformedTga()
    {
        byte[] broken = TestImageBuilder.TgaUncompressedBgr(1, 2, 3);
        broken[16] = 32; // 32-bit declaration without a payload

        Assert.Throws<TgaFormatException>(
            () => Loader.Load(new MemoryStream(broken), "broken.tga"));
    }

    [Fact]
    public void Load_ReportsTheFormatDecoderErrorForMalformedPng()
    {
        byte[] png = TestImageBuilder.Png(1, 2, 3, 4);
        byte[] truncated = png[..20];

        Assert.Throws<PngFormatException>(
            () => Loader.Load(new MemoryStream(truncated), "broken.png"));
    }

    [Fact]
    public void Load_ReportsTheFormatDecoderErrorForMalformedDds()
    {
        byte[] dds = TestImageBuilder.DdsUncompressedRgba(1, 2, 3, 4);
        dds[4] = 1; // header size, with the signature intact

        Assert.Throws<DdsFormatException>(
            () => Loader.Load(new MemoryStream(dds), "broken.dds"));
    }

    [Fact]
    public void Load_RejectsEmptyInput()
    {
        TextureFormatException exception = Assert.Throws<TextureFormatException>(
            () => Loader.Load(new MemoryStream(Array.Empty<byte>()), "empty.png"));

        Assert.Contains("empty", exception.Message);
    }

    [Fact]
    public void Load_RejectsUnknownContent()
    {
        Assert.Throws<TextureFormatException>(
            () => Loader.Load(new MemoryStream("not an image"u8.ToArray()), "note.txt"));
    }

    [Fact]
    public void Load_LeavesTheStreamOpen()
    {
        using var stream = new MemoryStream(TestImageBuilder.Png(1, 2, 3, 4));

        Loader.Load(stream);

        Assert.True(stream.CanRead);
        Assert.Equal(stream.Length, stream.Position);
    }

    [Fact]
    public void Load_HonorsConfiguredInputLimit()
    {
        byte[] png = TestImageBuilder.Png(1, 2, 3, 4);
        var loader = new TextureLoader(png.Length - 1);

        TextureFormatException exception = Assert.Throws<TextureFormatException>(
            () => loader.Load(new MemoryStream(png)));

        Assert.Contains("limit", exception.Message);
    }

    [Fact]
    public void Load_RejectsUnreadableStream()
    {
        Assert.Throws<ArgumentException>(
            () => Loader.Load(new UnreadableStream()));
    }

    private sealed class UnreadableStream : MemoryStream
    {
        public override bool CanRead => false;
    }
}
