using HoldFast.Data;
using HoldFast.Domain.Entities;
using HoldFast.Shared.AlertEvaluation;
using HoldFast.Shared.ErrorGrouping;
using HoldFast.Shared.Kafka;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HoldFast.Worker;

/// <summary>
/// Kafka message for backend errors that need grouping.
/// </summary>
public record BackendErrorMessage(
    string? ProjectId,
    string Event,
    string Type,
    string Url,
    string Source,
    string StackTrace,
    DateTime Timestamp,
    string? Payload,
    string ServiceName,
    string ServiceVersion,
    string Environment,
    string? SessionSecureId,
    string? TraceId,
    string? SpanId);

/// <summary>
/// Consumes backend errors from Kafka, groups them, and stores to database.
/// Replaces the Go worker's processBackendPayloadImpl handler.
/// </summary>
public class ErrorGroupingConsumer : KafkaConsumerService<BackendErrorMessage>
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ErrorGroupingConsumer> _logger;

    public ErrorGroupingConsumer(
        IOptions<KafkaOptions> options,
        IServiceScopeFactory scopeFactory,
        ILogger<ErrorGroupingConsumer> logger)
        : base(options, KafkaTopics.BackendErrors, "error-grouping-worker", logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ProcessAsync(string key, BackendErrorMessage value, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HoldFastDbContext>();
        var groupingService = scope.ServiceProvider.GetRequiredService<IErrorGroupingService>();

        _logger.LogDebug("Processing backend error: {Type} - {Event}", value.Type, value.Event);

        // Resolve project ID
        int projectId;
        if (!string.IsNullOrEmpty(value.ProjectId) && int.TryParse(value.ProjectId, out var pid))
        {
            projectId = pid;
        }
        else
        {
            _logger.LogWarning("Backend error has no valid ProjectId, skipping");
            return;
        }

        // Resolve session if present
        int? sessionId = null;
        if (!string.IsNullOrEmpty(value.SessionSecureId))
        {
            var session = await db.Sessions
                .FirstOrDefaultAsync(s => s.SecureId == value.SessionSecureId, ct);
            sessionId = session?.Id;
        }

        var result = await groupingService.GroupErrorAsync(
            projectId,
            value.Event,
            value.Type,
            value.StackTrace,
            value.Timestamp,
            value.Url,
            value.Source,
            value.Payload,
            value.Environment,
            value.ServiceName,
            value.ServiceVersion,
            sessionId,
            value.TraceId,
            value.SpanId,
            ct);

        _logger.LogInformation(
            "Error grouped: GroupId={GroupId}, IsNew={IsNew}, Event={Event}",
            result.ErrorGroup.Id, result.IsNewGroup, value.Event);

        // Evaluate alerts inline after grouping (matches Go behavior)
        var alertService = scope.ServiceProvider.GetRequiredService<IAlertEvaluationService>();
        var alertResult = await alertService.EvaluateErrorAlertsAsync(
            projectId, result.ErrorGroup, result.ErrorObject, ct);

        if (alertResult.AlertsTriggered > 0)
        {
            _logger.LogInformation(
                "Alerts triggered: {Triggered}/{Evaluated} for error group {GroupId}",
                alertResult.AlertsTriggered, alertResult.AlertsEvaluated, result.ErrorGroup.Id);
        }
    }
}

/// <summary>
/// BackgroundService that runs the error grouping consumer.
/// </summary>
public class ErrorGroupingWorker : BackgroundService
{
    private readonly ErrorGroupingConsumer _consumer;

    public ErrorGroupingWorker(ErrorGroupingConsumer consumer)
    {
        _consumer = consumer;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        return _consumer.ConsumeLoopAsync(stoppingToken);
    }
}
