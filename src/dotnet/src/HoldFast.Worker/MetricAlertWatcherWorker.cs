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
/// Background worker that evaluates metric/unified Alert thresholds every minute.
///
/// Mirrors Go's WatchMetricAlerts / processMetricAlert (simplified — no anomaly detection,
/// no saved metric states). Handles the common constant-threshold case for all product types:
/// Sessions, Errors, Logs, Traces, Metrics, Events.
///
/// Algorithm:
/// 1. Load all enabled Alert records from PostgreSQL.
/// 2. For each alert, query ClickHouse metrics for the threshold window.
/// 3. Compare the aggregated value against the threshold.
/// 4. If alerting and cooldown has elapsed, fire notifications and write state change.
/// </summary>
public class MetricAlertWatcherWorker : BackgroundService
{
    internal static readonly TimeSpan EvalInterval = TimeSpan.FromMinutes(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<MetricAlertWatcherWorker> _logger;

    public MetricAlertWatcherWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<MetricAlertWatcherWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("MetricAlertWatcherWorker started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunEvaluationCycleAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "MetricAlertWatcherWorker iteration failed");
            }

            await Task.Delay(EvalInterval, stoppingToken);
        }
    }

    internal async Task RunEvaluationCycleAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HoldFastDbContext>();
        var clickHouse = scope.ServiceProvider.GetRequiredService<IClickHouseService>();
        var notifications = scope.ServiceProvider.GetRequiredService<INotificationService>();

        var alerts = await db.Set<Alert>()
            .Where(a => !a.Disabled)
            .ToListAsync(ct);

        _logger.LogDebug("Evaluating {Count} metric alerts", alerts.Count);

        foreach (var alert in alerts)
        {
            _ = Task.Run(async () =>
            {
                try { await EvaluateAlertAsync(alert, db, clickHouse, notifications, ct); }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Error evaluating metric alert {AlertId}", alert.Id);
                }
            }, ct);
        }
    }

    internal async Task EvaluateAlertAsync(
        Alert alert,
        HoldFastDbContext db,
        IClickHouseService clickHouse,
        INotificationService notifications,
        CancellationToken ct)
    {
        var curDate = DateTime.UtcNow.AddSeconds(-60); // 1-minute lag matches Go
        var thresholdWindow = TimeSpan.FromSeconds(alert.ThresholdWindow ?? 3600);
        var startDate = curDate - thresholdWindow;
        var endDate = curDate;

        // Check cooldown: if already alerting within cooldown window, suppress
        var cooldown = TimeSpan.FromSeconds(alert.ThresholdCooldown ?? 0);
        if (cooldown > TimeSpan.Zero)
        {
            var recentAlerts = await clickHouse.GetLastAlertingStatesAsync(
                alert.ProjectId, alert.Id,
                curDate - cooldown, curDate, ct);

            if (recentAlerts.Any(s => s.State == "Alerting"))
            {
                _logger.LogDebug("Alert {AlertId} in cooldown, skipping", alert.Id);
                return;
            }
        }

        // Query ClickHouse for the metric value
        double metricValue;
        try
        {
            metricValue = await GetMetricValueAsync(alert, clickHouse, startDate, endDate, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to query metric for alert {AlertId}", alert.Id);
            return;
        }

        var thresholdValue = alert.ThresholdValue ?? alert.AboveThreshold ?? 0;
        var alertCondition = alert.BelowThreshold.HasValue && alert.BelowThreshold.Value > 0
            ? metricValue <= thresholdValue
            : metricValue >= thresholdValue;

        var state = alertCondition ? "Alerting" : "Normal";

        _logger.LogDebug(
            "MetricAlert {Id} ({ProductType}): value={Value} threshold={Threshold} state={State}",
            alert.Id, alert.ProductType, metricValue, thresholdValue, state);

        // Write state change to ClickHouse
        await clickHouse.WriteAlertStateChangesAsync(alert.ProjectId,
        [
            new AlertStateChangeRow
            {
                ProjectId = alert.ProjectId,
                AlertId = alert.Id,
                Timestamp = curDate,
                State = state,
                GroupByKey = alert.GroupByKey ?? "",
            }
        ], ct);

        if (alertCondition)
        {
            await FireNotificationsAsync(alert, metricValue, db, notifications, ct);
        }
    }

    private async Task<double> GetMetricValueAsync(
        Alert alert, IClickHouseService clickHouse,
        DateTime startDate, DateTime endDate, CancellationToken ct)
    {
        // For log alerts via the unified Alert type, use log count
        if (alert.ProductType.Equals("LOGS", StringComparison.OrdinalIgnoreCase) ||
            alert.ProductType.Equals("logs", StringComparison.OrdinalIgnoreCase))
        {
            var count = await clickHouse.CountLogsAsync(
                alert.ProjectId, alert.Query, startDate, endDate, ct);
            return (double)count;
        }

        // For other product types, use the metrics query
        var buckets = await clickHouse.ReadMetricsAsync(
            alert.ProjectId,
            new HoldFast.Data.ClickHouse.Models.QueryInput
            {
                Query = alert.Query ?? "",
                DateRangeStart = startDate,
                DateRangeEnd = endDate,
            },
            bucketBy: "timestamp",
            groupBy: alert.GroupByKey != null ? [alert.GroupByKey] : null,
            aggregator: alert.FunctionType ?? "Count",
            column: alert.FunctionColumn,
            ct);

        // Return the average metric value across all buckets
        var values = buckets.Buckets.Where(b => b.MetricValue.HasValue)
            .Select(b => b.MetricValue!.Value).ToList();
        return values.Count > 0 ? values.Average() : 0;
    }

    private async Task FireNotificationsAsync(
        Alert alert, double value,
        HoldFastDbContext db, INotificationService notifications, CancellationToken ct)
    {
        _logger.LogInformation(
            "Firing metric alert {AlertId} ({Name}): value={Value}",
            alert.Id, alert.Name, value);

        // Send to configured webhook destinations
        var destinations = await db.Set<AlertDestination>()
            .Where(d => d.AlertId == alert.Id)
            .ToListAsync(ct);

        foreach (var dest in destinations)
        {
            if (dest.DestinationType.Equals("webhook", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrEmpty(dest.TypeId))
            {
                var payload = new
                {
                    alert_id = alert.Id,
                    alert_name = alert.Name,
                    project_id = alert.ProjectId,
                    product_type = alert.ProductType,
                    value,
                    threshold = alert.ThresholdValue ?? alert.AboveThreshold,
                };
                await notifications.SendWebhookAsync(dest.TypeId, payload, ct);
            }
        }
    }
}
