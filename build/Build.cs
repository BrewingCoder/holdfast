using Tamp;
using Tamp.NetCli.V10;
using Tamp.Yarn.V4;
using Tamp.Turbo.V2;
using Tamp.Docker.V27;
using Tamp.Helm.V3;
using Tamp.Http;
using Tamp.GraphQLCodegen.V5;
using Tamp.Coverlet.V6;
using Tamp.ReportGenerator.V5;
using Tamp.Syft;
using Tamp.Grype;
using Tamp.TruffleHog.V3;

class Build : TampBuild
{
    public static int Main(string[] args) => Execute<Build>(args);

    [Parameter("Build configuration (Debug|Release)")]
    Configuration Configuration = IsLocalBuild ? Configuration.Debug : Configuration.Release;

    [Parameter("Container registry for QA push")]
    string Registry = "localhost:32000";

    [Parameter("QA hostname (no trailing slash)")]
    string QaUrl = "https://holdfast.brewingcoder.com";

    [Parameter("Override the computed image tag (defaults to short git SHA)")]
    string? ImageTagOverride = null;

    // HoldFast is a multi-solution monorepo (SDK + e2e scaffolds also carry
    // .sln/.slnx files), so the subtree search would be ambiguous. Pin explicitly.
    [Solution("src/dotnet/HoldFast.Backend.slnx")] readonly Solution Solution = null!;
    [GitRepository] readonly GitRepository Git = null!;

    [FromPath("yarn")] readonly Tool YarnTool = null!;
    [FromPath("helm")] readonly Tool HelmTool = null!;
    // Compliance + coverage tools are operator-installed (one tool per axis;
    // see Tamp's Module Catalog). Marked Optional so the target surface
    // enumerates on machines without them — invocation will surface a
    // targeted error then, not a global injection failure.
    [FromPath("syft", Optional = true)] readonly Tool SyftTool = null!;
    [FromPath("grype", Optional = true)] readonly Tool GrypeTool = null!;
    [FromPath("trufflehog", Optional = true)] readonly Tool TruffleHogTool = null!;
    [FromPath("reportgenerator", Optional = true)] readonly Tool ReportGeneratorTool = null!;
    [FromNodeModules("turbo")] readonly Tool TurboTool = null!;
    [FromNodeModules("graphql-codegen", Optional = true)] readonly Tool GraphQLCodegenTool = null!;

    AbsolutePath Artifacts => RootDirectory / "artifacts";
    AbsolutePath PublishDir => Artifacts / "publish" / "HoldFast.Api";
    AbsolutePath CoverageDir => Artifacts / "coverage";
    AbsolutePath CoverageReportDir => Artifacts / "coverage-report";
    AbsolutePath Sbom => Artifacts / $"holdfast-{Version}.cdx.json";
    AbsolutePath HelmChart => RootDirectory / "infra" / "helm" / "holdfast";

    // Image tag = short git SHA (CLI override wins). Canonical version lives
    // in Chart.yaml.appVersion. GitVersion-derived semver is the future state
    // but Tamp.GitVersion.V6 0.1.1 doesn't ship the [GitVersion] injection
    // attribute yet — friction filed to airm5; revisit when that lands.
    string Version => ImageTagOverride ?? Git!.Commit[..7];
    string ImageTag => Version;
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
            Console.WriteLine($"  Version:        {Version}");
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

    // Coverage variant of Test — collects XPlat Code Coverage via the
    // standard data collector. Coverlet config built via the satellite's
    // Configure(...) helper, then handed to dotnet test as a runsettings
    // file. Kept separate from Test so the fast Ci path doesn't pay
    // coverage overhead on every run.
    Target CoverageTest => _ => _
        .DependsOn(Compile)
        .Executes(() =>
        {
            var runSettings = Artifacts / "coverlet.runsettings";
            System.IO.Directory.CreateDirectory(Artifacts);
            var xml = Coverlet.Configure(s => s
                .AddFormat(CoverletFormat.OpenCover)
                .AddExclude("[xunit.*]*")
                .AddExclude("[*.Tests]*")
                .SetUseSourceLink(true)).ToRunSettingsXml();
            System.IO.File.WriteAllText(runSettings, xml);

            return DotNet.Test(s => s
                .SetProject(Solution.Path)
                .SetConfiguration(Configuration)
                .SetNoBuild(true)
                .SetNoRestore(true)
                .SetResultsDirectory(CoverageDir)
                .SetSettings(runSettings)
                .AddLogger("trx;LogFileName=test-results.trx"));
        });

