namespace Nymeria.Formats.Png;

/// <summary>
/// CRC-32 as specified by PNG for chunk names and payloads.
/// </summary>
internal static class PngCrc32
{
    private static readonly uint[] s_table = CreateTable();

    public static uint Compute(ReadOnlySpan<byte> chunkType, ReadOnlySpan<byte> data)
    {
        uint crc = 0xFFFFFFFFu;

        foreach (byte value in chunkType)
        {
            crc = s_table[(crc ^ value) & 0xFF] ^ (crc >> 8);
        }

        foreach (byte value in data)
        {
            crc = s_table[(crc ^ value) & 0xFF] ^ (crc >> 8);
        }

        return ~crc;
    }

    private static uint[] CreateTable()
    {
        var table = new uint[256];

        for (uint n = 0; n < table.Length; n++)
        {
            uint value = n;

            for (int bit = 0; bit < 8; bit++)
            {
                value = (value & 1) != 0
                    ? 0xEDB88320u ^ (value >> 1)
                    : value >> 1;
            }

            table[n] = value;
        }

        return table;
    }
}
