using HoldFast.Shared.Kafka;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HoldFast.Worker;

/// <summary>
/// Kafka message for session events (replay data, errors, resources).
/// </summary>
public record SessionEventsMessage(
    string SessionSecureId,
    long PayloadId,
    string Data);

/// <summary>
/// Consumes session events from Kafka and processes them.
/// Replaces the Go worker's processPublicWorkerMessage handler.
/// </summary>
public class SessionEventsConsumer : KafkaConsumerService<SessionEventsMessage>
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SessionEventsConsumer> _logger;

    public SessionEventsConsumer(
        IOptions<KafkaOptions> options,
        IServiceScopeFactory scopeFactory,
        ILogger<SessionEventsConsumer> logger)
        : base(options, KafkaTopics.SessionEvents, "session-events-worker", logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ProcessAsync(string key, SessionEventsMessage value, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var processor = scope.ServiceProvider.GetRequiredService<ISessionEventsProcessor>();

        _logger.LogDebug(
            "Processing session events for {SecureId}, payload {PayloadId}",
            value.SessionSecureId, value.PayloadId);

        var result = await processor.ProcessCompressedPayloadAsync(
            value.SessionSecureId, value.PayloadId, value.Data, ct);

        if (result.SessionId > 0)
        {
            _logger.LogInformation(
                "Session {SessionId}: {Chunks} chunks, {Bytes} bytes",
                result.SessionId, result.ChunksCreated, result.TotalBytes);
        }
    }
}

/// <summary>
/// BackgroundService that runs the session events consumer.
/// </summary>
public class SessionEventsWorker : BackgroundService
{
    private readonly SessionEventsConsumer _consumer;

    public SessionEventsWorker(SessionEventsConsumer consumer)
    {
        _consumer = consumer;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        return _consumer.ConsumeLoopAsync(stoppingToken);
    }
}
