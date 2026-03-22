using HoldFast.Data.ClickHouse;
using HoldFast.Data.ClickHouse.Models;
using HoldFast.Shared.Kafka;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HoldFast.Worker;

/// <summary>
/// Kafka message for trace span rows from OTeL ingestion.
/// </summary>
public record TraceIngestionMessage(
    int ProjectId,
    DateTime Timestamp,
    string TraceId,
    string SpanId,
    string ParentSpanId,
    string SecureSessionId,
    string ServiceName,
    string ServiceVersion,
    string Environment,
    string SpanName,
    string SpanKind,
    long Duration,
    string StatusCode,
    string StatusMessage,
    Dictionary<string, string>? TraceAttributes,
    bool HasErrors);

/// <summary>
/// Consumes trace spans from Kafka and writes to ClickHouse.
/// </summary>
public class TraceIngestionConsumer : KafkaConsumerService<TraceIngestionMessage>
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TraceIngestionConsumer> _logger;

    public TraceIngestionConsumer(
        IOptions<KafkaOptions> options,
        IServiceScopeFactory scopeFactory,
        ILogger<TraceIngestionConsumer> logger)
        : base(options, KafkaTopics.Traces, "trace-ingestion-worker", logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ProcessAsync(string key, TraceIngestionMessage value, CancellationToken ct)
    {
        _logger.LogDebug("Processing trace span: {SpanName} ({TraceId})", value.SpanName, value.TraceId);

        using var scope = _scopeFactory.CreateScope();
        var clickHouse = scope.ServiceProvider.GetRequiredService<IClickHouseService>();

        var traceRow = new TraceRowInput
        {
            ProjectId = value.ProjectId,
            Timestamp = value.Timestamp,
            TraceId = value.TraceId,
            SpanId = value.SpanId,
            ParentSpanId = value.ParentSpanId,
            SecureSessionId = value.SecureSessionId,
            ServiceName = value.ServiceName,
            ServiceVersion = value.ServiceVersion,
            Environment = value.Environment,
            SpanName = value.SpanName,
            SpanKind = value.SpanKind,
            Duration = value.Duration,
            StatusCode = value.StatusCode,
            StatusMessage = value.StatusMessage,
            TraceAttributes = value.TraceAttributes ?? new(),
            HasErrors = value.HasErrors,
        };

        await clickHouse.WriteTracesAsync([traceRow], ct);
    }
}

/// <summary>
/// BackgroundService that runs the trace ingestion consumer.
/// </summary>
public class TraceIngestionWorker : BackgroundService
{
    private readonly TraceIngestionConsumer _consumer;

    public TraceIngestionWorker(TraceIngestionConsumer consumer)
    {
        _consumer = consumer;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        return _consumer.ConsumeLoopAsync(stoppingToken);
    }
}
