using System;
using System.IO;
using System.Linq;

namespace Sdcb.GgufWeights;

/// <summary>
/// A single seekable, read-only <see cref="Stream"/> over an ordered run of
/// <see cref="IModelWeightSegment"/>s — the stitched view of one logical file.
/// Underlying segment streams open lazily on first read; no payload copy.
/// <see cref="Read(byte[], int, int)"/> may return fewer bytes than requested and
/// always stops at a segment boundary — loop until it returns 0. On .NET 7+ use
/// <c>ReadExactly</c>. <see cref="Read(Span{byte})"/> hands the inner stream at most
/// 4MB at a time, instead of renting an array the size of the caller's span.
/// </summary>
public sealed class SegmentStream : Stream
{
    /// <summary>Largest slice passed to the current segment stream from <see cref="Read(Span{byte})"/>.</summary>
    internal const int MaxSpanChunk = 4 << 20;
    private readonly IModelWeightSegment[] _segments;
    private readonly long[] _cumOffsets;   // _cumOffsets[i] = logical offset where segment i starts
    private readonly Stream?[] _streams;
    private readonly long _length;
    private long _position;
    private int _cursor = -1;              // index of the segment whose stream is currently open

    private SegmentStream(IModelWeightSegment[] segments, long[] cumOffsets, long length)
    {
        _segments = segments;
        _cumOffsets = cumOffsets;
        _streams = new Stream?[segments.Length];
        _length = length;
    }

    /// <summary>
    /// Join segments of one logical file in manifest order. Validates that all
    /// segments share ModelId/File, that indices cover 0..Count-1 exactly once,
    /// and that offsets are contiguous.
    /// </summary>
    public static SegmentStream Join(params IModelWeightSegment[] segments)
    {
        if (segments.Length == 0)
            throw new ArgumentException("no segments");

        IModelWeightSegment[] ordered = segments
            .OrderBy(s => s.Manifest.Index)
            .ToArray();
        ModelSegmentManifest m0 = ordered[0].Manifest;

        if (ordered.Any(s => s.Manifest.ModelId != m0.ModelId || s.Manifest.File != m0.File))
            throw new ArgumentException("segments must share ModelId and File");
        if (ordered.Select(s => s.Manifest.Index).ToArray() is int[] idx &&
            !(idx.Length == m0.Count && idx.Zip(Enumerable.Range(0, m0.Count), (a, b) => a == b).All(x => x)))
            throw new ArgumentException($"segment indices must be exactly 0..{m0.Count - 1}");

        var cum = new long[ordered.Length];
        long pos = 0;
        for (int i = 0; i < ordered.Length; i++)
        {
            ModelSegmentManifest m = ordered[i].Manifest;
            if (m.Offset != pos)
                throw new ArgumentException($"segment {i}: expected offset {pos}, got {m.Offset}");
            cum[i] = pos;
            pos += m.Length;
        }

        return new SegmentStream(ordered, cum, pos);
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

    private Stream StreamFor(int seg)
    {
        if (_cursor != seg)
        {
            _streams[seg] ??= _segments[seg].OpenStream();
            _streams[seg]!.Position = 0;
            _cursor = seg;
        }
        return _streams[seg]!;
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        if (_position >= _length) return 0;
        int seg = FindSegment(_position);
        Stream s = StreamFor(seg);
        long inner = _position - _cumOffsets[seg];
        s.Position = inner;
        int canRead = (int)Math.Min(count, _cumOffsets[seg] + _segments[seg].Manifest.Length - _position);
        int n = s.Read(buffer, offset, canRead);
        _position += n;
        return n;
    }

    /// <summary>
    /// Same short-read rule as <see cref="Read(byte[], int, int)"/>: stops at the current
    /// segment boundary. Each call into the segment stream is at most <see cref="MaxSpanChunk"/>,
    /// so a span that covers a whole tensor does not make the base <see cref="Stream"/> rent a matching array.
    /// </summary>
    public override int Read(Span<byte> buffer)
    {
        if (_position >= _length) return 0;
        int seg = FindSegment(_position);
        Stream s = StreamFor(seg);
        s.Position = _position - _cumOffsets[seg];
        int canRead = (int)Math.Min(buffer.Length, _cumOffsets[seg] + _segments[seg].Manifest.Length - _position);
        int read = 0;
        while (read < canRead)
        {
            int slice = Math.Min(MaxSpanChunk, canRead - read);
            int n = s.Read(buffer.Slice(read, slice));
            if (n == 0) break;
            read += n;
            _position += n;
        }
        return read;
    }

    private int FindSegment(long position)
    {
        // linear over segments — count is tiny (≤ ~16)
        for (int i = _segments.Length - 1; i >= 0; i--)
            if (_cumOffsets[i] <= position)
                return i;
        return 0;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        _position = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => _position + offset,
            SeekOrigin.End => _length + offset,
            _ => throw new ArgumentOutOfRangeException(nameof(origin)),
        };
        if (_position < 0 || _position > _length)
            throw new IOException($"seek out of range: {_position}");
        return _position;
    }

    public override void Flush() { }
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            foreach (Stream? s in _streams)
                s?.Dispose();
        base.Dispose(disposing);
    }
}
