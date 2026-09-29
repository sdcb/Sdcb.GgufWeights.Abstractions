using Sdcb.GgufWeights;
using Xunit;

namespace Sdcb.GgufWeights.Tests;

public class SegmentStreamTests
{
    [Fact]
    public void ReadSpan_StopsAtSegmentBoundary_AndCapsInnerSlices()
    {
        byte[] file = Enumerable.Range(0, 6 << 20).Select(i => (byte)i).ToArray();
        int split = 5 << 20;
        RecordingSegment a = Segment(file, 0, split, index: 0, count: 2);
        RecordingSegment b = Segment(file, split, file.Length - split, index: 1, count: 2);

        using SegmentStream stream = SegmentStream.Join(a, b);
        byte[] dest = new byte[file.Length];
        int n = stream.Read(dest);

        Assert.Equal(split, n);
        Assert.Equal(file.AsSpan(0, split).ToArray(), dest.AsSpan(0, n).ToArray());
        Assert.NotEmpty(a.SpanLengths);
        Assert.All(a.SpanLengths, len => Assert.True(len <= SegmentStream.MaxSpanChunk));
        Assert.Contains(a.SpanLengths, len => len == SegmentStream.MaxSpanChunk);
        Assert.Empty(b.SpanLengths);
    }

    [Fact]
    public void Read_StopsAtSegmentBoundary()
    {
        byte[] file = Enumerable.Range(0, 100).Select(i => (byte)i).ToArray();
        RecordingSegment a = Segment(file, 0, 40, index: 0, count: 2);
        RecordingSegment b = Segment(file, 40, 60, index: 1, count: 2);

        using SegmentStream stream = SegmentStream.Join(b, a);
        byte[] dest = new byte[file.Length];
        int n = stream.Read(dest, 0, dest.Length);

        Assert.Equal(40, n);
        Assert.Equal(file.AsSpan(0, 40).ToArray(), dest.AsSpan(0, n).ToArray());
    }

    [Fact]
    public void LoopingReads_ReconstructTheFile()
    {
        byte[] file = Enumerable.Range(0, 9 << 20).Select(i => (byte)(i * 3)).ToArray();
        int split = (4 << 20) + 100;
        RecordingSegment a = Segment(file, 0, split, index: 0, count: 2);
        RecordingSegment b = Segment(file, split, file.Length - split, index: 1, count: 2);

        using SegmentStream stream = SegmentStream.Join(a, b);
        byte[] dest = new byte[file.Length];
        int filled = 0;
        while (filled < dest.Length)
        {
            int n = stream.Read(dest.AsSpan(filled));
            Assert.NotEqual(0, n);
            filled += n;
        }

        Assert.Equal(0, stream.Read(dest));
        Assert.Equal(file, dest);
        Assert.All(a.SpanLengths.Concat(b.SpanLengths), len => Assert.True(len <= SegmentStream.MaxSpanChunk));
    }

    private static RecordingSegment Segment(byte[] file, int offset, int length, int index, int count) => new(
        file.AsSpan(offset, length).ToArray(),
        new ModelSegmentManifest
        {
            ModelId = "m",
            File = "m.gguf",
            Index = index,
            Count = count,
            Offset = offset,
            Length = length,
            Sha256 = "",
        });

    private sealed class RecordingSegment : IModelWeightSegment
    {
        private readonly byte[] _payload;

        public RecordingSegment(byte[] payload, ModelSegmentManifest manifest)
        {
            _payload = payload;
            Manifest = manifest;
        }

        public ModelSegmentManifest Manifest { get; }
        public List<int> SpanLengths { get; } = [];

        public Stream OpenStream() => new RecordingStream(_payload, SpanLengths);
    }

    private sealed class RecordingStream : MemoryStream
    {
        private readonly List<int> _spanLengths;

        public RecordingStream(byte[] payload, List<int> spanLengths) : base(payload, writable: false)
        {
            _spanLengths = spanLengths;
        }

        public override int Read(Span<byte> buffer)
        {
            _spanLengths.Add(buffer.Length);
            return base.Read(buffer);
        }
    }
}
