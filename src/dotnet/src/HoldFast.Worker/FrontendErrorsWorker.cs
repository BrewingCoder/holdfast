using System.Text.Json;
using HoldFast.Data;
using HoldFast.Data.ClickHouse;
using HoldFast.Data.ClickHouse.Models;
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
/// Kafka message for frontend errors extracted from a SDK pushPayload.
/// One message per error in the original payload.errors[] list.
///
/// Project ID is resolved on the consumer side from the SessionSecureId — the
/// SDK's pushPayload doesn't carry an explicit project_id (it's implicit
/// through the session).
/// </summary>
public record FrontendErrorMessage(
    string SessionSecureId,
    string Event,
    string Type,
    string Url,
    string Source,
    int LineNumber,
    int ColumnNumber,
    // StackTrace is the JSON-serialized list of frames (matches the format
    // IErrorGroupingService.GroupErrorAsync expects).
    string StackTrace,
    DateTime Timestamp,
    string? Payload);

/// <summary>
/// Consumes frontend errors from the SDK pushPayload pipeline and groups them
/// into ClickHouse error_objects + postgres error_groups via the existing
/// IErrorGroupingService. Mirrors how ErrorGroupingConsumer handles backend
/// errors, but resolves projectId via the session.
/// </summary>
public class FrontendErrorsConsumer : KafkaConsumerService<FrontendErrorMessage>
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<FrontendErrorsConsumer> _logger;

    public FrontendErrorsConsumer(
        IOptions<KafkaOptions> options,
        IServiceScopeFactory scopeFactory,
        ILogger<FrontendErrorsConsumer> logger)
        : base(options, KafkaTopics.FrontendErrors, "frontend-errors-worker", logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ProcessAsync(string key, FrontendErrorMessage value, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HoldFastDbContext>();
        var groupingService = scope.ServiceProvider.GetRequiredService<IErrorGroupingService>();

        // Frontend errors carry session_secure_id, not project_id — look the
        // session up to find the project this error belongs to.
        var session = await db.Sessions
            .FirstOrDefaultAsync(s => s.SecureId == value.SessionSecureId, ct);
        if (session is null)
        {
            _logger.LogWarning(
                "Frontend error references unknown session {SecureId} — skipping",
                value.SessionSecureId);
            return;
        }

        var result = await groupingService.GroupErrorAsync(
            session.ProjectId,
            value.Event,
            value.Type,
            value.StackTrace,
            value.Timestamp,
            value.Url,
            value.Source,
            value.Payload,
            environment: null,
            serviceName: null,
            serviceVersion: null,
            sessionId: session.Id,
            traceExternalId: null,
            spanId: null,
            ct);

        _logger.LogInformation(
            "Frontend error grouped: GroupId={GroupId}, IsNew={IsNew}, Event={Event}, Session={Session}",
            result.ErrorGroup.Id, result.IsNewGroup, value.Event, value.SessionSecureId);

        // Mirror the row to ClickHouse error_objects so dashboard analytics can
        // query it. Postgres holds the relational error_groups + error_objects;
        // ClickHouse is the analytics surface for time-series error queries.
        var clickhouse = scope.ServiceProvider.GetRequiredService<IClickHouseService>();
        await clickhouse.WriteErrorObjectsAsync(
            [new ErrorObjectRowInput
            {
                ProjectId = session.ProjectId,
                ErrorObjectId = (int)result.ErrorObject.Id,
                ErrorGroupId = (int)result.ErrorGroup.Id,
                Timestamp = value.Timestamp,
                Event = value.Event,
                Type = value.Type,
                Url = value.Url,
                Environment = null,
                OS = null,
                Browser = null,
                ServiceName = null,
                ServiceVersion = null,
            }],
            ct);

        // Match ErrorGroupingConsumer: evaluate alerts inline so newly-grouped
        // errors trigger any subscribed alerts immediately.
        var alertService = scope.ServiceProvider.GetRequiredService<IAlertEvaluationService>();
        await alertService.EvaluateErrorAlertsAsync(
            session.ProjectId, result.ErrorGroup, result.ErrorObject, ct);
    }
}

public class FrontendErrorsWorker : BackgroundService
{
    private readonly FrontendErrorsConsumer _consumer;

    public FrontendErrorsWorker(FrontendErrorsConsumer consumer) => _consumer = consumer;

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        _consumer.ConsumeLoopAsync(stoppingToken);
}
