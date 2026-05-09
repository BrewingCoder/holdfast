using System.Net;
using ClickHouse.Client.ADO;
using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;

namespace HoldFast.E2E;

/// <summary>
/// Forensic ingest happy-path: a page using @holdfast-io/browser submits an
/// error and that error lands in ClickHouse error_objects.
///
/// Requires the local hobby stack to be up:
///   docker compose -f infra/docker/compose.yml -f infra/docker/compose.hobby-dotnet.yml up -d
/// and the ClickHouse schema to be applied (see HOL-11 for migration runner).
/// </summary>
[TestFixture]
[Parallelizable(ParallelScope.Self)]
public class BrowserSdkIngestTests : PageTest
{
    /// <summary>
    /// Project ID seeded by DevSeed for the "HoldFast Dev / default" project.
    /// DevSeed uses sequential IDs starting at 1; project 2 is the first dev project.
    /// </summary>
    private const int ProjectId = 2;

    private static string BackendUrl =>
        Environment.GetEnvironmentVariable("HOLDFAST_BACKEND_URL")
        ?? "http://localhost:8082/public";

    private static string OtlpEndpoint =>
        Environment.GetEnvironmentVariable("HOLDFAST_OTLP_ENDPOINT")
        ?? "http://localhost:4318";

    private static string ClickHouseUrl =>
        Environment.GetEnvironmentVariable("HOLDFAST_CLICKHOUSE_URL")
        ?? "Host=localhost;Port=8123;Database=default;Username=default;Password=";

    private static string RepoRoot => Path.GetFullPath(Path.Combine(
        TestContext.CurrentContext.TestDirectory, "..", "..", "..", "..", ".."));

    private HttpListener? _listener;
    private string _serverBaseUrl = "";

    [SetUp]
    public void StartLocalFileServer()
    {
        // Serve the sample page + SDK over HTTP so the browser sees an http:// origin
        // (file:// origins are treated as null in CORS and break the SDK's exporters).
        var port = GetFreePort();
        _serverBaseUrl = $"http://localhost:{port}";
        _listener = new HttpListener();
        _listener.Prefixes.Add($"{_serverBaseUrl}/");
        _listener.Start();
        _ = Task.Run(ServeAsync);
    }

    [TearDown]
    public void StopLocalFileServer()
    {
        try { _listener?.Stop(); _listener?.Close(); } catch { /* best-effort */ }
    }

