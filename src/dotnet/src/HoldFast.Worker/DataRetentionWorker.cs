using HoldFast.Data;
using HoldFast.Domain.Entities;
using HoldFast.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HoldFast.Worker;

/// <summary>
/// Daily worker that cleans up data past retention periods.
/// Ported from Go's ProcessRetentionDeletions + StartSessionDeleteJob.
///
/// Per-workspace retention settings control how long data is kept.
/// Sessions, errors, logs, traces, and metrics are cleaned up independently.
/// </summary>
public class DataRetentionWorker : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<DataRetentionWorker> _logger;

    public DataRetentionWorker(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<DataRetentionWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("DataRetentionWorker started, interval={Interval}", Interval);

        // Delay initial run by 5 minutes to let system stabilize
        await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunRetentionCleanupAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "DataRetentionWorker iteration failed");
            }

            await Task.Delay(Interval, stoppingToken);
        }
    }

    internal async Task RunRetentionCleanupAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HoldFastDbContext>();

        var workspaces = await db.Workspaces.ToListAsync(ct);

        foreach (var workspace in workspaces)
        {
            try
            {
                await CleanupWorkspaceAsync(db, workspace, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed retention cleanup for workspace {WorkspaceId}", workspace.Id);
            }
        }
    }

    private async Task CleanupWorkspaceAsync(HoldFastDbContext db, Workspace workspace, CancellationToken ct)
    {
        var projectIds = await db.Projects
            .Where(p => p.WorkspaceId == workspace.Id)
            .Select(p => p.Id)
            .ToListAsync(ct);

        if (projectIds.Count == 0)
            return;

        // Session retention
        var sessionCutoff = GetRetentionCutoff(workspace.RetentionPeriod);
        var deletedSessions = await CleanupSessionsAsync(db, projectIds, sessionCutoff, ct);

        // Error retention
        var errorCutoff = GetRetentionCutoff(workspace.ErrorsRetentionPeriod);
        var deletedErrors = await CleanupErrorGroupsAsync(db, projectIds, errorCutoff, ct);

        if (deletedSessions > 0 || deletedErrors > 0)
        {
            _logger.LogInformation(
                "Workspace {WorkspaceId}: deleted {Sessions} sessions, {Errors} error groups",
                workspace.Id, deletedSessions, deletedErrors);
        }
    }

    private static async Task<int> CleanupSessionsAsync(
        HoldFastDbContext db, List<int> projectIds, DateTime cutoff, CancellationToken ct)
    {
        // Delete old sessions that haven't been viewed (matches Go behavior)
        var oldSessions = await db.Sessions
            .Where(s => projectIds.Contains(s.ProjectId)
                && s.CreatedAt < cutoff
                && (s.ViewedByAdmins == null || s.ViewedByAdmins == 0))
            .Take(1000) // Batch to avoid huge transactions
            .ToListAsync(ct);

        if (oldSessions.Count == 0)
            return 0;

        // Delete related data first
        var sessionIds = oldSessions.Select(s => s.Id).ToList();

        var chunks = await db.EventChunks.Where(c => sessionIds.Contains(c.SessionId)).ToListAsync(ct);
        db.EventChunks.RemoveRange(chunks);

        var intervals = await db.SessionIntervals.Where(i => sessionIds.Contains(i.SessionId)).ToListAsync(ct);
        db.SessionIntervals.RemoveRange(intervals);

        var rageClicks = await db.RageClickEvents.Where(r => sessionIds.Contains(r.SessionId)).ToListAsync(ct);
        db.RageClickEvents.RemoveRange(rageClicks);

        db.Sessions.RemoveRange(oldSessions);
        await db.SaveChangesAsync(ct);

        return oldSessions.Count;
    }

    private static async Task<int> CleanupErrorGroupsAsync(
        HoldFastDbContext db, List<int> projectIds, DateTime cutoff, CancellationToken ct)
    {
        // Delete resolved error groups with no recent errors
        var oldGroups = await db.ErrorGroups
            .Where(g => projectIds.Contains(g.ProjectId)
                && g.State == ErrorGroupState.Resolved
                && g.UpdatedAt < cutoff)
            .Take(500)
            .ToListAsync(ct);

        if (oldGroups.Count == 0)
            return 0;

        var groupIds = oldGroups.Select(g => g.Id).ToList();

        // Delete related data
        var fingerprints = await db.ErrorFingerprints.Where(f => groupIds.Contains(f.ErrorGroupId)).ToListAsync(ct);
        db.ErrorFingerprints.RemoveRange(fingerprints);

        var errorObjects = await db.ErrorObjects.Where(e => groupIds.Contains(e.ErrorGroupId)).ToListAsync(ct);
        db.ErrorObjects.RemoveRange(errorObjects);

        db.ErrorGroups.RemoveRange(oldGroups);
        await db.SaveChangesAsync(ct);

        return oldGroups.Count;
    }

    internal static DateTime GetRetentionCutoff(RetentionPeriod period) => period switch
    {
        RetentionPeriod.SevenDays => DateTime.UtcNow.AddDays(-7),
        RetentionPeriod.ThirtyDays => DateTime.UtcNow.AddDays(-30),
        RetentionPeriod.ThreeMonths => DateTime.UtcNow.AddMonths(-3),
        RetentionPeriod.SixMonths => DateTime.UtcNow.AddMonths(-6),
        RetentionPeriod.TwelveMonths => DateTime.UtcNow.AddMonths(-12),
        RetentionPeriod.TwoYears => DateTime.UtcNow.AddYears(-2),
        RetentionPeriod.ThreeYears => DateTime.UtcNow.AddYears(-3),
        _ => DateTime.UtcNow.AddMonths(-6), // Default: 6 months
    };
}
