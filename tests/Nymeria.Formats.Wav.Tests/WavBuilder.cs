using System.Text;

namespace Nymeria.Formats.Wav.Tests;

/// <summary>
/// Builds deterministic RIFF/WAVE byte arrays for decoder tests.
/// </summary>
internal sealed class WavBuilder
{
    public string RiffId { get; set; } = "RIFF";

    public string FormType { get; set; } = "WAVE";

    public ushort FormatTag { get; set; } = 1;

    public ushort Channels { get; set; } = 1;

    public uint SampleRate { get; set; } = 8000;

    public ushort BitsPerSample { get; set; } = 16;

    public ushort? BlockAlignOverride { get; set; }

    public uint? ByteRateOverride { get; set; }

    public byte[] Data { get; set; } = [];

    public bool IncludeFormat { get; set; } = true;

    public bool IncludeData { get; set; } = true;

    public bool FormatFirst { get; set; } = true;

    public bool DuplicateFormat { get; set; }

    public bool DuplicateData { get; set; }

    public bool AddUnknownChunk { get; set; }

    public bool AddOddUnknownChunk { get; set; }

    public bool AddTrailingByte { get; set; }

    public uint? RiffSizeOverride { get; set; }

    public uint? FormatChunkSizeOverride { get; set; }

    public uint? DataChunkSizeOverride { get; set; }

    public byte[] Build()
    {
        using var body = new MemoryStream();

        if (AddUnknownChunk)
        {
            WriteChunk(body, "LIST", "INFO"u8.ToArray());
        }

        if (AddOddUnknownChunk)
        {
            WriteChunk(body, "junk", [1, 2, 3]);
        }

        if (FormatFirst)
        {
            WriteFormat(body);
            WriteData(body);
        }
        else
        {
            WriteData(body);
            WriteFormat(body);
        }

        using var file = new MemoryStream();
        file.Write(Encoding.ASCII.GetBytes(RiffId));
        file.Write(BitConverter.GetBytes(RiffSizeOverride ?? (uint)(4 + body.Length)));
        file.Write(Encoding.ASCII.GetBytes(FormType));
        body.Position = 0;
        body.CopyTo(file);

        if (AddTrailingByte)
        {
            file.WriteByte(0xAA);
        }

        return file.ToArray();
    }

    private void WriteFormat(Stream output)
    {
        if (!IncludeFormat)
        {
            return;
        }

        int blockAlign = BlockAlignOverride ?? (Channels * BitsPerSample / 8);
        uint byteRate = ByteRateOverride ?? (SampleRate * (uint)blockAlign);

        using var payload = new MemoryStream();
        payload.Write(BitConverter.GetBytes(FormatTag));
        payload.Write(BitConverter.GetBytes(Channels));
        payload.Write(BitConverter.GetBytes(SampleRate));
        payload.Write(BitConverter.GetBytes(byteRate));
        payload.Write(BitConverter.GetBytes((ushort)blockAlign));
        payload.Write(BitConverter.GetBytes(BitsPerSample));

        if (FormatChunkSizeOverride is uint declaredSize && declaredSize > payload.Length)
        {
            payload.Write(new byte[declaredSize - payload.Length]);
        }

        WriteChunk(
            output,
            "fmt ",
            payload.ToArray(),
            FormatChunkSizeOverride);

        if (DuplicateFormat)
        {
            WriteChunk(output, "fmt ", payload.ToArray(), FormatChunkSizeOverride);
        }
    }

    private void WriteData(Stream output)
    {
        if (!IncludeData)
        {
            return;
        }

        WriteChunk(output, "data", Data, DataChunkSizeOverride);

        if (DuplicateData)
        {
            WriteChunk(output, "data", Data, DataChunkSizeOverride);
        }
    }

    private static void WriteChunk(
        Stream output,
        string id,
        byte[] payload,
        uint? declaredSize = null)
    {
        output.Write(Encoding.ASCII.GetBytes(id));
        output.Write(BitConverter.GetBytes(declaredSize ?? (uint)payload.Length));
        output.Write(payload);

        if ((payload.Length & 1) != 0)
        {
            output.WriteByte(0);
        }
    }

    public static byte[] Int16(params short[] values)
    {
        var bytes = new byte[values.Length * sizeof(short)];

        for (int index = 0; index < values.Length; index++)
        {
            BitConverter.GetBytes(values[index]).CopyTo(bytes, index * sizeof(short));
        }

        return bytes;
    }

    public static byte[] Int24(params int[] values)
    {
        var bytes = new byte[values.Length * 3];

        for (int index = 0; index < values.Length; index++)
        {
            int value = values[index];
            bytes[(index * 3) + 0] = (byte)value;
            bytes[(index * 3) + 1] = (byte)(value >> 8);
            bytes[(index * 3) + 2] = (byte)(value >> 16);
        }

        return bytes;
    }

    public static byte[] Int32(params int[] values)
    {
        var bytes = new byte[values.Length * sizeof(int)];

        for (int index = 0; index < values.Length; index++)
        {
            BitConverter.GetBytes(values[index]).CopyTo(bytes, index * sizeof(int));
        }

        return bytes;
    }

    public static byte[] Float32(params float[] values)
    {
        var bytes = new byte[values.Length * sizeof(float)];

        for (int index = 0; index < values.Length; index++)
        {
            BitConverter.GetBytes(values[index]).CopyTo(bytes, index * sizeof(float));
        }

        return bytes;
    }
}
