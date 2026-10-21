namespace Nymeria.Formats.Tga.Tests;

internal static class TgaTestFileBuilder
{
    public const int UncompressedTrueColor = 2;
    public const int RleTrueColor = 10;

    public static byte[] Header(
        int imageType,
        int width,
        int height,
        int pixelDepth,
        int descriptor = 0,
        int colorMapType = 0,
        byte[]? id = null)
    {
        id ??= Array.Empty<byte>();
        var header = new byte[18 + id.Length];
        header[0] = (byte)id.Length;
        header[1] = (byte)colorMapType;
        header[2] = (byte)imageType;
        header[12] = (byte)width;
        header[13] = (byte)(width >> 8);
        header[14] = (byte)height;
        header[15] = (byte)(height >> 8);
        header[16] = (byte)pixelDepth;
        header[17] = (byte)descriptor;
        id.CopyTo(header, 18);
        return header;
    }

    public static byte[] Uncompressed(
        int width,
        int height,
        int pixelDepth,
        byte[] bgra,
        int descriptor = 0)
    {
        byte[] header = Header(UncompressedTrueColor, width, height, pixelDepth, descriptor);
        var file = new byte[header.Length + bgra.Length];
        header.CopyTo(file, 0);
        bgra.CopyTo(file, header.Length);
        return file;
    }

    public static byte[] Rle(
        int width,
        int height,
        int pixelDepth,
        byte[] packets,
        int descriptor = 0)
    {
        byte[] header = Header(RleTrueColor, width, height, pixelDepth, descriptor);
        var file = new byte[header.Length + packets.Length];
        header.CopyTo(file, 0);
        packets.CopyTo(file, header.Length);
        return file;
    }

    public static byte[] Bgr(params (byte R, byte G, byte B)[] pixels)
    {
        var bytes = new byte[pixels.Length * 3];

        for (int i = 0; i < pixels.Length; i++)
        {
            bytes[i * 3] = pixels[i].B;
            bytes[i * 3 + 1] = pixels[i].G;
            bytes[i * 3 + 2] = pixels[i].R;
        }

        return bytes;
    }

    public static byte[] Bgra(params (byte R, byte G, byte B, byte A)[] pixels)
    {
        var bytes = new byte[pixels.Length * 4];

        for (int i = 0; i < pixels.Length; i++)
        {
            bytes[i * 4] = pixels[i].B;
            bytes[i * 4 + 1] = pixels[i].G;
            bytes[i * 4 + 2] = pixels[i].R;
            bytes[i * 4 + 3] = pixels[i].A;
        }

        return bytes;
    }

    public static byte[] RleRun(int count, byte r, byte g, byte b, int pixelDepth = 32, byte a = 255)
    {
        if (count is < 1 or > 128)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        // TGA uses bit 7 of the packet header to mark a run-length packet.
        var packet = new List<byte> { (byte)(0x80 | (count - 1)) };

        if (pixelDepth == 32)
        {
            packet.AddRange(new[] { b, g, r, a });
        }
        else
        {
            packet.AddRange(new[] { b, g, r });
        }

        return packet.ToArray();
    }

    public static byte[] RleRaw(params (byte R, byte G, byte B, byte A)[] pixels)
    {
        if (pixels.Length is < 1 or > 128)
        {
            throw new ArgumentOutOfRangeException(nameof(pixels));
        }

        var packet = new List<byte> { (byte)(pixels.Length - 1) };

        foreach ((byte r, byte g, byte b, byte a) in pixels)
        {
            packet.AddRange(new[] { b, g, r, a });
        }

        return packet.ToArray();
    }

    public static byte[] Combine(params byte[][] parts)
    {
        int length = parts.Sum(part => part.Length);
        var combined = new byte[length];
        int offset = 0;

        foreach (byte[] part in parts)
        {
            part.CopyTo(combined, offset);
            offset += part.Length;
        }

        return combined;
    }
}
