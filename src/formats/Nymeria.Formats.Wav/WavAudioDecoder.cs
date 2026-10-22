using System.Buffers.Binary;
using System.Text;
using Nymeria.Core.Audio;

namespace Nymeria.Formats.Wav;

/// <summary>
/// Decodes uncompressed RIFF/WAVE audio into validated PCM data.
/// </summary>
/// <remarks>
/// <para>
/// The supported subset is little-endian RIFF/WAVE with a <c>fmt </c> chunk and
/// one <c>data</c> chunk. Integer PCM (format tag 1) is accepted at 8, 16, 24,
/// and 32 bits; IEEE float (format tag 3) is accepted at 32 bits only. Mono and
/// stereo are supported at sample rates from 8000 through 192000 hertz.
/// </para>
/// <para>
/// Compressed formats, <c>WAVE_FORMAT_EXTENSIBLE</c>, more than two channels,
/// inconsistent block or byte rates, and multiple format or data chunks are
/// rejected explicitly. The decoder treats the stream as untrusted input:
/// it bounds the encoded byte count, validates the RIFF and chunk sizes before
/// using them, requires the payload to match the declared sizes exactly, uses
/// checked arithmetic, and enforces frame and decoded-payload limits before
/// allocating. It never returns partial audio and never opens a file or resolves
/// a path. The caller owns and disposes the stream, which is left open.
/// </para>
/// <para>
/// Samples are normalized to floating point in approximately <c>[-1, 1]</c>.
/// Non-finite float samples are rejected. Resampling and channel conversion are
/// the playback layer's responsibility.
/// </para>
/// </remarks>
public sealed class WavAudioDecoder
{
    /// <summary>The largest accepted encoded WAV, in bytes.</summary>
    public const int MaxInputBytes = 32 * 1024 * 1024;

    /// <summary>The largest accepted frame count.</summary>
    public const int MaxFrames = 4_000_000;

    /// <summary>The largest accepted decoded float payload, in bytes.</summary>
    public const int MaxDecodedBytes = 32 * 1024 * 1024;

    private const int RiffHeaderLength = 12;
    private const int FormatChunkMinimumLength = 16;
    private const ushort PcmFormatTag = 1;
    private const ushort FloatFormatTag = 3;
    private const ushort ExtensibleFormatTag = 0xFFFE;
    private const int BytesPerFloat = sizeof(float);

    private static ReadOnlySpan<byte> RiffSignature => "RIFF"u8;

    private static ReadOnlySpan<byte> WaveFormType => "WAVE"u8;

    private static ReadOnlySpan<byte> FormatChunkId => "fmt "u8;

    private static ReadOnlySpan<byte> DataChunkId => "data"u8;

    private readonly string? _sourceName;
    private readonly WavDecodeLimits _limits;

    /// <summary>
    /// Creates a WAV decoder.
    /// </summary>
    /// <param name="sourceName">
    /// An optional file or source name included in diagnostics. It does not open
    /// any file or affect decoding.
    /// </param>
    public WavAudioDecoder(string? sourceName = null)
        : this(sourceName, WavDecodeLimits.Default)
    {
    }

    internal WavAudioDecoder(string? sourceName, WavDecodeLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);

