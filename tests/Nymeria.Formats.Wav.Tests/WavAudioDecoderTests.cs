using Nymeria.Core.Audio;
using Nymeria.Formats.Wav;

namespace Nymeria.Formats.Wav.Tests;

public sealed class WavAudioDecoderTests
{
    [Fact]
    public void Decode_Reads16BitStereoPcm()
    {
        byte[] file = new WavBuilder
        {
            Channels = 2,
            SampleRate = 48000,
            BitsPerSample = 16,
            Data = WavBuilder.Int16(short.MinValue, short.MaxValue, 16384, -16384),
        }.Build();

        PcmAudioData audio = Decode(file);

        Assert.Equal(48000, audio.SampleRate);
        Assert.Equal(2, audio.Channels);
        Assert.Equal(2, audio.FrameCount);
        Assert.Equal(-1.0f, audio.Samples.Span[0]);
        Assert.Equal(short.MaxValue / 32768.0f, audio.Samples.Span[1], 6);
        Assert.Equal(0.5f, audio.Samples.Span[2], 6);
        Assert.Equal(-0.5f, audio.Samples.Span[3], 6);
    }

    [Fact]
    public void Decode_Reads8BitUnsignedPcm()
    {
        byte[] file = new WavBuilder
        {
            BitsPerSample = 8,
            Data = [0, 128, 255],
        }.Build();

        PcmAudioData audio = Decode(file);

        Assert.Equal(-1.0f, audio.Samples.Span[0]);
        Assert.Equal(0.0f, audio.Samples.Span[1]);
        Assert.Equal(127.0f / 128.0f, audio.Samples.Span[2], 6);
    }

    [Fact]
    public void Decode_Reads24BitPcm()
    {
        byte[] file = new WavBuilder
        {
            BitsPerSample = 24,
            Data = WavBuilder.Int24(0, 0x7FFFFF, 0x800000),
        }.Build();

        PcmAudioData audio = Decode(file);

        Assert.Equal(0.0f, audio.Samples.Span[0]);
        Assert.Equal(8388607.0f / 8388608.0f, audio.Samples.Span[1], 6);
        Assert.Equal(-1.0f, audio.Samples.Span[2]);
    }

    [Fact]
    public void Decode_Reads32BitPcm()
    {
        byte[] file = new WavBuilder
        {
            BitsPerSample = 32,
            Data = WavBuilder.Int32(int.MaxValue, int.MinValue),
        }.Build();

        PcmAudioData audio = Decode(file);

        Assert.Equal(2147483647.0f / 2147483648.0f, audio.Samples.Span[0], 6);
        Assert.Equal(-1.0f, audio.Samples.Span[1]);
    }

    [Fact]
    public void Decode_Reads32BitFloatAudio()
    {
        byte[] file = new WavBuilder
        {
            FormatTag = 3,
            BitsPerSample = 32,
            Data = WavBuilder.Float32(0.25f, -0.5f, 1.5f),
        }.Build();

        PcmAudioData audio = Decode(file);

        Assert.Equal(0.25f, audio.Samples.Span[0]);
        Assert.Equal(-0.5f, audio.Samples.Span[1]);
        Assert.Equal(1.5f, audio.Samples.Span[2]);
    }

    [Fact]
    public void Decode_SkipsUnknownChunksAndTheirPadding()
    {
        byte[] file = new WavBuilder
        {
            AddUnknownChunk = true,
            AddOddUnknownChunk = true,
            Data = WavBuilder.Int16(1000),
        }.Build();

        PcmAudioData audio = Decode(file);

        Assert.Equal(1, audio.FrameCount);
    }

    [Fact]
    public void Decode_LeavesTheCallerStreamOpen()
    {
        byte[] file = new WavBuilder { Data = WavBuilder.Int16(0) }.Build();

        using var stream = new MemoryStream(file);
        PcmAudioData audio = new WavAudioDecoder().Decode(stream);

        Assert.Equal(1, audio.FrameCount);
        Assert.True(stream.CanRead);
    }

    [Fact]
    public void Decode_ReadsFromNonSeekableStreams()
    {
        byte[] file = new WavBuilder { Data = WavBuilder.Int16(0) }.Build();
        using var stream = new NonSeekableStream(file);

        PcmAudioData audio = new WavAudioDecoder().Decode(stream);

        Assert.Equal(1, audio.FrameCount);
    }

    [Fact]
    public void Decode_TruncatedHeaderFails()
    {
        byte[] file = new WavBuilder { Data = WavBuilder.Int16(0) }.Build();

        Assert.Throws<WavFormatException>(
            () => Decode(file[..11]));
    }

