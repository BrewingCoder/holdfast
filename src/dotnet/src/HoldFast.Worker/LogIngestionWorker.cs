using HoldFast.Data.ClickHouse;
using HoldFast.Data.ClickHouse.Models;
using HoldFast.Shared.Kafka;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HoldFast.Worker;

/// <summary>
/// Kafka message for log rows from OTeL ingestion.
/// </summary>
public record LogIngestionMessage(
    int ProjectId,
    DateTime Timestamp,
    string TraceId,
    string SpanId,
    string SecureSessionId,
    string SeverityText,
    int SeverityNumber,
    string Source,
    string ServiceName,
    string ServiceVersion,
    string Body,
    Dictionary<string, string>? LogAttributes,
    string Environment);

/// <summary>
/// Consumes log rows from Kafka and writes to ClickHouse.
/// </summary>
public class LogIngestionConsumer : KafkaConsumerService<LogIngestionMessage>
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<LogIngestionConsumer> _logger;

    public LogIngestionConsumer(
        IOptions<KafkaOptions> options,
        IServiceScopeFactory scopeFactory,
        ILogger<LogIngestionConsumer> logger)
        : base(options, KafkaTopics.Logs, "log-ingestion-worker", logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ProcessAsync(string key, LogIngestionMessage value, CancellationToken ct)
    {
        _logger.LogDebug("Processing log: {Severity} from {Service}", value.SeverityText, value.ServiceName);

        using var scope = _scopeFactory.CreateScope();
        var clickHouse = scope.ServiceProvider.GetRequiredService<IClickHouseService>();

        var logRow = new LogRowInput
        {
            ProjectId = value.ProjectId,
            Timestamp = value.Timestamp,
            TraceId = value.TraceId,
            SpanId = value.SpanId,
            SecureSessionId = value.SecureSessionId,
            SeverityText = value.SeverityText,
            SeverityNumber = value.SeverityNumber,
            Source = value.Source,
            ServiceName = value.ServiceName,
            ServiceVersion = value.ServiceVersion,
            Body = value.Body,
            LogAttributes = value.LogAttributes ?? new(),
            Environment = value.Environment,
        };

        await clickHouse.WriteLogsAsync([logRow], ct);
    }
}

/// <summary>
/// BackgroundService that runs the log ingestion consumer.
/// </summary>
public class LogIngestionWorker : BackgroundService
{
    private readonly LogIngestionConsumer _consumer;

    public LogIngestionWorker(LogIngestionConsumer consumer)
    {
        _consumer = consumer;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        return _consumer.ConsumeLoopAsync(stoppingToken);
    }
}
