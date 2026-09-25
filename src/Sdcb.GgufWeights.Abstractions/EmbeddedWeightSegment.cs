using System.IO;
using System.Reflection;

namespace Sdcb.GgufWeights;

/// <summary>
/// <see cref="IModelWeightSegment"/> over an assembly-embedded resource: the
/// standard shape for a generated weight package (one DLL + one resource blob).
/// </summary>
public sealed class EmbeddedWeightSegment : IModelWeightSegment
{
    private readonly Assembly _assembly;
    private readonly string _resourceName;

    public ModelSegmentManifest Manifest { get; }

    /// <param name="assembly">Assembly holding the resource (typically the caller's own).</param>
    /// <param name="resourceName">Manifest resource name of the weight blob.</param>
    public EmbeddedWeightSegment(Assembly assembly, string resourceName, ModelSegmentManifest manifest)
    {
        _assembly = assembly;
        _resourceName = resourceName;
        Manifest = manifest;
    }

    /// <summary>Opens the embedded resource stream — zero-copy over the loaded PE image.</summary>
    public Stream OpenStream()
        => _assembly.GetManifestResourceStream(_resourceName)
            ?? throw new FileNotFoundException($"resource '{_resourceName}' not found in {_assembly.GetName().Name}");
}
