using System.Globalization;

namespace Sdcb.GgufWeights.Packer;

internal sealed class PackerOptions
{
    public const long DefaultMaxPartBytes = 240_000_000;
    public const long DefaultMaxNupkgBytes = 250L * 1000 * 1000;

    public List<string> Inputs { get; } = [];
    public string OutputDir { get; set; } = "nupkgs";
    public string Version { get; set; } = "1.0.0";
    public string IdPrefix { get; set; } = "Sdcb.GgufWeights.";
    public string? Id { get; set; }
    public string? Namespace { get; set; }
    public string? ModelId { get; set; }
    public long MaxPartBytes { get; set; } = DefaultMaxPartBytes;
    public long MaxNupkgBytes { get; set; } = DefaultMaxNupkgBytes;
    public string Authors { get; set; } = "sdcb";
    public string? Description { get; set; }
    public string? ProjectUrl { get; set; }
    public string? License { get; set; }
    public string? LicenseFile { get; set; }
    public string Tags { get; set; } = "gguf;weights;llm";
    public string? AbstractionsVersion { get; set; }
    public bool Verify { get; set; } = true;
    public bool KeepWork { get; set; }
    public bool DryRun { get; set; }

    public const string Usage = """
        Sdcb.GgufWeights.Packer — split GGUF files into <=250MB embedded-resource nupkgs.

        usage: Sdcb.GgufWeights.Packer <input.gguf|dir>... [options]

          -o, --output <dir>          output directory (default: nupkgs)
          -v, --version <ver>         package version (default: 1.0.0)
          --id <id>                   entry package id (single input only; default: <id-prefix><file stem>)
          --id-prefix <prefix>        package id prefix (default: Sdcb.GgufWeights.)
          --namespace <ns>            generated C# namespace (default: Sdcb.GgufWeights.<sanitized stem>)
          --model-id <id>             manifest ModelId (default: file stem)
          --max-part-bytes <n>        max payload bytes per part (default: 240000000)
          --max-nupkg-bytes <n>       fail if a nupkg exceeds this (default: 250000000)
          --authors <a>               nuspec authors (default: sdcb)
          --description <text>        entry package description
          --project-url <url>         nuspec projectUrl
          --license <spdx>            SPDX license expression, e.g. Apache-2.0
          --license-file <path>       license file embedded in every package (non-SPDX licenses)
          --tags <a;b;c>              nuspec tags (default: gguf;weights;llm)
          --abstractions-version <v>  min Sdcb.GgufWeights.Abstractions version (default: referenced assembly)
          --no-verify                 skip loading the built assemblies and re-hashing the stitched stream
          --keep-work                 keep intermediate DLLs under <output>/obj
          --dry-run                   print the split plan only
          -h, --help                  show this help
        """;

    public static PackerOptions Parse(string[] args)
    {
        PackerOptions o = new();
        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i];
            string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{a} needs a value");
            switch (a)
            {
                case "-o" or "--output": o.OutputDir = Next(); break;
                case "-v" or "--version": o.Version = Next(); break;
                case "--id": o.Id = Next(); break;
                case "--id-prefix": o.IdPrefix = Next(); break;
                case "--namespace": o.Namespace = Next(); break;
                case "--model-id": o.ModelId = Next(); break;
                case "--max-part-bytes": o.MaxPartBytes = long.Parse(Next(), CultureInfo.InvariantCulture); break;
                case "--max-nupkg-bytes": o.MaxNupkgBytes = long.Parse(Next(), CultureInfo.InvariantCulture); break;
                case "--authors": o.Authors = Next(); break;
                case "--description": o.Description = Next(); break;
                case "--project-url": o.ProjectUrl = Next(); break;
                case "--license": o.License = Next(); break;
                case "--license-file": o.LicenseFile = Next(); break;
                case "--tags": o.Tags = Next(); break;
                case "--abstractions-version": o.AbstractionsVersion = Next(); break;
                case "--no-verify": o.Verify = false; break;
                case "--keep-work": o.KeepWork = true; break;
                case "--dry-run": o.DryRun = true; break;
                case "-h" or "--help": throw new HelpRequestedException();
                default:
                    if (a.StartsWith('-'))
                        throw new ArgumentException($"unknown option {a}");
                    o.Inputs.Add(a);
                    break;
            }
        }

        if (o.Inputs.Count == 0)
            throw new HelpRequestedException();
        if (o.MaxPartBytes <= 0)
            throw new ArgumentException("--max-part-bytes must be positive");
        if (o.License is not null && o.LicenseFile is not null)
            throw new ArgumentException("--license and --license-file are mutually exclusive");
        return o;
    }
}

internal sealed class HelpRequestedException : Exception;