    private async Task ServeAsync()
    {
        while (_listener?.IsListening == true)
        {
            HttpListenerContext ctx;
            try { ctx = await _listener.GetContextAsync(); }
            catch { return; }
            // Don't block the accept loop on slow file IO — fan out per request.
            _ = Task.Run(() => HandleRequestAsync(ctx));
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext ctx)
    {
        try
        {
            var path = ctx.Request.Url!.AbsolutePath.TrimStart('/');
            if (string.IsNullOrEmpty(path)) path = "tests/sample-browser/index.html";
            var filePath = Path.Combine(RepoRoot, path);
            if (File.Exists(filePath))
            {
                var bytes = await File.ReadAllBytesAsync(filePath);
                ctx.Response.ContentType = filePath.EndsWith(".html") ? "text/html"
                    : filePath.EndsWith(".js") || filePath.EndsWith(".cjs") ? "application/javascript"
                    : filePath.EndsWith(".map") ? "application/json"
                    : "application/octet-stream";
                ctx.Response.ContentLength64 = bytes.Length;
                await ctx.Response.OutputStream.WriteAsync(bytes);
            }
            else
            {
                ctx.Response.StatusCode = 404;
            }
        }
        catch { /* client likely disconnected */ }
        finally { try { ctx.Response.Close(); } catch { } }
    }

    private static int GetFreePort()
    {
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private string SamplePageUrl() => $"{_serverBaseUrl}/tests/sample-browser/index.html";

    [Test]
    public async Task Browser_TriggeredError_LandsInClickHouse()
    {
        // Capture all network requests at BrowserContext level so Web Worker
        // traffic (the SDK spins up a worker for async export) is also captured.
        var allRequests = new List<string>();
        var sdkRequests = new List<string>();
        Context.Request += (_, req) =>
        {
            allRequests.Add($"{req.Method} {req.Url}");
            if (req.Url.Contains(":8082/") || req.Url.Contains(":4318/") || req.Url.Contains(":4317/"))
                sdkRequests.Add($"{req.Method} {req.Url}");
        };

        // Capture page console + page errors — SDK warns to console when ingest fails
        var consoleMessages = new List<string>();
        var pageErrors = new List<string>();
        Page.Console += (_, msg) => consoleMessages.Add($"[{msg.Type}] {msg.Text}");
        Page.PageError += (_, err) => pageErrors.Add(err);

        // Capture response bodies for SDK requests so we can see GraphQL errors
        var sdkResponses = new List<string>();
        Context.Response += (_, resp) =>
        {
            if (resp.Url.Contains(":8082/public") || resp.Url.Contains(":4318/"))
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var body = await resp.TextAsync();
                        sdkResponses.Add($"{resp.Status} {resp.Url}\n{body.Substring(0, Math.Min(400, body.Length))}");
                    }
                    catch { }
                });
            }
        };

        var pageUri = $"{SamplePageUrl()}?project_id={ProjectId}" +
                      $"&backend_url={Uri.EscapeDataString(BackendUrl)}" +
                      $"&otlp_endpoint={Uri.EscapeDataString(OtlpEndpoint)}";

        await Page.GotoAsync(pageUri, new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 30_000 });

        // Wait for SDK init to complete (page sets a window flag on success)
        await Page.WaitForFunctionAsync("() => window.__holdfast_sdk_ready === true",
            new PageWaitForFunctionOptions { Timeout = 10_000 });

        // Tag this error so we can find it in ClickHouse — matches the page's
        // synthetic message format `HoldFast smoke test error @ <timestamp>`
        var beforeTs = DateTime.UtcNow.AddSeconds(-5);

        await Page.ClickAsync("#btn-error");

        // Allow the SDK to batch + flush. The browser SDK uses an OTLP exporter
        // with default batch settings — events may take a few seconds to ship.
        await Task.Delay(8_000);

        TestContext.Out.WriteLine("All requests captured:\n" + string.Join("\n", allRequests));
        TestContext.Out.WriteLine($"\nSDK (8082/4318/4317) requests: {sdkRequests.Count}");
        TestContext.Out.WriteLine("\n=== Console messages ===\n" + string.Join("\n", consoleMessages));
        TestContext.Out.WriteLine("\n=== Page errors ===\n" + string.Join("\n", pageErrors));
        TestContext.Out.WriteLine("\n=== SDK response bodies ===\n" + string.Join("\n---\n", sdkResponses));

        Assert.That(sdkRequests, Is.Not.Empty,
            "Expected at least one request to localhost (SDK should ship the error to backend or collector).\n" +
            "All requests:\n" + string.Join("\n", allRequests));

        // Now confirm the error landed in ClickHouse
        await using var conn = new ClickHouseConnection(ClickHouseUrl);
        await conn.OpenAsync();

        // error_objects holds individual occurrences (ProjectID + Timestamp);
        // error_groups holds the deduplicated record with the Event message.
        // Count occurrences for our project since the click — first ingest end-to-end.
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT count()
            FROM error_objects
            WHERE ProjectID = {projectId:Int32}
              AND Timestamp >= {since:DateTime64(6)}";
        cmd.Parameters.Add(new ClickHouse.Client.ADO.Parameters.ClickHouseDbParameter
        {
            ParameterName = "projectId",
            Value = ProjectId
        });
        cmd.Parameters.Add(new ClickHouse.Client.ADO.Parameters.ClickHouseDbParameter
        {
            ParameterName = "since",
            Value = beforeTs
        });

        var count = Convert.ToInt64(await cmd.ExecuteScalarAsync());

        Assert.That(count, Is.GreaterThanOrEqualTo(1),
            $"Expected at least one error_objects row matching the synthetic error " +
            $"for project {ProjectId} since {beforeTs:O}. SDK requests captured:\n" +
            string.Join("\n", sdkRequests));
    }

    [Test]
    public async Task Browser_PageLoad_SdkInitializesWithoutError()
    {
        var consoleErrors = new List<string>();
        Page.Console += (_, msg) =>
        {
            if (msg.Type == "error") consoleErrors.Add(msg.Text);
        };

        var pageUri = $"{SamplePageUrl()}?project_id={ProjectId}" +
                      $"&backend_url={Uri.EscapeDataString(BackendUrl)}";

        await Page.GotoAsync(pageUri, new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 30_000 });
        await Page.WaitForFunctionAsync("() => window.__holdfast_sdk_ready === true",
            new PageWaitForFunctionOptions { Timeout = 10_000 });

        var sdkError = await Page.EvaluateAsync<string?>("() => window.__holdfast_sdk_error ?? null");

        Assert.Multiple(() =>
        {
            Assert.That(sdkError, Is.Null, "SDK reported an init error.");
            Assert.That(consoleErrors, Is.Empty,
                "Console errors during SDK init:\n" + string.Join("\n", consoleErrors));
        });
    }
}