        _sourceName = string.IsNullOrWhiteSpace(sourceName) ? null : sourceName;
        _limits = limits;
    }

    /// <summary>
    /// Decodes one WAV clip from the supplied stream.
    /// </summary>
    /// <param name="stream">
    /// A readable stream positioned at the start of the file. The caller owns and
    /// disposes it; the decoder leaves it open.
    /// </param>
    /// <returns>Validated interleaved floating-point audio.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="stream"/> is null.</exception>
    /// <exception cref="ArgumentException">The stream is not readable.</exception>
    /// <exception cref="WavFormatException">
    /// The input is malformed, unsupported, or exceeds a documented limit.
    /// </exception>
    public PcmAudioData Decode(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (!stream.CanRead)
        {
            throw new ArgumentException("The stream must be readable.", nameof(stream));
        }

        byte[] input = ReadInput(stream);

        if (input.Length < RiffHeaderLength)
        {
            throw Error($"the RIFF header is truncated at {input.Length} bytes.");
        }

        if (!input.AsSpan(0, RiffSignature.Length).SequenceEqual(RiffSignature))
        {
            throw Error("the input does not start with a RIFF signature.");
        }

        if (!input.AsSpan(8, WaveFormType.Length).SequenceEqual(WaveFormType))
        {
            throw Error("the RIFF form type is not WAVE.");
        }

        long declaredEnd = 8L + ReadUInt32(input, 4);

        if (declaredEnd > input.Length)
        {
            throw Error(
                $"the RIFF chunk declares {declaredEnd - 8} bytes but only " +
                $"{input.Length - 8} are present.");
        }

        if (declaredEnd < input.Length)
        {
            throw Error(
                $"{input.Length - declaredEnd} unexpected bytes follow the RIFF chunk.");
        }

        if (declaredEnd < RiffHeaderLength)
        {
            throw Error("the RIFF chunk is shorter than the WAVE header.");
        }

        WavFormat? format = null;
        long dataOffset = 0;
        long dataLength = 0;
        bool hasFormat = false;
        bool hasData = false;
        long offset = RiffHeaderLength;

        while (offset < declaredEnd)
        {
            if (declaredEnd - offset < 8)
            {
                throw Error($"a chunk header at offset {offset} is truncated.");
            }

            int chunkOffset = (int)offset;
            uint chunkSize = ReadUInt32(input, chunkOffset + 4);
            long payloadOffset = offset + 8;
            long payloadEnd = payloadOffset + chunkSize;

            if (payloadEnd > declaredEnd)
            {
                throw Error(
                    $"the '{DescribeChunk(input, chunkOffset)}' chunk at offset " +
                    $"{offset} declares {chunkSize} bytes, which exceeds the RIFF chunk.");
            }

            ReadOnlySpan<byte> chunkId = input.AsSpan(chunkOffset, 4);

            if (chunkId.SequenceEqual(FormatChunkId))
            {
                if (hasFormat)
                {
                    throw Error("the file contains more than one fmt chunk.");
                }

                format = ParseFormat(input, payloadOffset, chunkSize, offset);
                hasFormat = true;
            }
            else if (chunkId.SequenceEqual(DataChunkId))
            {
                if (!hasFormat)
                {
                    throw Error("the data chunk precedes the fmt chunk.");
                }

                if (hasData)
                {
                    throw Error("the file contains more than one data chunk.");
                }

                dataOffset = payloadOffset;
                dataLength = chunkSize;
                hasData = true;
            }

            offset = payloadEnd;

            if ((chunkSize & 1) != 0)
            {
                // RIFF pads odd-sized chunk payloads to an even boundary.
                if (offset >= declaredEnd)
                {
                    throw Error(
                        $"the '{DescribeChunk(input, chunkOffset)}' chunk at offset " +
                        $"{chunkOffset} has an odd size and no padding byte.");
                }

                offset++;
            }
        }

        if (offset != declaredEnd)
        {
            throw Error($"the chunk list ends at offset {offset}, not {declaredEnd}.");
        }

        if (!hasFormat || format is null)
        {
            throw Error("the file has no fmt chunk.");
        }

        if (!hasData)
        {
            throw Error("the file has no data chunk.");
        }

        WavFormat decodedFormat = format.Value;

        if (dataLength % decodedFormat.BlockAlign != 0)
        {
            throw Error(
                $"the data chunk length {dataLength} is not a whole number of " +
                $"{decodedFormat.BlockAlign}-byte sample frames.");
        }

        long frameCount = dataLength / decodedFormat.BlockAlign;

        if (frameCount == 0)
        {
            throw Error("the data chunk contains no complete sample frames.");
        }

        if (frameCount > _limits.MaxFrames)
        {
            throw Error(
                $"the clip has {frameCount} frames, which exceeds the supported " +
                $"limit of {_limits.MaxFrames}.");
        }

        long sampleCount = frameCount * decodedFormat.Channels;

        if (sampleCount > int.MaxValue)
        {
            throw Error($"the decoded sample count {sampleCount} is too large.");
        }

        if (sampleCount * BytesPerFloat > _limits.MaxDecodedBytes)
        {
            throw Error(
                $"the decoded payload is {sampleCount * BytesPerFloat} bytes, which " +
                $"exceeds the supported limit of {_limits.MaxDecodedBytes} bytes.");
        }

        var samples = new float[(int)sampleCount];

        for (long frame = 0; frame < frameCount; frame++)
        {
            long frameOffset = dataOffset + (frame * decodedFormat.BlockAlign);

            for (int channel = 0; channel < decodedFormat.Channels; channel++)
            {
                long sampleOffset = frameOffset + ((long)channel * decodedFormat.BytesPerSample);
                float value = DecodeSample(
                    input,
                    sampleOffset,
                    decodedFormat,
                    frame,
                    channel);

                samples[(frame * decodedFormat.Channels) + channel] = value;
            }
        }

        return new PcmAudioData(
            samples,
            decodedFormat.SampleRate,
            decodedFormat.Channels);
    }

    private WavFormat ParseFormat(
        byte[] input,
        long payloadOffset,
        uint chunkSize,
        long chunkOffset)
    {
        if (chunkSize < FormatChunkMinimumLength)
        {
            throw Error(
                $"the fmt chunk at offset {chunkOffset} is {chunkSize} bytes; at " +
                $"least {FormatChunkMinimumLength} are required.");
        }

        int offset = (int)payloadOffset;
        ushort formatTag = BinaryPrimitives.ReadUInt16LittleEndian(input.AsSpan(offset, 2));
        ushort channels = BinaryPrimitives.ReadUInt16LittleEndian(input.AsSpan(offset + 2, 2));
        uint sampleRate = BinaryPrimitives.ReadUInt32LittleEndian(input.AsSpan(offset + 4, 4));
        uint byteRate = BinaryPrimitives.ReadUInt32LittleEndian(input.AsSpan(offset + 8, 4));
        ushort blockAlign = BinaryPrimitives.ReadUInt16LittleEndian(input.AsSpan(offset + 12, 2));
        ushort bitsPerSample = BinaryPrimitives.ReadUInt16LittleEndian(input.AsSpan(offset + 14, 2));

        if (formatTag == ExtensibleFormatTag)
        {
            throw Error(
                "WAVE_FORMAT_EXTENSIBLE is not supported. Write plain PCM " +
                "(format 1) or IEEE float (format 3).");
        }

        if (formatTag is not (PcmFormatTag or FloatFormatTag))
        {
            throw Error(
                $"format tag {formatTag} is not supported. Expected 1 (PCM) or " +
                "3 (IEEE float).");
        }

        if (channels is not (1 or 2))
        {
            throw Error(
                $"{channels} channels are declared; only mono and stereo are " +
                "supported.");
        }

        if (sampleRate is < PcmAudioData.MinSampleRate or > PcmAudioData.MaxSampleRate)
        {
            throw Error(
                $"the sample rate {sampleRate} is outside the supported range of " +
                $"{PcmAudioData.MinSampleRate} to {PcmAudioData.MaxSampleRate} hertz.");
        }

        bool isFloat = formatTag == FloatFormatTag;
        bool isSupportedDepth = isFloat
            ? bitsPerSample == 32
            : bitsPerSample is 8 or 16 or 24 or 32;

        if (!isSupportedDepth)
        {
            throw Error(
                $"{bitsPerSample}-bit {(isFloat ? "IEEE float" : "PCM")} samples " +
                "are not supported.");
        }

        int bytesPerSample = bitsPerSample / 8;
        int expectedBlockAlign = channels * bytesPerSample;

        if (blockAlign != expectedBlockAlign)
        {
            throw Error(
                $"the declared block alignment {blockAlign} does not match " +
                $"{expectedBlockAlign} bytes for {channels} channel(s) of " +
                $"{bitsPerSample}-bit samples.");
        }

        long expectedByteRate = (long)sampleRate * blockAlign;

        if (byteRate != expectedByteRate)
        {
            throw Error(
                $"the declared byte rate {byteRate} does not match " +
                $"{expectedByteRate} bytes per second.");
        }

        return new WavFormat(
            formatTag,
            channels,
            (int)sampleRate,
            bitsPerSample,
            bytesPerSample,
            blockAlign);
    }

    private static float DecodeSample(
        byte[] input,
        long sampleOffset,
        WavFormat format,
        long frame,
        int channel)
    {
        int offset = (int)sampleOffset;

        if (format.FormatTag == FloatFormatTag)
        {
            float value = BitConverter.Int32BitsToSingle(
                BinaryPrimitives.ReadInt32LittleEndian(input.AsSpan(offset, 4)));

            if (!float.IsFinite(value))
            {
                throw new WavFormatException(
                    $"The WAV audio is invalid or unsupported: frame {frame}, " +
                    $"channel {channel} contains a non-finite float sample.");
            }

            return value;
        }

        return format.BitsPerSample switch
        {
            8 => (input[offset] - 128) / 128.0f,
            16 => BinaryPrimitives.ReadInt16LittleEndian(input.AsSpan(offset, 2)) / 32768.0f,
            24 => ReadInt24(input, offset) / 8388608.0f,
            32 => BinaryPrimitives.ReadInt32LittleEndian(input.AsSpan(offset, 4)) / 2147483648.0f,
            _ => throw new InvalidOperationException("The validated WAV bit depth is unsupported."),
        };
    }

    private static int ReadInt24(byte[] input, int offset)
    {
        int value = input[offset] | (input[offset + 1] << 8) | (input[offset + 2] << 16);

        // Sign-extend the 24-bit two's-complement value.
        if ((value & 0x0080_0000) != 0)
        {
            value |= unchecked((int)0xFF00_0000);
        }

        return value;
    }

    private byte[] ReadInput(Stream stream)
    {
        if (stream.CanSeek)
        {
            long remaining;

            try
            {
                remaining = stream.Length - stream.Position;
            }
            catch (NotSupportedException)
            {
                remaining = -1;
            }

            if (remaining > _limits.MaxInputBytes)
            {
                throw Error(
                    $"the input is {remaining} bytes, which exceeds the supported " +
                    $"limit of {_limits.MaxInputBytes} bytes.");
            }
        }

        using var output = new MemoryStream();
        byte[] buffer = new byte[8192];
        int total = 0;

        while (true)
        {
            int read = stream.Read(buffer, 0, buffer.Length);

            if (read == 0)
            {
                break;
            }

            if (read > _limits.MaxInputBytes - total)
            {
                throw Error(
                    $"the input is larger than the supported limit of " +
                    $"{_limits.MaxInputBytes} bytes.");
            }

            output.Write(buffer, 0, read);
            total += read;
        }

        return output.ToArray();
    }

    private static uint ReadUInt32(byte[] input, int offset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(input.AsSpan(offset, 4));

    private static string DescribeChunk(byte[] input, int offset)
    {
        var description = new StringBuilder(4);

        for (int index = 0; index < 4; index++)
        {
            byte value = input[offset + index];
            description.Append(value is >= 32 and <= 126 ? (char)value : '?');
        }

        return description.ToString();
    }

    private WavFormatException Error(string message) =>
        new(
            _sourceName is null
                ? $"The WAV audio is invalid or unsupported: {message}"
                : $"The WAV audio '{_sourceName}' is invalid or unsupported: {message}");

    private readonly record struct WavFormat(
        ushort FormatTag,
        int Channels,
        int SampleRate,
        int BitsPerSample,
        int BytesPerSample,
        int BlockAlign);
}
