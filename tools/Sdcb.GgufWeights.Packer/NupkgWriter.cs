using NuGet.Frameworks;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Packaging.Licenses;
using NuGet.Versioning;

namespace Sdcb.GgufWeights.Packer;

/// <summary>
/// Writes a nupkg via <see cref="PackageBuilder"/> (no dotnet pack), so the entry
/// package can pin every part to the exact same version: <c>[x.y.z]</c>.
/// </summary>
internal static class NupkgWriter
{
    private const string AbstractionsId = "Sdcb.GgufWeights.Abstractions";

    public static string Write(PackPlan plan, PartPlan part, string dll, string workDir, PackerOptions o, NuGetVersion abstractionsVersion)
    {
        PackageBuilder b = new(deterministic: true)
        {
            Id = part.PackageId,
            Version = plan.Version,
            Description = Description(plan, part, o),
            Readme = "README.md",
        };
        b.Authors.Add(o.Authors);
        foreach (string tag in o.Tags.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            b.Tags.Add(tag);
        if (o.ProjectUrl is not null)
            b.ProjectUrl = new Uri(o.ProjectUrl);

        if (o.License is not null)
        {
            b.LicenseMetadata = new LicenseMetadata(LicenseType.Expression, o.License,
                NuGetLicenseExpression.Parse(o.License), warningsAndErrors: null, LicenseMetadata.EmptyVersion);
        }
        else if (o.LicenseFile is not null)
        {
            string name = Path.GetFileName(o.LicenseFile);
            b.LicenseMetadata = new LicenseMetadata(LicenseType.File, name, null, null, LicenseMetadata.EmptyVersion);
            b.Files.Add(new PhysicalPackageFile { SourcePath = Path.GetFullPath(o.LicenseFile), TargetPath = name });
        }

        List<PackageDependency> deps = [new(AbstractionsId, new VersionRange(abstractionsVersion))];
        if (part.IsEntry)
        {
            VersionRange exact = new(plan.Version, true, plan.Version, true);
            deps.AddRange(plan.Parts.Skip(1).Select(p => new PackageDependency(p.PackageId, exact)));
        }
        b.DependencyGroups.Add(new PackageDependencyGroup(FrameworkConstants.CommonFrameworks.NetStandard20, deps));

        b.Files.Add(new PhysicalPackageFile
        {
            SourcePath = dll,
            TargetPath = $"lib/netstandard2.0/{part.PackageId}.dll",
        });

        string readme = Path.Combine(workDir, part.PackageId + ".README.md");
        File.WriteAllText(readme, Readme(plan, part));
        b.Files.Add(new PhysicalPackageFile { SourcePath = readme, TargetPath = "README.md" });

        string nupkg = Path.Combine(Path.GetFullPath(o.OutputDir), $"{part.PackageId}.{plan.Version.ToNormalizedString()}.nupkg");
        using (FileStream fs = new(nupkg, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20))
            b.Save(fs);

        long size = new FileInfo(nupkg).Length;
        if (size > o.MaxNupkgBytes)
            throw new InvalidOperationException($"{Path.GetFileName(nupkg)} is {size:N0} bytes > limit {o.MaxNupkgBytes:N0}; lower --max-part-bytes");
        return nupkg;
    }

    private static string Description(PackPlan plan, PartPlan part, PackerOptions o) => part.IsEntry
        ? o.Description ?? $"{plan.FileName} as embedded .NET resources (part 1/{plan.Parts.Length}). " +
            $"Installing this package pulls in all remaining parts; {plan.Namespace}.Weights.Model() returns the stitched, seekable GGUF stream."
        : $"{plan.FileName} part {part.Index + 1}/{plan.Parts.Length}. Installed automatically by {plan.EntryId}; do not reference directly.";

    private static string Readme(PackPlan plan, PartPlan part) => part.IsEntry
        ? $"""
            # {plan.EntryId}

            `{plan.FileName}` ({plan.FileLength:N0} bytes, SHA-256 `{plan.FileSha256}`) packaged as {plan.Parts.Length} embedded-resource
            assemblies. This package is part 1; the others (`{plan.EntryId}.Part2` …) are pinned dependencies and restore automatically.

            ```shell
            dotnet add package {plan.EntryId}
            ```

            ```csharp
            using Stream gguf = {plan.Namespace}.Weights.Model(); // seekable, read-only, zero-copy per segment
            ```

            Built with [Sdcb.GgufWeights.Packer](https://github.com/sdcb/Sdcb.GgufWeights.Abstractions).
            """
        : $"""
            # {part.PackageId}

            Part {part.Index + 1}/{plan.Parts.Length} of `{plan.FileName}`. Install [{plan.EntryId}](https://www.nuget.org/packages/{plan.EntryId}) instead — it depends on this package.
            """;
}
