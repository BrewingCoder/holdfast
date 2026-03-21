using HoldFast.Shared.Kafka;
using Xunit;

namespace HoldFast.Shared.Tests.Kafka;

/// <summary>
/// Tests for KafkaOptions configuration defaults and overrides.
/// </summary>
public class KafkaOptionsTests
{
    [Fact]
    public void KafkaOptions_DefaultBootstrapServers()
    {
        var options = new KafkaOptions();
        Assert.Equal("localhost:9092", options.BootstrapServers);
    }

    [Fact]
    public void KafkaOptions_SetBootstrapServers()
    {
        var options = new KafkaOptions { BootstrapServers = "kafka1:9092,kafka2:9092" };
        Assert.Equal("kafka1:9092,kafka2:9092", options.BootstrapServers);
    }

    [Fact]
    public void KafkaOptions_EmptyBootstrapServers()
    {
        var options = new KafkaOptions { BootstrapServers = "" };
        Assert.Equal("", options.BootstrapServers);
    }

    [Fact]
    public void KafkaOptions_MultipleServers()
    {
        var options = new KafkaOptions { BootstrapServers = "host1:9092,host2:9092,host3:9092" };
        var servers = options.BootstrapServers.Split(',');
        Assert.Equal(3, servers.Length);
    }
}
