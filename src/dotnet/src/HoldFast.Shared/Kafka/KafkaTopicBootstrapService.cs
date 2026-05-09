using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HoldFast.Shared.Kafka;

/// <summary>
/// Configuration for the Kafka topic bootstrap runner.
/// </summary>
public class KafkaTopicBootstrapOptions
{
    /// <summary>
    /// Number of partitions for newly-created topics. Hobby/dev defaults to 1;
    /// production deployments should set this higher (typically 3-12 depending
    /// on consumer parallelism).
    /// </summary>
    public int Partitions { get; set; } = 1;

    /// <summary>
    /// Replication factor for newly-created topics. Hobby/dev defaults to 1
    /// since the cluster has a single broker; production should be 3.
    /// </summary>
    public short ReplicationFactor { get; set; } = 1;

    /// <summary>
    /// Timeout for the create-topics admin call.
    /// </summary>
    public TimeSpan CreateTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Skip topic creation. Use when topics are managed externally (Helm
    /// chart pre-job, Strimzi KafkaTopic resources, ops automation).
    /// </summary>
    public bool Disabled { get; set; }
}

/// <summary>
/// Pre-creates the Kafka topics consumers will subscribe to, on backend startup.
///
/// Why: Confluent.Kafka's auto-create only triggers on producer writes, not on
/// consumer subscribe. With the .NET backend's default `BackgroundServiceException
/// Behavior = StopHost`, a single consumer failing to subscribe to a missing
/// topic kills the entire process. Net effect on a fresh stack: backend starts,
/// SessionEventsConsumer fails to subscribe to "session-events", host stops,
/// container restarts, repeat forever. Pre-creating topics breaks the loop.
///
/// See HOL-12.
/// </summary>
public class KafkaTopicBootstrapService : IHostedService
{
    /// <summary>
    /// Topics required by the backend's worker hosted services. Order doesn't
    /// matter; the admin call is batched.
    /// </summary>
    private static readonly string[] RequiredTopics =
    [
        KafkaTopics.SessionEvents,
        KafkaTopics.BackendErrors,
        KafkaTopics.FrontendErrors,
        KafkaTopics.Metrics,
        KafkaTopics.Logs,
        KafkaTopics.Traces,
    ];

    private readonly KafkaOptions _kafka;
    private readonly KafkaTopicBootstrapOptions _options;
    private readonly ILogger<KafkaTopicBootstrapService> _logger;

    public KafkaTopicBootstrapService(
        IOptions<KafkaOptions> kafka,
        IOptions<KafkaTopicBootstrapOptions> options,
        ILogger<KafkaTopicBootstrapService> logger)
    {
        _kafka = kafka.Value;
        _options = options.Value;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (_options.Disabled)
        {
            _logger.LogInformation("Kafka topic bootstrap: disabled by configuration, skipping");
            return;
        }

        var config = new AdminClientConfig { BootstrapServers = _kafka.BootstrapServers };
        using var admin = new AdminClientBuilder(config).Build();

        // Find which topics actually exist so we only create the missing ones —
        // CreateTopicsAsync returns one Error per topic, but plumbing it through
        // is awkward; querying first keeps the log output clean.
        var meta = admin.GetMetadata(TimeSpan.FromSeconds(15));
        var existing = meta.Topics
            .Where(t => t.Error.Code == ErrorCode.NoError)
            .Select(t => t.Topic)
            .ToHashSet();

        var missing = RequiredTopics.Where(t => !existing.Contains(t)).ToList();
        if (missing.Count == 0)
        {
            _logger.LogInformation("Kafka topic bootstrap: all {Count} topics already exist",
                RequiredTopics.Length);
            return;
        }

        var specs = missing.Select(name => new TopicSpecification
        {
            Name = name,
            NumPartitions = _options.Partitions,
            ReplicationFactor = _options.ReplicationFactor,
        }).ToList();

        try
        {
            await admin.CreateTopicsAsync(specs, new CreateTopicsOptions
            {
                OperationTimeout = _options.CreateTimeout,
            });
            _logger.LogInformation(
                "Kafka topic bootstrap: created {Created} of {Required} topics ({Names})",
                missing.Count, RequiredTopics.Length, string.Join(", ", missing));
        }
        catch (CreateTopicsException ex)
        {
            // Distinguish "raced with another instance / hobby restart" from real failures
            var failed = ex.Results
                .Where(r => r.Error.Code != ErrorCode.NoError &&
                            r.Error.Code != ErrorCode.TopicAlreadyExists)
                .ToList();
            if (failed.Count > 0)
            {
                foreach (var f in failed)
                    _logger.LogError(
                        "Kafka topic bootstrap: failed to create {Topic} — {Error}",
                        f.Topic, f.Error.Reason);
                throw;
            }
            _logger.LogInformation(
                "Kafka topic bootstrap: {Existed} of {Created} topics already existed (race-safe)",
                ex.Results.Count(r => r.Error.Code == ErrorCode.TopicAlreadyExists),
                missing.Count);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
