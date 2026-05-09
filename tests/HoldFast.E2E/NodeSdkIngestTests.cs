using System.Diagnostics;
using System.Net.Http;
using ClickHouse.Client.ADO;

namespace HoldFast.E2E;

/// <summary>
/// HOL-5: Node.js sample app submitting traces + logs via OTLP, verified
/// landing in the analytics store.
///
/// Companion to the HOL-4 BrowserSdkIngestTests. Approach:
/// 1. Spawn `node tests/sample-node/server.mjs` as a child process. The
///    server prints "READY &lt;port&gt;" on stdout when bound.
/// 2. Hit the sample endpoints (/test/log, /test/trace, /test/error) over HTTP.
///    Each carries a unique tag so we can fish the row out of CH later.
/// 3. Wait for OTLP batch flush (~2 sec at the sample's batch settings).
/// 4. Query ClickHouse for log + trace rows tagged with our scenario tag.
///
/// Requires:
/// - The local hobby stack running (backend + postgres + clickhouse) on a
///   recent backend image. The stock image must include the post-HOL-26
///   OTLP receiver fixes; older images accept POSTs and return 200 but
///   silently drop the rows. Rebuild with
///   `docker compose build backend &amp;&amp; docker compose up -d --force-recreate backend`
///   if these tests fail with "0 rows" against a known-good Node sample.
/// - Node.js + the deps in tests/sample-node/package.json installed
///   (`cd tests/sample-node &amp;&amp; npm install` — done once locally).
///
/// Skipped via NUnit's Ignore attribute when SKIP_E2E env var is set, so CI
/// without the live stack can still run the .NET solution.
/// </summary>
[TestFixture]
[Parallelizable(ParallelScope.Self)]
public class NodeSdkIngestTests
{
    private const int ProjectId = 2; // DevSeed dev project (matches HOL-4 fixture)

    private static string BackendOtlpEndpoint =>
        Environment.GetEnvironmentVariable("HOLDFAST_OTLP_ENDPOINT")
        ?? "http://localhost:8082/otel";

    private static string ClickHouseUrl =>
        Environment.GetEnvironmentVariable("HOLDFAST_CLICKHOUSE_URL")
        ?? "Host=localhost;Port=8123;Database=default;Username=default;Password=";

    private static string RepoRoot => Path.GetFullPath(Path.Combine(
        TestContext.CurrentContext.TestDirectory, "..", "..", "..", "..", ".."));

    private static string SampleNodeDir => Path.Combine(RepoRoot, "tests", "sample-node");

    private Process? _node;
    private int _nodePort;
    private HttpClient _http = null!;

