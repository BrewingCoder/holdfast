using Tamp;
using Tamp.NetCli.V10;

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
}
