using Basic.Reference.Assemblies;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;

namespace Sdcb.GgufWeights.Packer;

/// <summary>Compiles one netstandard2.0 part DLL in-process, embedding its slice of the GGUF as a manifest resource.</summary>
internal static class AssemblyEmitter
{
    private static readonly MetadataReference AbstractionsRef =
        MetadataReference.CreateFromFile(typeof(SegmentStream).Assembly.Location);

    public static void Emit(PackPlan plan, PartPlan part, string source, IEnumerable<string> referencePaths, string outputDll)
    {
        SyntaxTree tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.CSharp7_3), path: part.ClassName + ".g.cs");
        List<MetadataReference> refs = [.. NetStandard20.References.All, AbstractionsRef];
        refs.AddRange(referencePaths.Select(p => MetadataReference.CreateFromFile(p)));

        CSharpCompilation compilation = CSharpCompilation.Create(
            part.PackageId,
            [tree],
            refs,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release,
                deterministic: true));

        ResourceDescription resource = new(
            part.ResourceName,
            () => new SliceStream(plan.InputPath, part.Offset, part.Length),
            isPublic: true);

        using FileStream pe = new(outputDll, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20);
        EmitResult result = compilation.Emit(pe, manifestResources: [resource]);
        if (!result.Success)
        {
            string errors = string.Join(Environment.NewLine, result.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.ToString()));
            throw new InvalidOperationException($"compile {part.PackageId} failed:{Environment.NewLine}{errors}");
        }
    }
}
