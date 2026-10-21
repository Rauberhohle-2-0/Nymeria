using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace Nymeria.Formats.Textures.Tests;

internal static class TestImageBuilder
{
    public static byte[] Png(byte red, byte green, byte blue, byte alpha)
    {
        using var output = new MemoryStream();
        output.Write([137, 80, 78, 71, 13, 10, 26, 10]);

        var header = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(0), 1);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4), 1);
        header[8] = 8; // bit depth
        header[9] = 6; // RGBA
        WriteChunk(output, "IHDR", header);

        using var compressed = new MemoryStream();

        using (var zlib = new ZLibStream(compressed, CompressionMode.Compress, leaveOpen: true))
        {
            zlib.WriteByte(0); // filter: none
            zlib.WriteByte(red);
            zlib.WriteByte(green);
            zlib.WriteByte(blue);
            zlib.WriteByte(alpha);
        }

        WriteChunk(output, "IDAT", compressed.ToArray());
        WriteChunk(output, "IEND", Array.Empty<byte>());
        return output.ToArray();
    }

    public static byte[] DdsUncompressedRgba(byte red, byte green, byte blue, byte alpha)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        writer.Write(0x20534444u);
        writer.Write(124u);
        writer.Write(0x1u | 0x2u | 0x4u | 0x8u | 0x1000u); // CAPS|HEIGHT|WIDTH|PITCH|PF
        writer.Write(1u);
        writer.Write(1u);
        writer.Write(4u);
        writer.Write(1u);
        writer.Write(1u);
        writer.Write(new byte[44]);
        writer.Write(32u);
        writer.Write(0x1u | 0x40u); // ALPHAPIXELS|RGB
        writer.Write(0u);
        writer.Write(32u);
        writer.Write(0x000000FFu);
        writer.Write(0x0000FF00u);
        writer.Write(0x00FF0000u);
        writer.Write(0xFF000000u);
        writer.Write(0x1000u);
        writer.Write(0u);
        writer.Write(0u);
        writer.Write(0u);
        writer.Write(0u);
        writer.Write(new[] { red, green, blue, alpha });
        writer.Flush();
        return stream.ToArray();
    }

    public static byte[] TgaUncompressedBgr(byte red, byte green, byte blue)
    {
        var file = new byte[18 + 3];
        file[2] = 2; // uncompressed true-color
        file[12] = 1;
        file[14] = 1;
        file[16] = 24;
        file[18] = blue;
        file[19] = green;
        file[20] = red;
        return file;
    }

    public static byte[] BmpUncompressedBgr(byte red, byte green, byte blue)
    {
        var file = new byte[54 + 4];
        file[0] = (byte)'B';
        file[1] = (byte)'M';
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(2), (uint)file.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(10), 54);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(14), 40);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(18), 1);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(22), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(26), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(28), 24);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(34), 4);
        file[54] = blue;
        file[55] = green;
        file[56] = red;
        return file;
    }

    public static byte[] JpegFixture =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Assets", "checker.jpg"));

    private static void WriteChunk(Stream output, string type, byte[] payload)
    {
        byte[] typeBytes = Encoding.ASCII.GetBytes(type);
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(length, (uint)payload.Length);
        output.Write(length);
        output.Write(typeBytes);
        output.Write(payload);
        uint crc = Crc32(typeBytes, payload);
        Span<byte> checksum = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(checksum, crc);
        output.Write(checksum);
    }

    private static uint Crc32(byte[] first, byte[] second)
    {
        uint crc = 0xFFFFFFFF;

        foreach (byte value in first)
        {
            crc = Update(crc, value);
        }

        foreach (byte value in second)
        {
            crc = Update(crc, value);
        }

        return crc ^ 0xFFFFFFFF;
    }

    private static uint Update(uint crc, byte value)
    {
        crc ^= value;

        for (int bit = 0; bit < 8; bit++)
        {
            crc = (crc & 1) != 0 ? 0xEDB88320 ^ (crc >> 1) : crc >> 1;
        }

        return crc;
    }
}
