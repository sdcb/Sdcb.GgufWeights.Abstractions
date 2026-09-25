# Sdcb.Weights.Abstractions

Contracts for chunked LLM weight distribution — shared by weight nupkgs (≤250MB each) and inference engines.

Deliberately thin: **identity + read, no merge logic**. netstandard2.0, AOT-safe (no reflection at runtime; manifest is a plain POCO).

## Concepts

- A model's weights split into **logical files** (`model.gguf`, `mmproj.gguf`, …), each file split into ordered **segments** (typically ~230MB, one per package/DLL).
- A segment package implements `IModelWeightSegment`: `Manifest` says where it sits (`File`, `Index`, `Count`, `Offset`, `Length`, `Sha256`), `OpenStream()` yields a zero-copy seekable payload stream (embedded resource / mmap / whatever the package chooses).
- The inference side owns stitching: collect segments per `File`/`ModelId`, order by `Index`, and read `[Offset, Offset+Length)` windows — or concatenate streams manually. Nothing here dictates how.

```csharp
public interface IModelWeightSegment
{
    ModelSegmentManifest Manifest { get; }
    Stream OpenStream();
}
```

`EmbeddedWeightSegment` is the standard generated-package shape: one DLL + one embedded-resource blob, zero-copy over the loaded PE image.
