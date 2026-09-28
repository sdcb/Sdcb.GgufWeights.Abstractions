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

## Packer

`tools/Sdcb.GgufWeights.Packer` turns a `.gguf` (or a directory of them) into ready-to-push nupkgs — Roslyn compiles each part in-process, `NuGet.Packaging` writes the packages, then the built assemblies are loaded and `Weights.Model()` is re-hashed against the source file.

```shell
dotnet run --project tools/Sdcb.GgufWeights.Packer -c Release -- D:\models --dry-run
dotnet run --project tools/Sdcb.GgufWeights.Packer -c Release -- D:\models -o nupkgs -v 1.0.0 --license-file LICENSE.txt
```

`Hy-MT2-1.8B-Q4_K_M.gguf` becomes `Sdcb.GgufWeights.Hy-MT2-1.8B-Q4_K_M` (part 1 + `Weights`) and `….Part2`…`.Part5`, each ≤240MB payload, with the entry package pinning every part at `[version]`. Consumers install only the entry package:

```csharp
using Stream gguf = Sdcb.GgufWeights.Hy_MT2_1_8B_Q4_K_M.Weights.Model();
```
