using System.Buffers.Binary;

namespace Nymeria.Formats.Dds.Tests;

internal static class DdsTestFileBuilder
{
    public const uint FlagCaps = 0x1;
    public const uint FlagHeight = 0x2;
    public const uint FlagWidth = 0x4;
    public const uint FlagPitch = 0x8;
    public const uint FlagPixelFormat = 0x1000;
    public const uint FlagMipMapCount = 0x20000;
    public const uint FlagLinearSize = 0x80000;

    public const uint PixelFormatAlphaPixels = 0x1;
    public const uint PixelFormatFourCc = 0x4;
    public const uint PixelFormatRgb = 0x40;

    public static byte[] Compressed(
        int width,
        int height,
        string fourCc,
        byte[] payload,
        int mipCount = 1,
        int blockBytes = 8)
    {
        uint flags = FlagCaps | FlagHeight | FlagWidth | FlagPixelFormat | FlagLinearSize;

        if (mipCount > 1)
        {
            flags |= FlagMipMapCount;
        }

        int baseLevelSize = BlockPayload(width, height, blockBytes).Length;

        return Build(
            flags,
            width,
            height,
            0,
            1,
            mipCount,
            PixelFormatFourCc,
            FourCc(fourCc),
            0,
            0,
            0,
            0,
            0,
            payload,
            linearSize: (uint)baseLevelSize);
    }

    public static byte[] Dx10(
        int width,
        int height,
        uint dxgiFormat,
        byte[] payload,
        int mipCount = 1,
        uint resourceDimension = 3,
        uint arraySize = 1,
        uint miscFlag = 0,
        uint caps2 = 0,
        uint depth = 1)
    {
        uint flags = FlagCaps | FlagHeight | FlagWidth | FlagPixelFormat | FlagLinearSize;

        if (mipCount > 1)
        {
            flags |= FlagMipMapCount;
        }

        return Build(
            flags,
            width,
            height,
            0,
            depth,
            mipCount,
            PixelFormatFourCc,
            FourCc("DX10"),
            0,
            0,
            0,
            0,
            0,
            payload,
            caps2: caps2,
            dx10: (dxgiFormat, resourceDimension, miscFlag, arraySize, 0));
    }

    public static byte[] Uncompressed(
        int width,
        int height,
        int bitCount,
        uint redMask,
        uint greenMask,
        uint blueMask,
        uint alphaMask,
        byte[] payload,
        int mipCount = 1,
        bool declaresAlpha = true,
        uint? pitchOrLinearSize = null,
        bool pitchFlag = true,
        uint caps2 = 0,
        uint depth = 1)
    {
        uint flags = FlagCaps | FlagHeight | FlagWidth | FlagPixelFormat;

        if (pitchFlag)
        {
            flags |= FlagPitch;
        }

        if (mipCount > 1)
        {
            flags |= FlagMipMapCount;
        }

        uint pixelFormatFlags = PixelFormatRgb;

        if (declaresAlpha)
        {
            pixelFormatFlags |= PixelFormatAlphaPixels;
        }

        return Build(
            flags,
            width,
            height,
            pitchOrLinearSize ?? (uint)(width * (bitCount / 8)),
            depth,
            mipCount,
            pixelFormatFlags,
            0,
            (uint)bitCount,
            redMask,
            greenMask,
            blueMask,
            alphaMask,
            payload,
            caps2: caps2);
    }

    public static byte[] Build(
        uint flags,
        int width,
        int height,
        uint pitchOrLinearSize,
        uint depth,
        int mipCount,
        uint pixelFormatFlags,
        uint fourCc,
        uint bitCount,
        uint redMask,
        uint greenMask,
        uint blueMask,
        uint alphaMask,
        byte[] payload,
        uint caps2 = 0,
        uint? linearSize = null,
        (uint Format, uint ResourceDimension, uint MiscFlag, uint ArraySize, uint MiscFlags2)?
            dx10 = null)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        writer.Write(0x20534444u);
        writer.Write(124u);
        writer.Write(linearSize is null ? flags : flags | FlagLinearSize);
        writer.Write((uint)height);
        writer.Write((uint)width);
        writer.Write(linearSize ?? pitchOrLinearSize);
        writer.Write(depth);
        writer.Write((uint)mipCount);
        writer.Write(new byte[44]);
        writer.Write(32u);
        writer.Write(pixelFormatFlags);
        writer.Write(fourCc);
        writer.Write(bitCount);
        writer.Write(redMask);
        writer.Write(greenMask);
        writer.Write(blueMask);
        writer.Write(alphaMask);
        writer.Write(0x1000u);
        writer.Write(caps2);
        writer.Write(0u);
        writer.Write(0u);
        writer.Write(0u);

        if (dx10 is { } extension)
        {
            writer.Write(extension.Format);
            writer.Write(extension.ResourceDimension);
            writer.Write(extension.MiscFlag);
            writer.Write(extension.ArraySize);
            writer.Write(extension.MiscFlags2);
        }

        writer.Write(payload);
        writer.Flush();
        return stream.ToArray();
    }

    public static byte[] BlockPayload(int width, int height, int blockBytes)
    {
        int blocksX = (width + 3) / 4;
        int blocksY = (height + 3) / 4;
        return Pattern(blocksX * blocksY * blockBytes);
    }

    public static byte[] MipChainPayload(int width, int height, int blockBytes, int mipCount)
    {
        var payload = new List<byte>();
        int levelWidth = width;
        int levelHeight = height;

        for (int level = 0; level < mipCount; level++)
        {
            payload.AddRange(BlockPayload(levelWidth, levelHeight, blockBytes));
            levelWidth = Math.Max(1, levelWidth / 2);
            levelHeight = Math.Max(1, levelHeight / 2);
        }

        return payload.ToArray();
    }

    public static byte[] RgbaPayload(int width, int height, int bytesPerPixel = 4)
    {
        return Pattern(width * height * bytesPerPixel);
    }

    public static byte[] Pattern(int length)
    {
        var bytes = new byte[length];

        for (int i = 0; i < length; i++)
        {
            bytes[i] = (byte)(i * 7 + 3);
        }

        return bytes;
    }

    public static uint FourCc(string value)
    {
        return BinaryPrimitives.ReadUInt32LittleEndian(System.Text.Encoding.ASCII.GetBytes(value));
    }

    public static byte[] ClearFlag(byte[] dds, uint flag)
    {
        byte[] copy = dds.ToArray();
        uint flags = BinaryPrimitives.ReadUInt32LittleEndian(copy.AsSpan(8, 4));
        BinaryPrimitives.WriteUInt32LittleEndian(copy.AsSpan(8, 4), flags & ~flag);
        return copy;
    }

    public static byte[] SetMipCountWithoutFlag(byte[] dds, uint mipCount)
    {
        byte[] copy = dds.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(copy.AsSpan(28, 4), mipCount);
        uint flags = BinaryPrimitives.ReadUInt32LittleEndian(copy.AsSpan(8, 4));
        BinaryPrimitives.WriteUInt32LittleEndian(
            copy.AsSpan(8, 4),
            flags & ~FlagMipMapCount);
        return copy;
    }

    public static byte[] PatchUInt32(byte[] dds, int offset, uint value)
    {
        byte[] copy = dds.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(copy.AsSpan(offset, 4), value);
        return copy;
    }
}
