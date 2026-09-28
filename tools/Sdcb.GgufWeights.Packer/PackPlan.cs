using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis.CSharp;
using NuGet.Packaging;
using NuGet.Versioning;

namespace Sdcb.GgufWeights.Packer;

/// <summary>One nupkg: part 1 is the entry package (carries <c>Weights</c>), the rest are <c>.PartN</c>.</summary>
internal sealed record PartPlan(int Index, string PackageId, string ClassName, string ResourceName, long Offset, long Length)
{
    public string Sha256 { get; set; } = "";
    public bool IsEntry => Index == 0;
}

internal sealed class PackPlan
{
    public required string InputPath { get; init; }
    public required string FileName { get; init; }
    public required string ModelId { get; init; }
    public required string EntryId { get; init; }
    public required string Namespace { get; init; }
    public required NuGetVersion Version { get; init; }
    public required long FileLength { get; init; }
    public required PartPlan[] Parts { get; init; }
    public string FileSha256 { get; set; } = "";

    public static PackPlan Create(string inputPath, PackerOptions o, bool singleInput)
    {
        FileInfo fi = new(inputPath);
        if (!fi.Exists)
            throw new FileNotFoundException("input not found", inputPath);
        if (fi.Length == 0)
            throw new InvalidDataException($"{inputPath} is empty");

        string stem = Path.GetFileNameWithoutExtension(fi.Name);
        if (o.Id is not null && !singleInput)
            throw new ArgumentException("--id only applies to a single input");
        string entryId = o.Id ?? o.IdPrefix + stem;
        string ns = o.Namespace ?? "Sdcb.GgufWeights." + Sanitize(stem);
        foreach (string seg in ns.Split('.'))
            if (!SyntaxFacts.IsValidIdentifier(seg) || SyntaxFacts.GetKeywordKind(seg) != SyntaxKind.None)
                throw new ArgumentException($"namespace '{ns}' is not a valid C# namespace");

        int count = checked((int)((fi.Length + o.MaxPartBytes - 1) / o.MaxPartBytes));
        long partSize = (fi.Length + count - 1) / count;   // balanced, not "full parts + small tail"
        var parts = new PartPlan[count];
        for (int i = 0; i < count; i++)
        {
            long offset = i * partSize;
            long length = Math.Min(partSize, fi.Length - offset);
            string id = i == 0 ? entryId : $"{entryId}.Part{i + 1}";
            if (!PackageIdValidator.IsValidPackageId(id) || id.Length > PackageIdValidator.MaxPackageIdLength)
                throw new ArgumentException($"'{id}' is not a valid NuGet package id");
            parts[i] = new PartPlan(i, id, $"Part{i + 1}", $"{fi.Name}.part{i + 1}", offset, length);
        }

        return new PackPlan
        {
            InputPath = fi.FullName,
            FileName = fi.Name,
            ModelId = o.ModelId is not null && singleInput ? o.ModelId : stem,
            EntryId = entryId,
            Namespace = ns,
            Version = NuGetVersion.Parse(o.Version),
            FileLength = fi.Length,
            Parts = parts,
        };
    }

    /// <summary>One sequential pass: per-part and whole-file SHA-256.</summary>
    public void ComputeHashes(Action<long>? progress = null)
    {
        using FileStream fs = new(InputPath, FileMode.Open, FileAccess.Read, FileShare.Read, 1, FileOptions.SequentialScan);
        using IncrementalHash whole = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buf = new byte[4 << 20];
        long done = 0;
        foreach (PartPlan p in Parts)
        {
            using IncrementalHash part = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long left = p.Length;
            while (left > 0)
            {
                int n = fs.Read(buf, 0, (int)Math.Min(buf.Length, left));
                if (n <= 0)
                    throw new EndOfStreamException($"{InputPath} shrank while hashing");
                part.AppendData(buf, 0, n);
                whole.AppendData(buf, 0, n);
                left -= n;
                done += n;
                progress?.Invoke(done);
            }
            p.Sha256 = Convert.ToHexStringLower(part.GetHashAndReset());
        }
        FileSha256 = Convert.ToHexStringLower(whole.GetHashAndReset());
    }

    private static string Sanitize(string s)
    {
        StringBuilder sb = new(s.Length + 1);
        foreach (char c in s)
            sb.Append(char.IsAsciiLetterOrDigit(c) || c == '_' ? c : '_');
        if (sb.Length == 0 || char.IsAsciiDigit(sb[0]))
            sb.Insert(0, '_');
        return sb.ToString();
    }
}
