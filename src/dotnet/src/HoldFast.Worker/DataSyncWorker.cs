using HoldFast.Data;
using HoldFast.Data.ClickHouse;
using HoldFast.Data.ClickHouse.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HoldFast.Worker;

/// <summary>
/// Background worker that periodically syncs finalized data from PostgreSQL to ClickHouse.
///
/// The Go backend triggers syncs via Kafka messages. For the .NET self-hosted version,
/// a polling approach is simpler and sufficient — there are no multi-tenant scale concerns.
///
/// Syncs:
/// - Processed sessions (Processed=true, updated in the last hour)
/// - Error groups (updated in the last hour)
/// - Error objects (created in the last hour)
///
/// Batches at 500 records per sync to avoid memory pressure.
/// </summary>
public class DataSyncWorker : BackgroundService
{
    internal static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);
    internal const int BatchSize = 500;

    // How far back to look for data to sync (sliding window)
    internal static readonly TimeSpan SyncWindow = TimeSpan.FromHours(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DataSyncWorker> _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="DataSyncWorker"/>.
    /// </summary>
    /// <param name="scopeFactory">Factory for creating DI scopes per sync iteration.</param>
    /// <param name="logger">Logger instance for diagnostic output.</param>
    public DataSyncWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<DataSyncWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <summary>
    /// Main loop: waits 30 seconds for startup, then runs sync every <see cref="Interval"/>.
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("DataSyncWorker started, interval={Interval}", Interval);

        // Delay initial run by 30 seconds to let system stabilize
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunSyncAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "DataSyncWorker iteration failed");
            }

            await Task.Delay(Interval, stoppingToken);
        }
    }

    /// <summary>
    /// Execute a single sync iteration: sessions, error groups, and error objects
    /// modified within the <see cref="SyncWindow"/> are written to ClickHouse.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    internal async Task RunSyncAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HoldFastDbContext>();
        var clickHouse = scope.ServiceProvider.GetRequiredService<IClickHouseService>();

        var cutoff = DateTime.UtcNow.Subtract(SyncWindow);

        var sessionCount = await SyncSessionsAsync(db, clickHouse, cutoff, ct);
        var errorGroupCount = await SyncErrorGroupsAsync(db, clickHouse, cutoff, ct);
        var errorObjectCount = await SyncErrorObjectsAsync(db, clickHouse, cutoff, ct);

        if (sessionCount > 0 || errorGroupCount > 0 || errorObjectCount > 0)
        {
            _logger.LogInformation(
                "DataSync complete: {Sessions} sessions, {ErrorGroups} error groups, {ErrorObjects} error objects",
                sessionCount, errorGroupCount, errorObjectCount);
        }
    }

    /// <summary>
    /// Sync processed sessions updated after <paramref name="cutoff"/> to ClickHouse.
    /// </summary>
    /// <param name="db">PostgreSQL database context.</param>
    /// <param name="clickHouse">ClickHouse write service.</param>
    /// <param name="cutoff">Only sync sessions updated on or after this time.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Number of sessions synced.</returns>
    internal static async Task<int> SyncSessionsAsync(
        HoldFastDbContext db, IClickHouseService clickHouse, DateTime cutoff, CancellationToken ct)
    {
        var sessions = await db.Sessions
            .Where(s => s.Processed == true && s.UpdatedAt >= cutoff)
            .OrderBy(s => s.Id)
            .Take(BatchSize)
            .ToListAsync(ct);

        if (sessions.Count == 0)
            return 0;

        var rows = sessions.Select(s => new SessionRowInput
        {
            ProjectId = s.ProjectId,
            SessionId = s.Id,
            SecureSessionId = s.SecureId,
            CreatedAt = s.CreatedAt,
            Identifier = s.Identifier,
            OSName = s.OSName,
            OSVersion = s.OSVersion,
            BrowserName = s.BrowserName,
            BrowserVersion = s.BrowserVersion,
            City = s.City,
            State = s.State,
            Country = s.Country,
            Environment = s.Environment,
            AppVersion = s.AppVersion,
            ServiceName = s.ServiceName,
            ActiveLength = s.ActiveLength ?? 0,
            Length = s.Length ?? 0,
            PagesVisited = s.PagesVisited ?? 0,
            HasErrors = s.HasErrors ?? false,
            HasRageClicks = s.HasRageClicks ?? false,
            Processed = s.Processed ?? false,
            FirstTime = s.FirstTime == 1,
        });

        await clickHouse.WriteSessionsAsync(rows, ct);
        return sessions.Count;
    }

    /// <summary>
    /// Sync error groups updated after <paramref name="cutoff"/> to ClickHouse.
    /// </summary>
    /// <param name="db">PostgreSQL database context.</param>
    /// <param name="clickHouse">ClickHouse write service.</param>
    /// <param name="cutoff">Only sync error groups updated on or after this time.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Number of error groups synced.</returns>
    internal static async Task<int> SyncErrorGroupsAsync(
        HoldFastDbContext db, IClickHouseService clickHouse, DateTime cutoff, CancellationToken ct)
    {
        var groups = await db.ErrorGroups
            .Where(g => g.UpdatedAt >= cutoff)
            .OrderBy(g => g.Id)
            .Take(BatchSize)
            .ToListAsync(ct);

        if (groups.Count == 0)
            return 0;

        var rows = groups.Select(g => new ErrorGroupRowInput
        {
            ProjectId = g.ProjectId,
            ErrorGroupId = g.Id,
            SecureId = g.SecureId,
            CreatedAt = g.CreatedAt,
            UpdatedAt = g.UpdatedAt,
            Event = g.Event,
            Type = g.Type,
            State = g.State.ToString(),
            ServiceName = g.ServiceName,
            Environments = g.Environments,
        });

        await clickHouse.WriteErrorGroupsAsync(rows, ct);
        return groups.Count;
    }

    /// <summary>
    /// Sync error objects created after <paramref name="cutoff"/> to ClickHouse.
    /// </summary>
    /// <param name="db">PostgreSQL database context.</param>
    /// <param name="clickHouse">ClickHouse write service.</param>
    /// <param name="cutoff">Only sync error objects created on or after this time.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Number of error objects synced.</returns>
    internal static async Task<int> SyncErrorObjectsAsync(
        HoldFastDbContext db, IClickHouseService clickHouse, DateTime cutoff, CancellationToken ct)
    {
        var objects = await db.ErrorObjects
            .Where(e => e.CreatedAt >= cutoff)
            .OrderBy(e => e.Id)
            .Take(BatchSize)
            .ToListAsync(ct);

        if (objects.Count == 0)
            return 0;

        var rows = objects.Select(e => new ErrorObjectRowInput
        {
            ProjectId = e.ProjectId,
            ErrorObjectId = e.Id,
            ErrorGroupId = e.ErrorGroupId,
            Timestamp = e.Timestamp,
            Event = e.Event,
            Type = e.Type,
            Url = e.Url,
            Environment = e.Environment,
            OS = e.OS,
            Browser = e.Browser,
            ServiceName = e.ServiceName,
            ServiceVersion = e.ServiceVersion,
        });

        await clickHouse.WriteErrorObjectsAsync(rows, ct);
        return objects.Count;
    }
}
