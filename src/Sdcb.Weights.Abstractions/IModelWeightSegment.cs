using System.IO;

namespace Sdcb.Weights;

/// <summary>
/// One packaged weight segment: identifies itself via <see cref="Manifest"/> and
/// yields its payload via <see cref="OpenStream"/>. Implementations are expected
/// to be zero-alloc readers (e.g. embedded-resource streams, memory-mapped views) —
/// this contract carries no merge/stitch logic; the consumer owns that.
/// </summary>
public interface IModelWeightSegment
{
    /// <summary>Where this segment sits in the logical file chain.</summary>
    ModelSegmentManifest Manifest { get; }

    /// <summary>
    /// Opens a read-only, seekable stream over the payload. Each call returns a
    /// fresh stream positioned at 0; callers may keep it open for zero-copy reads.
    /// </summary>
    Stream OpenStream();
}
