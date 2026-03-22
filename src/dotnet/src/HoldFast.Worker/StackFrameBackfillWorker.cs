using HoldFast.Data;
using HoldFast.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HoldFast.Worker;

/// <summary>
/// Background worker that backfills ErrorObjects lacking a MappedStackTrace.
///
/// Mirrors Go's BackfillStackFrames() in worker.go. In the Go backend this enhances
/// stack traces with source maps via S3 upload lookup. The .NET self-hosted version
/// performs the same pass but without source map enhancement (which requires source maps
/// to be uploaded via the sourcemap SDK). Currently this worker copies StackTrace →
/// MappedStackTrace for any error objects that have been left unprocessed so that the
/// dashboard can show the raw frames rather than nothing.
///
/// Runs every 5 minutes, processes up to 200 objects per iteration.
/// </summary>
public class StackFrameBackfillWorker : BackgroundService
{
    internal static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);
    internal const int BatchSize = 200;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<StackFrameBackfillWorker> _logger;

    public StackFrameBackfillWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<StackFrameBackfillWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("StackFrameBackfillWorker started");

        // Initial delay to let the system warm up
        await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var processed = await RunBackfillAsync(stoppingToken);
                if (processed > 0)
                    _logger.LogInformation("StackFrameBackfill: processed {Count} error objects", processed);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "StackFrameBackfillWorker iteration failed");
            }

            await Task.Delay(Interval, stoppingToken);
        }
    }

    /// <summary>
    /// Find ErrorObjects with StackTrace but no MappedStackTrace and copy the raw trace.
    /// Returns the count of objects processed.
    /// </summary>
    internal async Task<int> RunBackfillAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HoldFastDbContext>();

        var objects = await db.ErrorObjects
            .Where(e => e.StackTrace != null && e.MappedStackTrace == null)
            .OrderBy(e => e.Id)
            .Take(BatchSize)
            .ToListAsync(ct);

        if (objects.Count == 0)
            return 0;

        foreach (var obj in objects)
        {
            // Copy raw stack trace as the mapped trace so the dashboard shows frames.
            // Real source map enhancement requires uploaded sourcemaps (future feature).
            obj.MappedStackTrace = obj.StackTrace;
        }

        // Also update the parent ErrorGroup if it has no MappedStackTrace yet
        var groupIds = objects.Select(o => o.ErrorGroupId).Distinct().ToList();
        var groups = await db.ErrorGroups
            .Where(g => groupIds.Contains(g.Id) && g.MappedStackTrace == null)
            .ToListAsync(ct);

        foreach (var group in groups)
        {
            var sample = objects.FirstOrDefault(o => o.ErrorGroupId == group.Id);
            if (sample != null)
                group.MappedStackTrace = sample.StackTrace;
        }

        await db.SaveChangesAsync(ct);
        return objects.Count;
    }
}
