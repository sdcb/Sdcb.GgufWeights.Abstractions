using System.Diagnostics;
using NuGet.Versioning;
using Sdcb.GgufWeights;
using Sdcb.GgufWeights.Packer;

PackerOptions o;
try
{
    o = PackerOptions.Parse(args);
}
catch (HelpRequestedException)
{
    Console.WriteLine(PackerOptions.Usage);
    return args.Length == 0 ? 1 : 0;
}
catch (ArgumentException e)
{
    Console.Error.WriteLine(e.Message);
    Console.Error.WriteLine("run with --help for usage");
    return 1;
}

List<string> inputs = [];
foreach (string input in o.Inputs)
{
    if (Directory.Exists(input))
        inputs.AddRange(Directory.GetFiles(input, "*.gguf").Order(StringComparer.OrdinalIgnoreCase));
    else
        inputs.Add(input);
}
if (inputs.Count == 0)
{
    Console.Error.WriteLine("no .gguf inputs found");
    return 1;
}

Version asmVer = typeof(SegmentStream).Assembly.GetName().Version!;
NuGetVersion abstractionsVersion = NuGetVersion.Parse(o.AbstractionsVersion ?? $"{asmVer.Major}.{asmVer.Minor}.{asmVer.Build}");

try
{
    PackPlan[] plans = [.. inputs.Select(i => PackPlan.Create(i, o, inputs.Count == 1))];
    foreach (PackPlan plan in plans)
    {
        Console.WriteLine($"{plan.FileName}  {plan.FileLength:N0} bytes -> {plan.Parts.Length} package(s), ns {plan.Namespace}");
        foreach (PartPlan p in plan.Parts)
            Console.WriteLine($"  {p.PackageId,-60} offset {p.Offset,15:N0}  length {p.Length,13:N0}");
    }
    if (o.DryRun)
        return 0;

    Directory.CreateDirectory(o.OutputDir);
    foreach (PackPlan plan in plans)
        Pack(plan);
    return 0;
}
catch (Exception e) when (e is ArgumentException or IOException or InvalidDataException or InvalidOperationException)
{
    Console.Error.WriteLine($"error: {e.Message}");
    return 2;
}

void Pack(PackPlan plan)
{
    Stopwatch sw = Stopwatch.StartNew();
    Console.WriteLine($"[{plan.EntryId}] hashing {plan.FileName} ...");
    plan.ComputeHashes();
    Console.WriteLine($"  sha256 {plan.FileSha256}  ({sw.Elapsed.TotalSeconds:F1}s)");

    string workDir = Path.Combine(Path.GetFullPath(o.OutputDir), "obj", plan.EntryId);
    Directory.CreateDirectory(workDir);
    string[] dlls = new string[plan.Parts.Length];
    string[] nupkgs = new string[plan.Parts.Length];

    // Entry (part 1) references every other part's Segment, so build it last.
    int[] order = [.. Enumerable.Range(1, plan.Parts.Length - 1), 0];
    foreach (int i in order)
    {
        PartPlan part = plan.Parts[i];
        dlls[i] = Path.Combine(workDir, part.PackageId + ".dll");
        string source = SourceGenerator.Generate(plan, part);
        File.WriteAllText(Path.ChangeExtension(dlls[i], ".g.cs"), source);

        long t0 = sw.ElapsedMilliseconds;
        AssemblyEmitter.Emit(plan, part, source, part.IsEntry ? dlls.Skip(1) : [], dlls[i]);
        long t1 = sw.ElapsedMilliseconds;
        nupkgs[i] = NupkgWriter.Write(plan, part, dlls[i], workDir, o, abstractionsVersion);
        long t2 = sw.ElapsedMilliseconds;
        Console.WriteLine($"  {Path.GetFileName(nupkgs[i]),-70} {new FileInfo(nupkgs[i]).Length,13:N0} bytes  (compile {(t1 - t0) / 1000.0:F1}s, pack {(t2 - t1) / 1000.0:F1}s)");
    }

    if (o.Verify)
    {
        long t0 = sw.ElapsedMilliseconds;
        Verifier.Verify(plan, dlls, nupkgs);
        Console.WriteLine($"  verified: Weights.Model() stitches to sha256 {plan.FileSha256}  ({(sw.ElapsedMilliseconds - t0) / 1000.0:F1}s)");
    }

    if (!o.KeepWork)
    {
        try { Directory.Delete(workDir, recursive: true); }
        catch (IOException e) { Console.WriteLine($"  note: could not delete {workDir}: {e.Message}"); }
        catch (UnauthorizedAccessException e) { Console.WriteLine($"  note: could not delete {workDir}: {e.Message}"); }
    }
    Console.WriteLine($"[{plan.EntryId}] done in {sw.Elapsed.TotalSeconds:F1}s");
}
