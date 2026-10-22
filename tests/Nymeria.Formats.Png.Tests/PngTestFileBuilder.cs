using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace Nymeria.Formats.Png.Tests;

/// <summary>
/// Builds small, independent PNG files so tests do not depend on checked-in
/// image fixtures or the decoder's own encoding logic.
/// </summary>
internal static class PngTestFileBuilder
{
    public static readonly byte[] Signature = [137, 80, 78, 71, 13, 10, 26, 10];

    public static byte[] CreatePng(
        uint width,
        uint height,
        byte bitDepth = 8,
        byte colorType = 6,
        byte[]? scanlines = null,
        byte[]? palette = null,
        byte[]? transparency = null,
        bool transparencyBeforePalette = false,
        bool interlace = false,
        bool includeImageData = true,
        bool includeEnd = true,
        byte[]? compressedOverride = null)
    {
        int channels = colorType switch
        {
            0 or 3 => 1,
            2 => 3,
            4 => 2,
            6 => 4,
            _ => 1,
        };

        if (scanlines is null)
        {
            int rowBytes = checked((int)width * channels);
            scanlines = new byte[checked((int)height * (rowBytes + 1))];
        }

        byte[] compressed = compressedOverride ?? Compress(scanlines);
        using var output = new MemoryStream();
        output.Write(Signature);
        output.Write(CreateChunk("IHDR", CreateHeader(
            width,
            height,
            bitDepth,
            colorType,
            interlace)));

        if (transparencyBeforePalette && transparency is not null)
        {
            output.Write(CreateChunk("tRNS", transparency));
        }

        if (palette is not null)
        {
            output.Write(CreateChunk("PLTE", palette));
        }

        if (!transparencyBeforePalette && transparency is not null)
        {
            output.Write(CreateChunk("tRNS", transparency));
        }

        if (includeImageData)
        {
            output.Write(CreateChunk("IDAT", compressed));
        }

        if (includeEnd)
        {
            output.Write(CreateChunk("IEND", []));
        }

        return output.ToArray();
    }

    public static byte[] CreateHeader(
        uint width,
        uint height,
        byte bitDepth = 8,
        byte colorType = 6,
        bool interlace = false,
        byte compressionMethod = 0,
        byte filterMethod = 0)
    {
        var header = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(0, 4), width);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4, 4), height);
        header[8] = bitDepth;
        header[9] = colorType;
        header[10] = compressionMethod;
        header[11] = filterMethod;
        header[12] = interlace ? (byte)1 : (byte)0;
        return header;
    }

    public static byte[] CreateChunk(string type, byte[] payload)
    {
        byte[] typeBytes = Encoding.ASCII.GetBytes(type);
        var chunk = new byte[12 + payload.Length];
        BinaryPrimitives.WriteUInt32BigEndian(chunk.AsSpan(0, 4), (uint)payload.Length);
        typeBytes.CopyTo(chunk, 4);
        payload.CopyTo(chunk, 8);

        uint crc = Crc32(typeBytes, payload);
        BinaryPrimitives.WriteUInt32BigEndian(
            chunk.AsSpan(8 + payload.Length, 4),
            crc);

        return chunk;
    }

    public static byte[] Compress(byte[] data)
    {
        using var output = new MemoryStream();

        using (var zlib = new ZLibStream(
            output,
            CompressionLevel.SmallestSize,
            leaveOpen: true))
        {
            zlib.Write(data);
        }

        return output.ToArray();
    }

    public static byte[] ApplyFilter(
        byte[] pixels,
        int width,
        int height,
        int channels,
        byte filter)
    {
        int rowBytes = checked(width * channels);
        var scanlines = new byte[checked(height * (rowBytes + 1))];

        for (int y = 0; y < height; y++)
        {
            int outputRow = y * (rowBytes + 1);
            scanlines[outputRow] = filter;

            for (int x = 0; x < rowBytes; x++)
            {
                int index = y * rowBytes + x;
                int left = x >= channels ? pixels[index - channels] : 0;
                int up = y > 0 ? pixels[index - rowBytes] : 0;
                int upLeft = y > 0 && x >= channels
                    ? pixels[index - rowBytes - channels]
                    : 0;
                int predictor = filter switch
                {
                    1 => left,
                    2 => up,
                    3 => (left + up) >> 1,
                    4 => Paeth(left, up, upLeft),
                    _ => 0,
                };

                scanlines[outputRow + 1 + x] =
                    (byte)((pixels[index] - predictor) & 0xFF);
            }
        }

        return scanlines;
    }

    private static int Paeth(int left, int up, int upLeft)
    {
        int estimate = left + up - upLeft;
        int distanceLeft = Math.Abs(estimate - left);
        int distanceUp = Math.Abs(estimate - up);
        int distanceUpLeft = Math.Abs(estimate - upLeft);

        if (distanceLeft <= distanceUp && distanceLeft <= distanceUpLeft)
        {
            return left;
        }

        return distanceUp <= distanceUpLeft ? up : upLeft;
    }

    private static uint Crc32(byte[] type, byte[] payload)
    {
        uint crc = 0xFFFFFFFFu;

        foreach (byte value in type)
        {
            crc = Update(crc, value);
        }

        foreach (byte value in payload)
        {
            crc = Update(crc, value);
        }

        return ~crc;
    }

    private static uint Update(uint crc, byte value)
    {
        crc ^= value;

        for (int bit = 0; bit < 8; bit++)
        {
            crc = (crc & 1) != 0
                ? 0xEDB88320u ^ (crc >> 1)
                : crc >> 1;
        }

        return crc;
    }
}
