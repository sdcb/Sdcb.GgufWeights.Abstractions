namespace Sdcb.GgufWeights.Packer;

/// <summary>Read-only window [offset, offset+length) over a file; lets Roslyn embed a part without a temp copy.</summary>
internal sealed class SliceStream : Stream
{
    private readonly FileStream _inner;
    private readonly long _offset;
    private readonly long _length;
    private long _position;

    public SliceStream(string path, long offset, long length)
    {
        _inner = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, FileOptions.SequentialScan);
        _offset = offset;
        _length = length;
    }

    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => false;
    public override long Length => _length;
    public override long Position
    {
        get => _position;
        set => Seek(value, SeekOrigin.Begin);
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        long left = _length - _position;
        if (left <= 0)
            return 0;
        if (buffer.Length > left)
            buffer = buffer[..(int)left];
        _inner.Position = _offset + _position;
        int n = _inner.Read(buffer);
        _position += n;
        return n;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        long p = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => _position + offset,
            SeekOrigin.End => _length + offset,
            _ => throw new ArgumentOutOfRangeException(nameof(origin)),
        };
        if (p < 0 || p > _length)
            throw new IOException($"seek out of range: {p}");
        return _position = p;
    }

    public override void Flush() { }
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _inner.Dispose();
        base.Dispose(disposing);
    }
}
