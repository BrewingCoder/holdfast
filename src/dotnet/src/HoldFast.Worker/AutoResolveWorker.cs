using HoldFast.Data;
using HoldFast.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HoldFast.Worker;

/// <summary>
/// Periodic worker that auto-resolves stale error groups.
/// Ported from Go's AutoResolveStaleErrors.
///
/// For each project with AutoResolveStaleErrorsDayInterval > 0:
/// - Finds all Open error groups with no new errors in the last N days
/// - Resolves them automatically
/// </summary>
public class AutoResolveWorker : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AutoResolveWorker> _logger;

    public AutoResolveWorker(IServiceScopeFactory scopeFactory, ILogger<AutoResolveWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("AutoResolveWorker started, interval={Interval}", Interval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunAutoResolveAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "AutoResolveWorker iteration failed");
            }

            await Task.Delay(Interval, stoppingToken);
        }
    }

    internal async Task RunAutoResolveAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HoldFastDbContext>();

        // Find all projects with auto-resolve enabled
        var settings = await db.ProjectFilterSettings
            .Where(s => s.AutoResolveStaleErrorsDayInterval > 0)
            .ToListAsync(ct);

        if (settings.Count == 0)
            return;

        var totalResolved = 0;

        foreach (var setting in settings)
        {
            var cutoff = DateTime.UtcNow.AddDays(-setting.AutoResolveStaleErrorsDayInterval);

            // Find open error groups with no recent errors
            var staleGroups = await db.ErrorGroups
                .Where(g => g.ProjectId == setting.ProjectId
                    && g.State == ErrorGroupState.Open)
                .Where(g => !db.ErrorObjects.Any(e =>
                    e.ErrorGroupId == g.Id && e.Timestamp >= cutoff))
                .ToListAsync(ct);

            foreach (var group in staleGroups)
            {
                group.State = ErrorGroupState.Resolved;
            }

            if (staleGroups.Count > 0)
            {
                await db.SaveChangesAsync(ct);
                totalResolved += staleGroups.Count;
                _logger.LogInformation(
                    "Auto-resolved {Count} stale errors for project {ProjectId} (interval={Days}d)",
                    staleGroups.Count, setting.ProjectId, setting.AutoResolveStaleErrorsDayInterval);
            }
        }

        if (totalResolved > 0)
        {
            _logger.LogInformation("AutoResolve total: {Total} error groups resolved", totalResolved);
        }
    }
}
