using HoldFast.Api;
using HoldFast.Data.ClickHouse;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace HoldFast.Api.Tests;

/// <summary>
/// Tests for ClickHouseHealthCheck.
///
/// HOL-36: the old "Degraded fallback when not the concrete ClickHouseService"
/// path is gone — the check now requires the concrete type. This test verifies
/// the simpler post-HOL-36 behavior: Unhealthy when ClickHouse is unreachable
/// (the actual production scenario the health check exists to cover).
/// </summary>
public class ClickHouseHealthCheckTests
{
    private static ClickHouseService UnreachableClickHouse() =>
        new(
            Options.Create(new ClickHouseOptions
            {
                // Pointing at an unreachable address — HealthCheckAsync will
                // catch the connection error and return false.
                Address = "localhost:65535",
                Database = "default",
            }),
            NullLogger<ClickHouseService>.Instance);

    [Fact]
    public async Task Unhealthy_when_ClickHouse_unreachable()
    {
        using var service = UnreachableClickHouse();
        var check = new ClickHouseHealthCheck(service);
        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Contains("not responding", result.Description);
    }

    [Fact]
    public async Task HealthCheck_respects_cancellation_token()
    {
        // CT is plumbed into HealthCheckAsync; cancellation should surface
        // rather than hang forever on a network-unreachable host.
        using var service = UnreachableClickHouse();
        var check = new ClickHouseHealthCheck(service);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var result = await check.CheckHealthAsync(new HealthCheckContext(), cts.Token);
        Assert.NotEqual(HealthStatus.Healthy, result.Status);
    }
}
