using HoldFast.Data;
using HoldFast.Domain.Entities;
using HoldFast.Shared.Kafka;
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

        _logger.LogDebug("Processing backend error: {Type} - {Event}", value.Type, value.Event);

        // TODO Phase 3: Match error to existing group via fingerprint/embeddings,
        // create ErrorObject, update ErrorGroup counts, evaluate alerts
        await Task.CompletedTask;
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
