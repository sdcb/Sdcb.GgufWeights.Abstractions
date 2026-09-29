<Query Kind="Program">
  <Namespace>System.Threading.Tasks</Namespace>
  <Namespace>LINQPad.Controls</Namespace>
  <IncludeUncapsulator>false</IncludeUncapsulator>
</Query>

#load ".\00-common"

DumpContainer dc = new DumpContainer().Dump();

async Task Main()
{
	await SetupAsync(QueryCancelToken);
	Refresh();
}

void Refresh()
{
	dc.Content = LoadTable();
}

object LoadTable()
{
	return new
	{
		Functions = Util.HorizontalRun(true,
			new Button("Build All", _ => Projects.ToList().ForEach(Build)),
			new Button("Pack GGUF", _ => PackGguf()),
			new Button("Clear Cache", _ => ClearNuGetCache()),
			new Button("📂Open nupkgs", _ => Process.Start("explorer", Path.GetFullPath(@".\nupkgs")))),
		Gguf = $"{GgufDirectory}  →  weight packages {WeightsVersion}",
		Table = Projects
			.Select(x => new
			{
				Project = x.name,
				Version = x.version,
				Build = new Button("Build", o => Build(x))
			})
	};
}

void ClearNuGetCache()
{
	DotNetRun("nuget locals http-cache --clear");
	DotNetRun("nuget locals temp --clear");
}

void Build(ProjectVersion p)
{
	QueryCancelToken.ThrowIfCancellationRequested();
	string projPosition = FindProjectPath(p.name);
	DotNetRun($@"pack ""{projPosition}"" -c Release -o .\nupkgs -p:Version={p.version}");
	Refresh();
}

void PackGguf()
{
	QueryCancelToken.ThrowIfCancellationRequested();
	if (!Directory.Exists(GgufDirectory))
		throw new DirectoryNotFoundException($"GGUF directory not found: {GgufDirectory} (edit GgufDirectory in 00-common.linq)");
	string packer = Path.GetFullPath(Path.Combine(Util.CurrentQuery.Location, @"..\tools\Sdcb.GgufWeights.Packer\Sdcb.GgufWeights.Packer.csproj"));
	DotNetRun($@"run --project ""{packer}"" -c Release -- ""{GgufDirectory}"" -o .\nupkgs -v {WeightsVersion}");
	Refresh();
}