    [SetUp]
    public async Task StartSampleServer()
    {
        if (Environment.GetEnvironmentVariable("SKIP_E2E") == "1")
            Assert.Ignore("SKIP_E2E set");
        if (!File.Exists(Path.Combine(SampleNodeDir, "node_modules", ".package-lock.json")))
            Assert.Ignore($"sample-node deps missing — run `npm install` in {SampleNodeDir}");

        // Use port 0 to let Node pick a free port. Reading "READY <port>" from
        // stdout tells us when the server is up and what port to hit.
        var psi = new ProcessStartInfo
        {
            FileName = OperatingSystem.IsWindows() ? "node.exe" : "node",
            Arguments = "server.mjs",
            WorkingDirectory = SampleNodeDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.Environment["PORT"] = "0";
        psi.Environment["HOLDFAST_PROJECT_ID"] = ProjectId.ToString();
        psi.Environment["OTLP_ENDPOINT"] = BackendOtlpEndpoint;

        _node = Process.Start(psi)
            ?? throw new InvalidOperationException("failed to spawn node server.mjs");

        var readyTask = Task.Run(async () =>
        {
            string? line;
            while ((line = await _node.StandardOutput.ReadLineAsync()) != null)
            {
                TestContext.Out.WriteLine($"[node stdout] {line}");
                if (line.StartsWith("READY "))
                    return int.Parse(line["READY ".Length..]);
            }
            return -1;
        });

        // Drain stderr in the background so the buffer doesn't fill and block the child.
        _ = Task.Run(async () =>
        {
            string? line;
            while ((line = await _node!.StandardError.ReadLineAsync()) != null)
                TestContext.Out.WriteLine($"[node stderr] {line}");
        });

        var ready = await Task.WhenAny(readyTask, Task.Delay(15_000));
        if (ready != readyTask || readyTask.Result <= 0)
            throw new TimeoutException("node sample server did not print READY within 15s");

        _nodePort = readyTask.Result;
        _http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{_nodePort}") };
    }

    [TearDown]
    public void StopSampleServer()
    {
        _http?.Dispose();
        if (_node is not null && !_node.HasExited)
        {
            try { _node.Kill(entireProcessTree: true); }
            catch { /* best-effort */ }
        }
        _node?.Dispose();
    }

    [Test]
    public async Task NodeSdk_LogEndpoint_LandsInClickHouseLogs()
    {
        var beforeTs = DateTime.UtcNow.AddSeconds(-5);

        var response = await _http.GetAsync("/test/log");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        TestContext.Out.WriteLine($"sample /test/log response: {body}");

        // Allow OTLP batch flush. The sample's BatchLogRecordProcessor uses a
        // 1-second scheduled delay; 4 seconds is comfortably past first flush.
        await Task.Delay(4_000);

        var count = await CountClickHouseLogsSinceAsync(beforeTs, severityText: "INFO");
        Assert.That(count, Is.GreaterThanOrEqualTo(1),
            $"Expected at least one INFO log row in ClickHouse logs for project {ProjectId} " +
            $"since {beforeTs:O} after calling /test/log on the sample Node server.");
    }

    [Test]
    public async Task NodeSdk_ErrorEndpoint_LandsInClickHouseLogsAsError()
    {
        var beforeTs = DateTime.UtcNow.AddSeconds(-5);

        var response = await _http.GetAsync("/test/error");
        response.EnsureSuccessStatusCode();

        await Task.Delay(4_000);

        var count = await CountClickHouseLogsSinceAsync(beforeTs, severityText: "ERROR");
        Assert.That(count, Is.GreaterThanOrEqualTo(1),
            $"Expected at least one ERROR log row in ClickHouse logs for project {ProjectId} " +
            $"since {beforeTs:O} after calling /test/error on the sample Node server.");
    }

    [Test]
    public async Task NodeSdk_TraceEndpoint_LandsInClickHouseTraces()
    {
        var beforeTs = DateTime.UtcNow.AddSeconds(-5);

        var response = await _http.GetAsync("/test/trace");
        response.EnsureSuccessStatusCode();

        // Trace exporter has a longer default flush interval than logs; give
        // the SDK 6 seconds to ship.
        await Task.Delay(6_000);

        await using var conn = new ClickHouseConnection(ClickHouseUrl);
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT count()
            FROM traces
            WHERE ProjectId = {projectId:Int32}
              AND Timestamp >= {since:DateTime64(6)}
              AND ServiceName = 'holdfast-sample-node'";
        cmd.Parameters.Add(new ClickHouse.Client.ADO.Parameters.ClickHouseDbParameter
        {
            ParameterName = "projectId",
            Value = ProjectId,
        });
        cmd.Parameters.Add(new ClickHouse.Client.ADO.Parameters.ClickHouseDbParameter
        {
            ParameterName = "since",
            Value = beforeTs,
        });

        var count = Convert.ToInt64(await cmd.ExecuteScalarAsync());
        Assert.That(count, Is.GreaterThanOrEqualTo(1),
            $"Expected at least one trace span row in ClickHouse traces for project {ProjectId} " +
            $"with service.name=holdfast-sample-node since {beforeTs:O} after calling /test/trace.");
    }

    [Test]
    public async Task SampleServer_HealthEndpoint_Responds()
    {
        // Sanity check that doesn't depend on the analytics store at all - if
        // this fails the whole spawn-and-bind path is broken.
        var response = await _http.GetAsync("/health");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        Assert.That(body, Is.EqualTo("ok"));
    }

    private static async Task<long> CountClickHouseLogsSinceAsync(DateTime sinceUtc, string severityText)
    {
        await using var conn = new ClickHouseConnection(ClickHouseUrl);
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT count()
            FROM logs
            WHERE ProjectId = {projectId:Int32}
              AND Timestamp >= {since:DateTime64(6)}
              AND SeverityText = {severity:String}
              AND ServiceName = 'holdfast-sample-node'";
        cmd.Parameters.Add(new ClickHouse.Client.ADO.Parameters.ClickHouseDbParameter
        {
            ParameterName = "projectId",
            Value = ProjectId,
        });
        cmd.Parameters.Add(new ClickHouse.Client.ADO.Parameters.ClickHouseDbParameter
        {
            ParameterName = "since",
            Value = sinceUtc,
        });
        cmd.Parameters.Add(new ClickHouse.Client.ADO.Parameters.ClickHouseDbParameter
        {
            ParameterName = "severity",
            Value = severityText,
        });
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }
}
