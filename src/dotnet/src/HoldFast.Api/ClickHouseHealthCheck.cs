using HoldFast.Data.ClickHouse;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace HoldFast.Api;

public class ClickHouseHealthCheck(IClickHouseService clickHouse) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        // Cast to concrete type to access HealthCheckAsync (not on the interface)
        if (clickHouse is ClickHouseService service)
        {
            var healthy = await service.HealthCheckAsync(cancellationToken);
            return healthy
                ? HealthCheckResult.Healthy("ClickHouse is responding")
                : HealthCheckResult.Unhealthy("ClickHouse is not responding");
        }

        return HealthCheckResult.Degraded("ClickHouse health check not available");
    }
}
