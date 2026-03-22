using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;

namespace HoldFast.E2E;

/// <summary>
/// Base fixture — reads HOLDFAST_BASE_URL from env, defaults to the dev VM.
/// Set HOLDFAST_BASE_URL in CI or locally to point at any environment.
/// </summary>
public class HoldFastFixture : PageTest
{
    protected string BaseUrl =>
        Environment.GetEnvironmentVariable("HOLDFAST_BASE_URL")
        ?? "http://va-holdfast-dev.home.local:3000";

    protected string AdminEmail =>
        Environment.GetEnvironmentVariable("HOLDFAST_ADMIN_EMAIL")
        ?? "dev@holdfast.local";

    protected string AdminPassword =>
        Environment.GetEnvironmentVariable("HOLDFAST_ADMIN_PASSWORD")
        ?? "Oyster44";

    /// <summary>
    /// Domains that are allowed to receive network requests.
    /// Everything else is an external leak and must fail the test.
    /// </summary>
    protected static readonly string[] AllowedHosts =
    [
        "va-holdfast-dev.home.local",
        "localhost",
        "127.0.0.1",
        // Allow any RFC-1918 private range by prefix check
        "10.",
        "192.168.",
        "172.",
    ];

    public override BrowserNewContextOptions ContextOptions() =>
        new()
        {
            // Run headless by default; set PWHEADLESS=false locally to watch
            // (headless is controlled by PLAYWRIGHT_HEADLESS env in NUnit runner)
        };
}