    [Fact]
    public void Decode_BadRiffAndFormSignaturesFail()
    {
        byte[] riff = new WavBuilder { Data = WavBuilder.Int16(0) }.Build();
        riff[0] = (byte)'X';

        byte[] form = new WavBuilder { Data = WavBuilder.Int16(0) }.Build();
        form[8] = (byte)'X';

        Assert.Throws<WavFormatException>(() => Decode(riff));
        Assert.Throws<WavFormatException>(() => Decode(form));
    }

    [Fact]
    public void Decode_TruncatedAndOverlongRiffSizesFail()
    {
        byte[] truncated = new WavBuilder
        {
            Data = WavBuilder.Int16(0),
            RiffSizeOverride = 1_000_000,
        }.Build();

        byte[] trailing = new WavBuilder
        {
            Data = WavBuilder.Int16(0),
            RiffSizeOverride = 4,
        }.Build();

        Assert.Throws<WavFormatException>(() => Decode(truncated));
        Assert.Throws<WavFormatException>(() => Decode(trailing));
    }

    [Fact]
    public void Decode_TrailingBytesFail()
    {
        byte[] file = new WavBuilder
        {
            Data = WavBuilder.Int16(0),
            AddTrailingByte = true,
        }.Build();

        Assert.Throws<WavFormatException>(() => Decode(file));
    }

    [Fact]
    public void Decode_ChunkSizeBeyondRiffFailsBeforeAllocation()
    {
        byte[] file = new WavBuilder
        {
            Data = WavBuilder.Int16(0),
            DataChunkSizeOverride = uint.MaxValue,
        }.Build();

        Assert.Throws<WavFormatException>(() => Decode(file));
    }

    [Theory]
    [InlineData((ushort)2)]
    [InlineData((ushort)6)]
    [InlineData((ushort)0xFFFE)]
    public void Decode_UnsupportedFormatTagsFail(ushort formatTag)
    {
        byte[] file = new WavBuilder
        {
            FormatTag = formatTag,
            Data = WavBuilder.Int16(0),
        }.Build();

        Assert.Throws<WavFormatException>(() => Decode(file));
    }

    [Theory]
    [InlineData((ushort)0)]
    [InlineData((ushort)3)]
    [InlineData((ushort)6)]
    public void Decode_UnsupportedChannelCountsFail(ushort channels)
    {
        byte[] file = new WavBuilder
        {
            Channels = channels,
            Data = WavBuilder.Int16(0),
        }.Build();

        Assert.Throws<WavFormatException>(() => Decode(file));
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(7999u)]
    [InlineData(192001u)]
    public void Decode_UnsupportedSampleRatesFail(uint sampleRate)
    {
        byte[] file = new WavBuilder
        {
            SampleRate = sampleRate,
            Data = WavBuilder.Int16(0),
        }.Build();

        Assert.Throws<WavFormatException>(() => Decode(file));
    }

    [Theory]
    [InlineData(12)]
    [InlineData(20)]
    [InlineData(64)]
    public void Decode_UnsupportedIntegerDepthsFail(ushort bitsPerSample)
    {
        byte[] file = new WavBuilder
        {
            BitsPerSample = bitsPerSample,
            Data = WavBuilder.Int16(0),
        }.Build();

        Assert.Throws<WavFormatException>(() => Decode(file));
    }

    [Fact]
    public void Decode_Non32BitFloatFails()
    {
        byte[] file = new WavBuilder
        {
            FormatTag = 3,
            BitsPerSample = 16,
            Data = WavBuilder.Int16(0),
        }.Build();

        Assert.Throws<WavFormatException>(() => Decode(file));
    }

    [Fact]
    public void Decode_NonFiniteFloatSampleFails()
    {
        byte[] file = new WavBuilder
        {
            FormatTag = 3,
            BitsPerSample = 32,
            Data = WavBuilder.Float32(float.NaN),
        }.Build();

        Assert.Throws<WavFormatException>(() => Decode(file));
    }

    [Fact]
    public void Decode_InconsistentBlockAlignAndByteRateFail()
    {
        byte[] blockAlign = new WavBuilder
        {
            Channels = 2,
            Data = WavBuilder.Int16(0, 0),
            BlockAlignOverride = 3,
        }.Build();

        byte[] byteRate = new WavBuilder
        {
            Data = WavBuilder.Int16(0),
            ByteRateOverride = 123,
        }.Build();

        Assert.Throws<WavFormatException>(() => Decode(blockAlign));
        Assert.Throws<WavFormatException>(() => Decode(byteRate));
    }

