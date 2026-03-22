using System.Collections.Concurrent;
using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;

namespace HoldFast.E2E;

/// <summary>
/// Core tennet: HoldFast must NEVER phone home to any external service.
/// These tests capture every network request made by the frontend and assert
/// that nothing leaves our private network — no analytics, no feature flags,
/// no telemetry to third-party hosts.
/// </summary>
[TestFixture]
[Parallelizable(ParallelScope.Self)]
public class NetworkBoundaryTests : HoldFastFixture
{
    private readonly ConcurrentBag<string> _externalRequests = [];

    [SetUp]
    public async Task AttachNetworkMonitor()
    {
        Page.Request += (_, req) =>
        {
            var host = new Uri(req.Url).Host;
            if (!IsAllowedHost(host))
                _externalRequests.Add($"{req.Method} {req.Url}");
        };

        // Use Load (not NetworkIdle) — the page may never go idle if it's
        // hammering external hosts that don't respond. Load fires once the
        // DOM + scripts are done, which is enough to capture all registered calls.
        await Page.GotoAsync(BaseUrl, new() { WaitUntil = WaitUntilState.Load, Timeout = 15_000 });
        await Task.Delay(3000); // allow async beacon / telemetry calls to fire
    }

    [Test]
    public void PageLoad_MakesNoExternalRequests()
    {
        Assert.That(_externalRequests, Is.Empty,
            $"External requests detected on page load — nothing should leave the private network:\n" +
            string.Join("\n", _externalRequests));
    }

    [Test]
    public async Task Login_MakesNoExternalRequests()
    {
        _externalRequests.Clear(); // reset — only count requests from login onwards

        await Page.FillAsync("input[type=email], input[name=email]", AdminEmail);
        await Page.FillAsync("input[type=password]", AdminPassword);
        await Page.ClickAsync("button[type=submit]");

        await Task.Delay(3000); // allow any post-login telemetry to fire

        Assert.That(_externalRequests, Is.Empty,
            $"External requests detected during login:\n" +
            string.Join("\n", _externalRequests));
    }

    [Test]
    public async Task Login_Succeeds()
    {
        await Page.FillAsync("input[type=email], input[name=email]", AdminEmail);
        await Page.FillAsync("input[type=password]", AdminPassword);
        await Page.ClickAsync("button[type=submit]");

        await Page.WaitForURLAsync(url => !url.Contains("/login"), new() { Timeout = 15_000 });

        Assert.That(Page.Url, Does.Not.Contain("/login"),
            "Expected redirect away from /login after successful authentication.");
    }

    [Test]
    public async Task PostLogin_DashboardMakesNoExternalRequests()
    {
        await Page.FillAsync("input[type=email], input[name=email]", AdminEmail);
        await Page.FillAsync("input[type=password]", AdminPassword);
        await Page.ClickAsync("button[type=submit]");

        await Page.WaitForURLAsync(url => !url.Contains("/login"), new() { Timeout = 15_000 });
        _externalRequests.Clear(); // reset — only count post-login dashboard requests

        await Task.Delay(4000); // let session replay / analytics fire if present

        Assert.That(_externalRequests, Is.Empty,
            $"External requests detected on dashboard after login:\n" +
            string.Join("\n", _externalRequests));
    }

    private static bool IsAllowedHost(string host)
    {
        if (string.IsNullOrEmpty(host)) return true;
        foreach (var allowed in AllowedHosts)
        {
            if (host.Equals(allowed, StringComparison.OrdinalIgnoreCase)) return true;
            if (host.StartsWith(allowed, StringComparison.OrdinalIgnoreCase)) return true;
            if (host.EndsWith("." + allowed.TrimEnd('.'), StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }
}
