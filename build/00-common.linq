<Query Kind="Program">
  <Namespace>System.Threading.Tasks</Namespace>
  <Namespace>System.Net.Http</Namespace>
  <IncludeUncapsulator>false</IncludeUncapsulator>
</Query>

async Task Main()
{
	await SetupAsync(QueryCancelToken);
}

async Task SetupAsync(CancellationToken cancellationToken = default)
{
	Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
	Environment.CurrentDirectory = Util.CurrentQuery.Location;
	Directory.CreateDirectory("nupkgs");
	await EnsureNugetExe(cancellationToken);
}

static void NuGetRun(string args) => Run(@".\nuget.exe", args, Encoding.GetEncoding("gb2312"));
static void DotNetRun(string args) => Run("dotnet", args.Dump(), Encoding.GetEncoding("utf-8"));
static void Run(string exe, string args, Encoding encoding) => Util.Cmd(exe, args, encoding);

// Keep src/Sdcb.GgufWeights.Abstractions/Sdcb.GgufWeights.Abstractions.csproj <Version> in sync.
// -p:Version= is safe here: this project has no ProjectReference that NuGet would restamp.
static ProjectVersion[] Projects =
{
	new("Sdcb.GgufWeights.Abstractions", "1.0.0"),
};

// Weight nupkgs come from the packer, not dotnet pack. Edit these, then use "Pack GGUF" in 01.
static string WeightsVersion = "1.0.0";
static string GgufDirectory = @"D:\_\model";

static string FindProjectPath(string projectName)
{
	string repoRoot = Path.GetFullPath(Path.Combine(Util.CurrentQuery.Location, ".."));
	string[] matches = Directory.GetFiles(repoRoot, projectName + ".csproj", SearchOption.AllDirectories)
		.Where(p =>
		{
			string n = p.Replace('/', Path.DirectorySeparatorChar);
			return !n.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
				&& !n.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}");
		})
		.ToArray();
	if (matches.Length == 0)
		throw new Exception($"Project {projectName}.csproj was not found.");
	if (matches.Length > 1)
		throw new Exception($"Multiple {projectName}.csproj files found:{Environment.NewLine}{string.Join(Environment.NewLine, matches)}");
	return matches[0];
}

static async Task DownloadFile(Uri uri, string localFile, CancellationToken cancellationToken = default)
{
	if (uri.Scheme == "https" || uri.Scheme == "http")
	{
		using HttpClient http = new();

		HttpResponseMessage resp = await http.GetAsync(uri, cancellationToken);
		if (!resp.IsSuccessStatusCode)
		{
			throw new Exception($"Failed to download: {uri}, status code: {(int)resp.StatusCode}({resp.StatusCode})");
		}

		using (FileStream file = File.OpenWrite(localFile))
		{
			await resp.Content.CopyToAsync(file, cancellationToken);
		}
	}
	else if (uri.Scheme == "file")
	{
		File.Copy(uri.ToString()[8..], localFile, overwrite: true);
	}
	else
	{
		throw new Exception($"Uri scheme: {uri.Scheme} not supported.");
	}
}

static async Task<string> EnsureNugetExe(CancellationToken cancellationToken = default)
{
	Uri uri = new Uri(@"https://dist.nuget.org/win-x86-commandline/latest/nuget.exe");
	string localPath = @".\nuget.exe";
	if (!File.Exists(localPath))
	{
		await DownloadFile(uri, localPath, cancellationToken);
	}
	return localPath;
}

record ProjectVersion(string name, string version);
