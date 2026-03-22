using HoldFast.Data;
using HoldFast.Data.ClickHouse;
using HoldFast.Data.ClickHouse.Models;
using HoldFast.Domain.Entities;
using HoldFast.Shared.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HoldFast.Worker;

/// <summary>
/// Background worker that evaluates log alert thresholds on a configurable schedule.
///
/// Algorithm (mirrors Go's WatchLogAlerts / processLogAlert):
/// 1. Every 60 seconds, reload all enabled LogAlerts from PostgreSQL.
/// 2. Every 15 seconds, evaluate each alert whose frequency bucket has elapsed:
///    - Count logs in ClickHouse matching alert.Query within the threshold window.
///    - If count >= CountThreshold (or <= when BelowThreshold), fire a notification.
///    - Record the event in LogAlertEvent for deduplication.
/// </summary>
public class LogAlertWatcherWorker : BackgroundService
{
    internal static readonly TimeSpan EvalInterval = TimeSpan.FromSeconds(15);
    internal static readonly TimeSpan ReloadInterval = TimeSpan.FromMinutes(1);

    // Go's ingestDelay: offset so we look at fully ingested data
    private static readonly TimeSpan IngestDelay = TimeSpan.FromMinutes(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<LogAlertWatcherWorker> _logger;

    public LogAlertWatcherWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<LogAlertWatcherWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("LogAlertWatcherWorker started");

        var alerts = new List<LogAlert>();
        var lastReload = DateTime.MinValue;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (DateTime.UtcNow - lastReload >= ReloadInterval)
                {
                    alerts = await LoadAlertsAsync(stoppingToken);
                    lastReload = DateTime.UtcNow;
                    _logger.LogDebug("Loaded {Count} log alerts", alerts.Count);
                }

                foreach (var alert in alerts)
                {
                    _ = Task.Run(() => EvaluateAlertAsync(alert, stoppingToken), stoppingToken);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "LogAlertWatcherWorker iteration failed");
            }

            await Task.Delay(EvalInterval, stoppingToken);
        }
    }

    private async Task<List<LogAlert>> LoadAlertsAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HoldFastDbContext>();
        return await db.Set<LogAlert>()
            .Where(a => !a.Disabled)
            .ToListAsync(ct);
    }

    internal async Task EvaluateAlertAsync(LogAlert alert, CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<HoldFastDbContext>();
            var clickHouse = scope.ServiceProvider.GetRequiredService<IClickHouseService>();
            var notifications = scope.ServiceProvider.GetRequiredService<INotificationService>();

            var thresholdWindow = alert.ThresholdWindow ?? alert.Frequency ?? 300; // default 5 min
            var end = DateTime.UtcNow - IngestDelay;
            var start = end - TimeSpan.FromSeconds(thresholdWindow);

            var count = await clickHouse.CountLogsAsync(
                alert.ProjectId, alert.Query, start, end, ct);

            var countThreshold = alert.CountThreshold ?? 0;
            var belowThreshold = alert.BelowThreshold.HasValue && alert.BelowThreshold.Value > 0;
            var alertCondition = belowThreshold
                ? count <= countThreshold
                : count >= countThreshold;

            _logger.LogDebug(
                "LogAlert {Id}: count={Count} threshold={Threshold} alerting={Alerting}",
                alert.Id, count, countThreshold, alertCondition);

            if (!alertCondition)
                return;

            // Fire notification via webhook destinations
            await FireNotificationsAsync(alert, count, start, end, db, notifications, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Error evaluating log alert {AlertId}", alert.Id);
        }
    }

    private async Task FireNotificationsAsync(
        LogAlert alert, long count, DateTime start, DateTime end,
        HoldFastDbContext db, INotificationService notifications, CancellationToken ct)
    {
        _logger.LogInformation(
            "Firing log alert {AlertId} ({Name}): count={Count}", alert.Id, alert.Name, count);

        // Record the event for deduplication
        db.Set<LogAlertEvent>().Add(new LogAlertEvent
        {
            LogAlertId = alert.Id,
            Query = alert.Query,
            Count = (int)Math.Min(count, int.MaxValue),
        });
        await db.SaveChangesAsync(ct);

        // Send webhook notifications
        if (!string.IsNullOrEmpty(alert.WebhookDestinations))
        {
            var payload = new
            {
                alert_id = alert.Id,
                alert_name = alert.Name,
                project_id = alert.ProjectId,
                query = alert.Query,
                count,
                threshold = alert.CountThreshold,
                start_date = start,
                end_date = end,
            };
            await notifications.SendWebhookAsync(alert.WebhookDestinations, payload, ct);
        }
    }
}
