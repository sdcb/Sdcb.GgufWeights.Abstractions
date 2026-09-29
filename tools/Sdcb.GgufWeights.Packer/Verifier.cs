using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using NuGet.Packaging;
using NuGet.Packaging.Core;

namespace Sdcb.GgufWeights.Packer;

/// <summary>
/// Loads the built entry assembly the same way a consumer would, re-hashes
/// <c>Weights.Model()</c> end to end, and sanity-checks every nupkg's nuspec.
/// </summary>
internal static class Verifier
{
    public static void Verify(PackPlan plan, IReadOnlyList<string> dlls, IReadOnlyList<string> nupkgs)
    {
        VerifyNupkgs(plan, nupkgs);
        WeakReference alc = VerifyStream(plan, dlls);
        for (int i = 0; alc.IsAlive && i < 10; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
    }

    private static void VerifyNupkgs(PackPlan plan, IReadOnlyList<string> nupkgs)
    {
        for (int i = 0; i < nupkgs.Count; i++)
        {
            using PackageArchiveReader r = new(nupkgs[i]);
            PackageIdentity id = r.GetIdentity();
            PartPlan part = plan.Parts[i];
            if (id.Id != part.PackageId || id.Version != plan.Version)
                throw new InvalidDataException($"{nupkgs[i]}: identity {id} != {part.PackageId} {plan.Version}");
            string lib = $"lib/netstandard2.1/{part.PackageId}.dll";
            if (!r.GetFiles().Contains(lib))
                throw new InvalidDataException($"{nupkgs[i]}: missing {lib}");
            if (part.IsEntry)
            {
                HashSet<string> deps = r.GetPackageDependencies().SelectMany(g => g.Packages)
                    .Where(d => d.VersionRange.IsFloating == false && d.VersionRange.MinVersion == plan.Version && d.VersionRange.MaxVersion == plan.Version)
                    .Select(d => d.Id).ToHashSet();
                foreach (PartPlan p in plan.Parts.Skip(1))
                    if (!deps.Contains(p.PackageId))
                        throw new InvalidDataException($"{nupkgs[i]}: missing exact dependency on {p.PackageId} [{plan.Version}]");
            }
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference VerifyStream(PackPlan plan, IReadOnlyList<string> dlls)
    {
        Dictionary<string, string> byName = dlls.ToDictionary(p => Path.GetFileNameWithoutExtension(p), StringComparer.OrdinalIgnoreCase);
        AssemblyLoadContext alc = new("verify:" + plan.EntryId, isCollectible: true);
        alc.Resolving += (ctx, name) => name.Name is { } n && byName.TryGetValue(n, out string? p) ? ctx.LoadFromAssemblyPath(p) : null;
        try
        {
            Assembly entry = alc.LoadFromAssemblyPath(dlls[0]);
            Type weights = entry.GetType(plan.Namespace + ".Weights", throwOnError: true)!;
            MethodInfo model = weights.GetMethod("Model", BindingFlags.Public | BindingFlags.Static)
                ?? throw new MissingMethodException(weights.FullName, "Model");
            using Stream s = (Stream)model.Invoke(null, null)!;
            if (s.Length != plan.FileLength)
                throw new InvalidDataException($"stitched length {s.Length} != {plan.FileLength}");
            string sha = Convert.ToHexStringLower(SHA256.HashData(s));
            if (sha != plan.FileSha256)
                throw new InvalidDataException($"stitched SHA-256 {sha} != {plan.FileSha256}");
        }
        finally
        {
            alc.Unload();
        }
        return new WeakReference(alc);
    }
}
