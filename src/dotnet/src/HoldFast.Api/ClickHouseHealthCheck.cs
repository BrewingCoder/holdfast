using HoldFast.Data.ClickHouse;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace HoldFast.Api;

/// <summary>
/// Health check for ClickHouse connectivity. Reports degraded if the ping query fails.
///
/// HOL-36: now takes the concrete ClickHouseService directly. The check is
/// CH-specific (Postgres has its own pg_isready compose-level health check),
/// so there's no abstraction to thread through here.
/// </summary>
public class ClickHouseHealthCheck(ClickHouseService clickHouse) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var healthy = await clickHouse.HealthCheckAsync(cancellationToken);
        return healthy
            ? HealthCheckResult.Healthy("ClickHouse is responding")
            : HealthCheckResult.Unhealthy("ClickHouse is not responding");
    }
}