    [Fact]
    public void Decode_ShortFormatChunkFails()
    {
        byte[] file = new WavBuilder
        {
            Data = WavBuilder.Int16(0),
            FormatChunkSizeOverride = 15,
        }.Build();

        Assert.Throws<WavFormatException>(() => Decode(file));
    }

    [Fact]
    public void Decode_MissingFormatOrDataChunkFails()
    {
        byte[] missingFormat = new WavBuilder
        {
            IncludeFormat = false,
            Data = WavBuilder.Int16(0),
        }.Build();

        byte[] missingData = new WavBuilder
        {
            IncludeData = false,
            Data = WavBuilder.Int16(0),
        }.Build();

        Assert.Throws<WavFormatException>(() => Decode(missingFormat));
        Assert.Throws<WavFormatException>(() => Decode(missingData));
    }

    [Fact]
    public void Decode_DuplicateFormatOrDataChunksFail()
    {
        byte[] duplicateFormat = new WavBuilder
        {
            DuplicateFormat = true,
            Data = WavBuilder.Int16(0),
        }.Build();

        byte[] duplicateData = new WavBuilder
        {
            DuplicateData = true,
            Data = WavBuilder.Int16(0),
        }.Build();

        Assert.Throws<WavFormatException>(() => Decode(duplicateFormat));
        Assert.Throws<WavFormatException>(() => Decode(duplicateData));
    }

    [Fact]
    public void Decode_DataBeforeFormatFails()
    {
        byte[] file = new WavBuilder
        {
            FormatFirst = false,
            Data = WavBuilder.Int16(0),
        }.Build();

        Assert.Throws<WavFormatException>(() => Decode(file));
    }

    [Fact]
    public void Decode_EmptyDataAndPartialFramesFail()
    {
        byte[] empty = new WavBuilder { Data = [] }.Build();

        byte[] partial = new WavBuilder
        {
            Channels = 2,
            BitsPerSample = 16,
            Data = [1, 2, 3],
        }.Build();

        Assert.Throws<WavFormatException>(() => Decode(empty));
        Assert.Throws<WavFormatException>(() => Decode(partial));
    }

    [Fact]
    public void Decode_AppliesInputLimit()
    {
        byte[] file = new WavBuilder { Data = WavBuilder.Int16(0, 1, 2) }.Build();
        var decoder = new WavAudioDecoder(
            null,
            new WavDecodeLimits(maxInputBytes: 20, maxFrames: 100, maxDecodedBytes: 4096));

        Assert.Throws<WavFormatException>(() => Decode(decoder, file));
    }

    [Fact]
    public void Decode_AppliesFrameLimit()
    {
        byte[] file = new WavBuilder { Data = WavBuilder.Int16(0, 1, 2) }.Build();
        var decoder = new WavAudioDecoder(
            null,
            new WavDecodeLimits(maxInputBytes: 4096, maxFrames: 2, maxDecodedBytes: 4096));

        Assert.Throws<WavFormatException>(() => Decode(decoder, file));
    }

    [Fact]
    public void Decode_AppliesDecodedPayloadLimit()
    {
        byte[] file = new WavBuilder { Data = WavBuilder.Int16(0, 1) }.Build();
        var decoder = new WavAudioDecoder(
            null,
            new WavDecodeLimits(maxInputBytes: 4096, maxFrames: 100, maxDecodedBytes: 4));

        Assert.Throws<WavFormatException>(() => Decode(decoder, file));
    }

    [Fact]
    public void Decode_InvalidSourceNameIsIncludedInTheDiagnostic()
    {
        byte[] file = new WavBuilder { Data = WavBuilder.Int16(0) }.Build();
        file[0] = (byte)'X';

        WavFormatException exception = Assert.Throws<WavFormatException>(
            () => Decode(new WavAudioDecoder("broken.wav"), file));

        Assert.Contains("broken.wav", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Decode_RejectsUnreadableStreams()
    {
        Assert.Throws<ArgumentException>(
            () => new WavAudioDecoder().Decode(new WriteOnlyStream()));
    }

    private static PcmAudioData Decode(byte[] file) => Decode(new WavAudioDecoder(), file);

    private static PcmAudioData Decode(WavAudioDecoder decoder, byte[] file)
    {
        using var stream = new MemoryStream(file);
        return decoder.Decode(stream);
    }

    private sealed class NonSeekableStream(byte[] data) : Stream
    {
        private readonly MemoryStream _inner = new(data, writable: false);

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            _inner.Read(buffer, offset, count);

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
    }

    private sealed class WriteOnlyStream : Stream
    {
        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => 0;

        public override long Position
        {
            get => 0;
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count)
        {
        }
    }
}
