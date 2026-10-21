namespace Nymeria.Formats.DirectX;

/// <summary>
/// A read-only stream that replays already-consumed prefix bytes and then
/// continues reading the caller's stream.
/// </summary>
/// <remarks>
/// The parser probes the ASCII header before creating a strict UTF-8 reader, so
/// a binary or compressed <c>.x</c> file is rejected with a precise diagnostic
/// instead of a decoding error. The prefix must be replayed without seeking, so
/// non-seekable input keeps working. Disposing this wrapper never disposes the
/// caller's stream.
/// </remarks>
internal sealed class PrefixStream(byte[] prefix, int prefixLength, Stream inner) : Stream
{
    private int _offset;

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        return Read(buffer.AsSpan(offset, count));
    }

    public override int Read(Span<byte> buffer)
    {
        if (buffer.IsEmpty)
        {
            return 0;
        }

        int copied = 0;

        if (_offset < prefixLength)
        {
            copied = Math.Min(buffer.Length, prefixLength - _offset);
            prefix.AsSpan(_offset, copied).CopyTo(buffer);
            _offset += copied;

            if (copied == buffer.Length)
            {
                return copied;
            }
        }

        return copied + inner.Read(buffer[copied..]);
    }

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        throw new NotSupportedException();
    }

    public override void SetLength(long value)
    {
        throw new NotSupportedException();
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        throw new NotSupportedException();
    }

    protected override void Dispose(bool disposing)
    {
        // The caller owns the inner stream. Do not dispose it here.
        base.Dispose(disposing);
    }
}