    Target CoverageReport => _ => _
        .DependsOn(CoverageTest)
        .Executes(() => ReportGenerator.Run(ReportGeneratorTool, s => s
            .AddReport(CoverageDir / "**" / "coverage.opencover.xml")
            .SetTargetDir(CoverageReportDir)
            .AddReportType("Html")
            .AddReportType("Badges")
            .AddReportType("MarkdownSummaryGithub")));

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

    // Regenerate GraphQL TypeScript types from src/backend/private-graph schema.
    // Generated files are checked in (src/frontend/src/graph/generated/) so
    // day-to-day frontend work doesn't have to wait on codegen — this target
    // runs on demand when *.gql or schema.graphqls drift.
    Target FrontendCodegen => _ => _
        .DependsOn(YarnInstall)
        .Executes(() => GraphQLCodegen.Generate(GraphQLCodegenTool, s => s
            .SetWorkingDirectory(RootDirectory / "src" / "frontend")
            .SetConfig("codegen.yml")));

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

    // ── Supply chain ─────────────────────────────────────────────────

    // CycloneDX SBOM for the whole repo. Excludes the transitively-vendored
    // node_modules / bin / obj noise so the SBOM reflects first-order deps
    // an operator actually has to defend. Output is consumed by CveGate.
    Target SbomScan => _ => _
        .Executes(() => Syft.Scan(SyftTool, s => s
            .SetDirectorySource(RootDirectory)
            .SetSourceName("HoldFast")
            .SetSourceVersion(Version)
            .AddOutputCycloneDxJson(Sbom)
            .AddExcludes("**/node_modules/**", "**/bin/**", "**/obj/**")));

    // CVE gate — reads the SBOM, hits NVD + GitHub Advisory DB + KEV, applies
    // EPSS-weighted composite risk scoring. Fails the build on >= high
    // severity. Adopters tune severity via --fail-on on the CLI.
    Target CveGate => _ => _
        .DependsOn(SbomScan)
        .Executes(() => Grype.Scan(GrypeTool, s => s
            .SetSbomSource(Sbom)
            .AddOutputJson()
            .SetOutputFile(Artifacts / "vulns.json")
            .SetFailOn("high")
            .SetSortBy("risk")
            .SetByCve(true)));

    // Secret scan — TruffleHog over the filesystem. Verified-only so unverified
    // pattern matches (often false positives in test fixtures) don't flap the
    // build. Run as part of Compliance, not Ci, because verification hits live
    // endpoints (slower than the no-network analyzers).
    Target SecretScan => _ => _
        .Executes(() => TruffleHog.Filesystem(TruffleHogTool, s => s
            .AddPath(RootDirectory)
            .SetOnlyVerified(true)
            .SetFail(true)));

    // Aggregate compliance gate — `dotnet tamp Compliance` runs the full
    // supply-chain triplet for a release-prep snapshot.
    Target Compliance => _ => _
        .DependsOn(SbomScan, CveGate, SecretScan);

    // ── Deploy ──────────────────────────────────────────────────────

    // Deploy the chart to the lab cluster. helm upgrade --install is idempotent;
    // --atomic rolls back automatically on a failed rollout.
    // Atomic disabled for now so a failed deploy leaves the cluster state
    // around for kubectl inspection. Re-enable once the chart has a few
    // green runs under it. Timeout bumped to 10m to give cold image pulls
    // (TimescaleDB-HA is 1.73 GB) headroom on first deploy to each node.
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
            .SetAtomic(false)
            .SetTimeout(TimeSpan.FromMinutes(10))));

    // Post-deploy smoke probe — polls /health until it returns 200 or the
    // timeout elapses. HttpProbe handles transient HttpRequestExceptions and
    // per-request timeouts as expected during pod warmup.
    // Backend's MapHealthChecks lands on /health (single endpoint, no
    // live/ready split). Don't append /live or /ready — those fall through
    // the SPA fallback to index.html (HTTP 200) and lie about health.
    Target SmokeQa => _ => _
        .DependsOn(DeployQa)
        .Executes(async () => await HttpProbe.WaitForHealthy(
            url: $"{QaUrl}/health",
            timeout: TimeSpan.FromMinutes(2)));

    // ── CI entry ─────────────────────────────────────────────────────

    // `dotnet tamp` (no args) runs the full verification + artifact pipeline.
    // Tamp.Core 1.3.0's params Target[] overload makes the fan-out one-liner.
    // Compliance (SBOM + CVE + secret scan) is deliberately NOT in Ci — it's
    // a release-prep step run separately so iteration on the fast path stays
    // fast. `dotnet tamp Compliance` runs it on demand.
    Target Ci => _ => _
        .Default()
        .DependsOn(Test, Publish, FrontendBuild, DockerBuildBackend);
}
