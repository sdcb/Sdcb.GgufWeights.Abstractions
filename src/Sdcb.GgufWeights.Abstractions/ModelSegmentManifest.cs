using System;

namespace Sdcb.GgufWeights;

/// <summary>
/// Identity of one weight segment within a chunked logical file.
/// One model may span multiple logical files (e.g. "model.gguf", "mmproj.gguf"),
/// each split into ordered segments; a segment belongs to exactly one logical file.
/// </summary>
public sealed class ModelSegmentManifest
{
    /// <summary>Model identity shared by all segments of the model (e.g. "paddleocr-vl-1.6").</summary>
    public string ModelId { get; set; } = "";

    /// <summary>Logical file this segment belongs to (e.g. "model.gguf").</summary>
    public string File { get; set; } = "";

    /// <summary>Zero-based index of this segment within <see cref="File"/>.</summary>
    public int Index { get; set; }

    /// <summary>Total segment count for <see cref="File"/>.</summary>
    public int Count { get; set; }

    /// <summary>Byte offset of this segment's payload within the logical file.</summary>
    public long Offset { get; set; }

    /// <summary>Payload length in bytes.</summary>
    public long Length { get; set; }

    /// <summary>SHA-256 (hex, lowercase) of this segment's payload only — not of the whole logical file. Verify out of band, not per read.</summary>
    public string Sha256 { get; set; } = "";
}
