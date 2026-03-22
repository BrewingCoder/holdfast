using System.Text.Json;
using Confluent.Kafka;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HoldFast.Shared.Kafka;

/// <summary>
/// Configuration for Kafka connections. BootstrapServers is comma-separated broker addresses.
/// </summary>
public class KafkaOptions
{
    public string BootstrapServers { get; set; } = "localhost:9092";
}

/// <summary>
/// Confluent.Kafka producer wrapper. Produces JSON-serialized messages.
/// </summary>
public class KafkaProducerService : IDisposable
{
    private readonly IProducer<string, string> _producer;
    private readonly ILogger<KafkaProducerService> _logger;

    public KafkaProducerService(IOptions<KafkaOptions> options, ILogger<KafkaProducerService> logger)
    {
        _logger = logger;
        var config = new ProducerConfig
        {
            BootstrapServers = options.Value.BootstrapServers,
            Acks = Acks.All,
            EnableIdempotence = true,
            LingerMs = 5,
            BatchNumMessages = 1000,
        };
        _producer = new ProducerBuilder<string, string>(config).Build();
    }

    public async Task ProduceAsync<T>(string topic, string key, T value, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(value);
        var message = new Message<string, string> { Key = key, Value = json };

        try
        {
            await _producer.ProduceAsync(topic, message, ct);
        }
        catch (ProduceException<string, string> ex)
        {
            _logger.LogError(ex, "Failed to produce message to {Topic} with key {Key}", topic, key);
            throw;
        }
    }

    public void Dispose()
    {
        _producer.Flush(TimeSpan.FromSeconds(5));
        _producer.Dispose();
    }
}
