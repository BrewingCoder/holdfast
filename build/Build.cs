using Tamp;
using Tamp.NetCli.V10;
using Tamp.Yarn.V4;
using Tamp.Turbo.V2;
using Tamp.Docker.V27;

class Build : TampBuild
{
    public static int Main(string[] args) => Execute<Build>(args);

    [Parameter("Build configuration (Debug|Release)")]
    Configuration Configuration = IsLocalBuild ? Configuration.Debug : Configuration.Release;

    [Solution(Path = "src/dotnet/HoldFast.Backend.slnx")] readonly Solution Solution = null!;
    [GitRepository] readonly GitRepository Git = null!;

    AbsolutePath Artifacts => RootDirectory / "artifacts";

    Target Info => _ => _
        .TopLevel()
        .Executes(() =>
        {
            Console.WriteLine("HoldFast build — first Tamp run");
            Console.WriteLine($"  Configuration:  {Configuration}");
            Console.WriteLine($"  Solution:       {Solution?.Path}");
            Console.WriteLine($"  Root:           {RootDirectory}");
            Console.WriteLine($"  Artifacts:      {Artifacts}");
            Console.WriteLine($"  Git branch:     {Git?.Branch}");
            Console.WriteLine($"  Git commit:     {Git?.Commit}");
        });

    Target Restore => _ => _
        .TopLevel()
        .Executes(() => DotNet.Restore(s => s
            .SetProject(Solution.Path)));

    Target Compile => _ => _
        .TopLevel()
        .DependsOn(nameof(Restore))
        .Executes(() => DotNet.Build(s => s
            .SetProject(Solution.Path)
            .SetConfiguration(Configuration)
            .SetNoRestore(true)));

    Target Test => _ => _
        .TopLevel()
        .DependsOn(nameof(Compile))
        .Executes(() => DotNet.Test(s => s
            .SetProject(Solution.Path)
            .SetConfiguration(Configuration)
            .SetNoBuild(true)
            .SetNoRestore(true)
            .SetResultsDirectory(Artifacts / "test-results")
            .AddLogger("trx;LogFileName=test-results.trx")));

    AbsolutePath DotnetSrc => RootDirectory / "src" / "dotnet";

    Target Clean => _ => _
        .TopLevel()
        .Executes(() =>
        {
            if (Artifacts.DirectoryExists())
            {
                Console.WriteLine($"  rm -rf {Artifacts}");
                Artifacts.DeleteDirectory();
            }
            Artifacts.EnsureDirectoryExists();

        });

    AbsolutePath PublishDir => Artifacts / "publish" / "HoldFast.Api";

    Target Publish => _ => _
        .TopLevel()
        .DependsOn(nameof(Compile))
        .Executes(() => DotNet.Publish(s => s
            .SetProject(RootDirectory / "src" / "dotnet" / "src" / "HoldFast.Api" / "HoldFast.Api.csproj")
            .SetConfiguration(Configuration)
            .SetOutput(PublishDir)
            .SetNoBuild(true)
            .SetNoRestore(true)));

    // ── Frontend (Yarn Berry 4.x + Turbo + Vite) ──────────────────────

    // No [FromPath] attribute in Tamp.Core yet — manually resolve yarn on PATH.
    // Tool ctor takes (AbsolutePath executable, string workingDirectory).
    static AbsolutePath ResolveOnPath(string name)
    {
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        var sep = OperatingSystem.IsWindows() ? ';' : ':';
        var exts = OperatingSystem.IsWindows()
            ? new[] { ".CMD", ".cmd", ".exe", ".EXE", ".bat", "" }
            : new[] { "" };
        foreach (var dir in pathEnv.Split(sep, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var ext in exts)
            {
                var candidate = Path.Combine(dir, name + ext);
                if (File.Exists(candidate)) return AbsolutePath.Create(candidate);
            }
        }
        throw new InvalidOperationException($"Could not find '{name}' on PATH");
    }

    Tool YarnTool => new(ResolveOnPath("yarn"), RootDirectory);

    Target YarnInstall => _ => _
        .TopLevel()
        .Executes(() => Yarn.Install(YarnTool, s => s.SetImmutable(true)));

    Target FrontendBuild => _ => _
        .TopLevel()
        .DependsOn(nameof(YarnInstall))
        .Executes(() => Yarn.Run(YarnTool, s => s.SetScript("build:frontend")));

    // ── Docker ──────────────────────────────────────────────────────

    Target DockerBuildBackend => _ => _
        .TopLevel()
        .Executes(() => Docker.Build(s => s
            .SetContext(RootDirectory)
            .SetDockerfile(RootDirectory / "infra" / "docker" / "backend-dotnet.Dockerfile")
            .AddTag("holdfast-backend-dotnet:tamp")));
}
