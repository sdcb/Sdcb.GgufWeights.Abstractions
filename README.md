# Sdcb.GgufWeights.Abstractions

Contracts for chunked GGUF weight distribution — shared by weight nupkgs (≤250MB each) and inference engines.

Deliberately thin: **identity + read + segment stitching**. netstandard2.0, AOT-safe (no runtime reflection; manifest is a plain POCO).

## Concepts

- A GGUF model splits into **logical files** (`model.gguf`, `mmproj.gguf`, …), each file split into ordered **segments** (typically ~230MB, one per package/DLL).
- A segment package implements `IModelWeightSegment`: `Manifest` says where it sits (`File`, `Index`, `Count`, `Offset`, `Length`, `Sha256`), `OpenStream()` yields a zero-copy seekable payload stream (embedded resource / mmap / whatever the package chooses).
- `SegmentStream.Join` stitches a file's segments back into one seekable `Stream` — validates ModelId/File/Index/Count/Offset consistency, reads lazily, no payload copy.

```csharp
// In a generated weight package:
public static class Weights
{
    public static Stream Model() => SegmentStream.Join(Part0.Segment, Part1.Segment, Part2.Segment, Part3.Segment);
}

// In the inference engine:
using var model = PaddleVlModel.Load(new GgufFile(Weights.Model()), new GgufFile(Weights.Mmproj()));
```

`EmbeddedWeightSegment` is the standard generated-package shape: one DLL + one embedded-resource blob, zero-copy over the loaded PE image (`GetManifestResourceStream`).
