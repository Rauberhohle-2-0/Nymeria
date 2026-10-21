using System.Buffers.Binary;

namespace Nymeria.Formats.Bmp.Tests;

internal static class BmpTestFileBuilder
{
    public const uint BiRgb = 0;
    public const uint BiBitFields = 3;

    public static readonly uint[] StandardBgraMasks =
    [
        0x00FF0000,
        0x0000FF00,
        0x000000FF,
        0xFF000000,
    ];

    public static readonly uint[] StandardBgrMasks =
    [
        0x00FF0000,
        0x0000FF00,
        0x000000FF,
    ];

    /// <summary>Packs row bytes with the BMP four-byte row padding.</summary>
    public static byte[] Rows(int width, int bitCount, params byte[][] rows)
    {
        int rowBytes = (width * bitCount + 7) / 8;
        int stride = (rowBytes + 3) / 4 * 4;
        var result = new byte[stride * rows.Length];

        // Non-zero padding proves that the decoder skips it instead of treating
        // it as pixel data.
        Array.Fill(result, (byte)0xA5);

        for (int row = 0; row < rows.Length; row++)
        {
            if (rows[row].Length != rowBytes)
            {
                throw new ArgumentException(
                    "Each row must contain exactly the declared pixel bytes.",
                    nameof(rows));
            }

            rows[row].CopyTo(result, row * stride);
        }

        return result;
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

    public static byte[] Palette(params (byte R, byte G, byte B, byte Reserved)[] entries)
    {
        var bytes = new byte[entries.Length * 4];

        for (int i = 0; i < entries.Length; i++)
        {
            bytes[i * 4] = entries[i].B;
            bytes[i * 4 + 1] = entries[i].G;
            bytes[i * 4 + 2] = entries[i].R;
            bytes[i * 4 + 3] = entries[i].Reserved;
        }

        return bytes;
    }

    public static byte[] Build(
        int width,
        int height,
        int bitCount,
        byte[] pixelData,
        uint compression = BiRgb,
        uint headerSize = 40,
        byte[]? palette = null,
        uint? declaredPaletteCount = null,
        uint[]? headerMasks = null,
        uint[]? trailingMasks = null,
        int? declaredHeight = null,
        uint? declaredImageSize = null,
        uint? declaredPixelOffset = null,
        uint? declaredFileSize = null,
        int? planeCount = null,
        ushort? declaredBitCount = null)
    {
        int trailingMaskBytes = trailingMasks is null ? 0 : trailingMasks.Length * 4;
        int paletteBytes = palette?.Length ?? 0;
        int headerBytes = 14 + checked((int)headerSize) + trailingMaskBytes + paletteBytes;
        int totalLength = headerBytes + pixelData.Length;
        uint pixelOffset = declaredPixelOffset ?? (uint)headerBytes;
        var file = new byte[totalLength];

        file[0] = (byte)'B';
        file[1] = (byte)'M';
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(2), declaredFileSize ?? (uint)totalLength);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(10), pixelOffset);

        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(14), headerSize);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(18), width);
        BinaryPrimitives.WriteInt32LittleEndian(
            file.AsSpan(22),
            declaredHeight ?? height);
        BinaryPrimitives.WriteUInt16LittleEndian(
            file.AsSpan(26),
            (ushort)(planeCount ?? 1));
        BinaryPrimitives.WriteUInt16LittleEndian(
            file.AsSpan(28),
            declaredBitCount ?? (ushort)bitCount);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(30), compression);
        BinaryPrimitives.WriteUInt32LittleEndian(
            file.AsSpan(34),
            declaredImageSize ?? 0);
        BinaryPrimitives.WriteUInt32LittleEndian(
            file.AsSpan(46),
            declaredPaletteCount ?? (uint)(paletteBytes / 4));

        if (headerMasks is not null)
        {
            int maskOffset = 14 + 40;

            for (int i = 0; i < headerMasks.Length; i++)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(
                    file.AsSpan(maskOffset + i * 4),
                    headerMasks[i]);
            }
        }

        int writeOffset = 14 + checked((int)headerSize);

        if (trailingMasks is not null)
        {
            for (int i = 0; i < trailingMasks.Length; i++)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(
                    file.AsSpan(writeOffset + i * 4),
                    trailingMasks[i]);
            }

            writeOffset += trailingMaskBytes;
        }

        if (palette is not null)
        {
            palette.CopyTo(file, writeOffset);
        }

        pixelData.CopyTo(file, headerBytes);
        return file;
    }

    public static byte[] WithFileSize(byte[] file, uint fileSize)
    {
        byte[] copy = (byte[])file.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(copy.AsSpan(2), fileSize);
        return copy;
    }

    public static byte[] WithDibHeaderSize(byte[] file, uint headerSize)
    {
        byte[] copy = (byte[])file.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(copy.AsSpan(14), headerSize);
        return copy;
    }

    public static byte[] WithPixelOffset(byte[] file, uint pixelOffset)
    {
        byte[] copy = (byte[])file.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(copy.AsSpan(10), pixelOffset);
        return copy;
    }
}
