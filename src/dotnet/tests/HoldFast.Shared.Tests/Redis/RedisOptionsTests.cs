using HoldFast.Shared.Redis;
using Xunit;

namespace HoldFast.Shared.Tests.Redis;

/// <summary>
/// Tests for RedisOptions configuration defaults.
/// RedisService itself requires a running Redis instance,
/// so we test the configuration layer only.
/// </summary>
public class RedisOptionsTests
{
    [Fact]
    public void RedisOptions_DefaultConfiguration()
    {
        var options = new RedisOptions();
        Assert.Equal("localhost:6379", options.Configuration);
    }

    [Fact]
    public void RedisOptions_SetConfiguration()
    {
        var options = new RedisOptions { Configuration = "redis.internal:6380" };
        Assert.Equal("redis.internal:6380", options.Configuration);
    }

    [Fact]
    public void RedisOptions_EmptyConfiguration()
    {
        var options = new RedisOptions { Configuration = "" };
        Assert.Equal("", options.Configuration);
    }

    [Fact]
    public void RedisOptions_ClusterConfiguration()
    {
        var options = new RedisOptions
        {
            Configuration = "redis1:6379,redis2:6379,redis3:6379"
        };
        Assert.Contains("redis1", options.Configuration);
        Assert.Contains("redis3", options.Configuration);
    }

    [Fact]
    public void RedisOptions_WithPassword()
    {
        var options = new RedisOptions
        {
            Configuration = "redis.internal:6379,password=secret"
        };
        Assert.Contains("password=", options.Configuration);
    }

    [Fact]
    public void RedisOptions_SentinelConfiguration()
    {
        var options = new RedisOptions
        {
            Configuration = "sentinel1:26379,sentinel2:26379,serviceName=mymaster"
        };
        Assert.Contains("serviceName=", options.Configuration);
    }
}
