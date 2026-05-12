using Tamp;
using Tamp.NetCli.V10;
using Tamp.Yarn.V4;
using Tamp.Turbo.V2;
using Tamp.Docker.V27;
using Tamp.Helm.V3;
using Tamp.Http;

class Build : TampBuild
{
    public static int Main(string[] args) => Execute<Build>(args);

    [Parameter("Build configuration (Debug|Release)")]
    Configuration Configuration = IsLocalBuild ? Configuration.Debug : Configuration.Release;

    [Parameter("Container registry for QA push")]
    readonly string Registry = "localhost:32000";

    [Parameter("QA hostname (no trailing slash)")]
    readonly string QaUrl = "https://holdfast.brewingcoder.com";

    // HoldFast is a multi-solution monorepo (SDK + e2e scaffolds also carry
    // .sln/.slnx files), so the subtree search would be ambiguous. Pin explicitly.
    [Solution("src/dotnet/HoldFast.Backend.slnx")] readonly Solution Solution = null!;
    [GitRepository] readonly GitRepository Git = null!;

    [FromPath("yarn")] readonly Tool YarnTool = null!;
    [FromPath("helm")] readonly Tool HelmTool = null!;
    [FromNodeModules("turbo")] readonly Tool TurboTool = null!;

    AbsolutePath Artifacts => RootDirectory / "artifacts";
    AbsolutePath PublishDir => Artifacts / "publish" / "HoldFast.Api";
    AbsolutePath HelmChart => RootDirectory / "infra" / "helm" / "holdfast";

    // Image tag = short git SHA. Canonical version lives in Chart.yaml.appVersion.
    string ImageTag => Git!.Commit[..7];
    string LocalImageRef => $"holdfast-backend-dotnet:{ImageTag}";
    string RegistryImageRef => $"{Registry}/holdfast-backend-dotnet:{ImageTag}";

    Target Info => _ => _
        .Executes(() =>
        {
            Console.WriteLine("HoldFast build via Tamp");
            Console.WriteLine($"  Configuration:  {Configuration}");
            Console.WriteLine($"  Solution:       {Solution?.Path}");
            Console.WriteLine($"  Root:           {RootDirectory}");
            Console.WriteLine($"  Artifacts:      {Artifacts}");
            Console.WriteLine($"  Git branch:     {Git?.Branch}");
            Console.WriteLine($"  Git commit:     {Git?.Commit}");
            Console.WriteLine($"  Image tag:      {ImageTag}");
            Console.WriteLine($"  Registry ref:   {RegistryImageRef}");
            Console.WriteLine($"  QA URL:         {QaUrl}");
        });

    Target Restore => _ => _
        .Executes(() => DotNet.Restore(s => s
            .SetProject(Solution.Path)));

    Target Compile => _ => _
        .DependsOn(Restore)
        .Executes(() => DotNet.Build(s => s
            .SetProject(Solution.Path)
            .SetConfiguration(Configuration)
            .SetNoRestore(true)));

    // NetCli.V10 1.0.9+ auto-expands LogFileName → LogFilePrefix in solution
    // mode, so this produces one TRX file per test assembly.
    Target Test => _ => _
        .DependsOn(Compile)
        .Executes(() => DotNet.Test(s => s
            .SetProject(Solution.Path)
            .SetConfiguration(Configuration)
            .SetNoBuild(true)
            .SetNoRestore(true)
            .SetResultsDirectory(Artifacts / "test-results")
            .AddLogger("trx;LogFileName=test-results.trx")));

    // CleanArtifacts(): framework-provided safe wipe — Solution.Projects only,
    // self-deletion guarded. Never use RootDirectory.GlobDirectories("**/bin")
    // — that's the friction-#12 footgun.
    Target Clean => _ => _
        .Executes(() => CleanArtifacts());

    Target Publish => _ => _
        .DependsOn(Compile)
        .Executes(() => DotNet.Publish(s => s
            .SetProject(RootDirectory / "src" / "dotnet" / "src" / "HoldFast.Api" / "HoldFast.Api.csproj")
            .SetConfiguration(Configuration)
            .SetOutput(PublishDir)
            .SetNoBuild(true)
            .SetNoRestore(true)));

    // ── Frontend (Yarn Berry 4.x + Turbo + Vite) ──────────────────────

    Target YarnInstall => _ => _
        .Executes(() => Yarn.Install(YarnTool, s => s.SetImmutable(true)));

    // Workspace-local turbo only exists after YarnInstall populates
    // node_modules/.bin/turbo, so this DependsOn is mandatory.
    Target FrontendBuild => _ => _
        .DependsOn(YarnInstall)
        .Executes(() => Turbo.Run(TurboTool, s => s
            .SetWorkingDirectory(RootDirectory)
            .AddTask("build:fast")
            .AddFilter("@holdfast-io/frontend...")));

    // ── Docker ──────────────────────────────────────────────────────

    // Docker.V27 0.3.x routes Build through `docker buildx build`, so the
    // Dockerfile's `RUN --mount=type=cache` directives work. Two tags so the
    // local-shorthand reference and the registry-prefixed reference both land.
    Target DockerBuildBackend => _ => _
        .Executes(() => Docker.Build(s => s
            .SetContext(RootDirectory)
            .SetDockerfile(RootDirectory / "infra" / "docker" / "backend-dotnet.Dockerfile")
            .AddTag(LocalImageRef)
            .AddTag(RegistryImageRef)));

    // Push the registry-prefixed image to the lab registry. ARC runner has its
    // ~/.docker/config.json populated for localhost:32000 (plain HTTP, daemon-
    // level insecure-registries setting); no Docker.Login call needed.
    Target DockerPush => _ => _
        .DependsOn(DockerBuildBackend)
        .Executes(() => Docker.Push(s => s
            .SetImage(RegistryImageRef)));

    // ── Deploy ──────────────────────────────────────────────────────

    // Deploy the chart to the lab cluster. helm upgrade --install is idempotent;
    // --atomic rolls back automatically on a failed rollout.
    Target DeployQa => _ => _
        .DependsOn(DockerPush)
        .Executes(() => Helm.Upgrade(HelmTool, s => s
            .SetRelease("holdfast")
            .SetNamespace("holdfast")
            .SetCreateNamespace(true)
            .SetChart(HelmChart)
            .AddValuesFile(HelmChart / "values.lab.yaml")
            .SetValue("image.tag", ImageTag)
            .SetWait(true)
            .SetAtomic(true)
            .SetTimeout(TimeSpan.FromMinutes(5))));

    // Post-deploy smoke probe — polls /health/live until it returns 200 or
    // the timeout elapses. HttpProbe handles transient HttpRequestExceptions
    // and per-request timeouts as expected during pod warmup.
    Target SmokeQa => _ => _
        .DependsOn(DeployQa)
        .Executes(async () => await HttpProbe.WaitForHealthy(
            url: $"{QaUrl}/health/live",
            timeout: TimeSpan.FromMinutes(2)));

    // ── CI entry ─────────────────────────────────────────────────────

    // `dotnet tamp` (no args) runs the full verification + artifact pipeline.
    // Tamp.Core 1.3.0's params Target[] overload makes the fan-out one-liner.
    Target Ci => _ => _
        .Default()
        .DependsOn(Test, Publish, FrontendBuild, DockerBuildBackend);
}
