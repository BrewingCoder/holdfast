using HoldFast.Shared.Runtime;
using Xunit;

namespace HoldFast.Api.Tests;

/// <summary>
/// Tests for Go-to-.NET environment variable mapping.
/// Ensures the same .env file used by the Go backend works with the .NET backend.
/// </summary>
public class GoEnvCompatTests
{
    // ══════════════════════════════════════════════════════════════════
    // BuildPostgresConnectionString
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void BuildPostgres_AllVarsSet()
    {
        var env = new Dictionary<string, string?>
        {
            ["PSQL_HOST"] = "db.example.com",
            ["PSQL_PORT"] = "5433",
            ["PSQL_USER"] = "admin",
            ["PSQL_PASSWORD"] = "secret",
            ["PSQL_DB"] = "holdfast",
            ["PSQL_DOCKER_HOST"] = null,
        };

        var result = GoEnvCompat.BuildPostgresConnectionString(k => env.GetValueOrDefault(k));

        Assert.Equal("Host=db.example.com;Port=5433;Database=holdfast;Username=admin;Password=secret", result);
    }

    [Fact]
    public void BuildPostgres_DockerHostTakesPrecedence()
    {
        var env = new Dictionary<string, string?>
        {
            ["PSQL_HOST"] = "external-host",
            ["PSQL_DOCKER_HOST"] = "postgres",
            ["PSQL_PORT"] = "5432",
            ["PSQL_USER"] = "postgres",
            ["PSQL_PASSWORD"] = "",
            ["PSQL_DB"] = "postgres",
        };

        var result = GoEnvCompat.BuildPostgresConnectionString(k => env.GetValueOrDefault(k));

        Assert.Contains("Host=postgres", result);
        Assert.DoesNotContain("external-host", result);
    }

    [Fact]
    public void BuildPostgres_Defaults_WhenOnlyHostSet()
    {
        var env = new Dictionary<string, string?>
        {
            ["PSQL_HOST"] = "myhost",
        };

        var result = GoEnvCompat.BuildPostgresConnectionString(k => env.GetValueOrDefault(k));

        Assert.Equal("Host=myhost;Port=5432;Database=postgres;Username=postgres;Password=", result);
    }

    [Fact]
    public void BuildPostgres_ReturnsNull_WhenNoHostSet()
    {
        var result = GoEnvCompat.BuildPostgresConnectionString(_ => null);

        Assert.Null(result);
    }

    [Fact]
    public void BuildPostgres_PasswordWithSpecialChars()
    {
        var env = new Dictionary<string, string?>
        {
            ["PSQL_HOST"] = "localhost",
            ["PSQL_PASSWORD"] = "p@ss;w0rd=with\"quotes",
        };

        var result = GoEnvCompat.BuildPostgresConnectionString(k => env.GetValueOrDefault(k));

        Assert.Contains("Password=p@ss;w0rd=with\"quotes", result);
    }

    // ══════════════════════════════════════════════════════════════════
    // GetConfigOverrides
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void GetConfigOverrides_MapsAllKnownVars()
    {
        var env = new Dictionary<string, string?>
        {
            ["KAFKA_SERVERS"] = "kafka1:9092,kafka2:9092",
            ["REDIS_PASSWORD"] = "redispass",
            ["OBJECT_STORAGE_FS"] = "/data",
            ["ADMIN_PASSWORD"] = "admin123",
            ["REACT_APP_FRONTEND_URI"] = "https://dash.example.com",
            ["REACT_APP_AUTH_MODE"] = "Simple",
            ["CLICKHOUSE_ADDRESS"] = "ch:8123",
            ["CLICKHOUSE_DATABASE"] = "analytics",
            ["CLICKHOUSE_USERNAME"] = "reader",
            ["CLICKHOUSE_PASSWORD"] = "chpass",
        };

        var overrides = GoEnvCompat.GetConfigOverrides(k => env.GetValueOrDefault(k));

        Assert.Equal("kafka1:9092,kafka2:9092", overrides["Kafka:BootstrapServers"]);
        Assert.Equal("redispass", overrides["Redis:Password"]);
        Assert.Equal("/data", overrides["Storage:FilesystemRoot"]);
        Assert.Equal("admin123", overrides["Auth:AdminPassword"]);
        Assert.Equal("https://dash.example.com", overrides["Frontend:Uri"]);
        Assert.Equal("Simple", overrides["Auth:Mode"]);
        Assert.Equal("ch:8123", overrides["ClickHouse:Address"]);
        Assert.Equal("analytics", overrides["ClickHouse:Database"]);
        Assert.Equal("reader", overrides["ClickHouse:Username"]);
        Assert.Equal("chpass", overrides["ClickHouse:Password"]);
    }

    [Fact]
    public void GetConfigOverrides_SkipsUnsetVars()
    {
        var env = new Dictionary<string, string?>
        {
            ["KAFKA_SERVERS"] = "kafka:9092",
        };

        var overrides = GoEnvCompat.GetConfigOverrides(k => env.GetValueOrDefault(k));

        Assert.Single(overrides);
        Assert.Equal("kafka:9092", overrides["Kafka:BootstrapServers"]);
    }

    [Fact]
    public void GetConfigOverrides_EmptyEnv_ReturnsEmpty()
    {
        var overrides = GoEnvCompat.GetConfigOverrides(_ => null);

        Assert.Empty(overrides);
    }

    [Fact]
    public void GetConfigOverrides_EmptyStringValues_AreIncluded()
    {
        var env = new Dictionary<string, string?>
        {
            ["CLICKHOUSE_PASSWORD"] = "",
        };

        var overrides = GoEnvCompat.GetConfigOverrides(k => env.GetValueOrDefault(k));

        Assert.True(overrides.ContainsKey("ClickHouse:Password"));
        Assert.Equal("", overrides["ClickHouse:Password"]);
    }

    // ══════════════════════════════════════════════════════════════════
    // Integration: both methods together
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void FullDockerEnv_MapsCorrectly()
    {
        // Simulates the full .env from docker-compose
        var env = new Dictionary<string, string?>
        {
            ["PSQL_DOCKER_HOST"] = "postgres",
            ["PSQL_PORT"] = "5432",
            ["PSQL_USER"] = "postgres",
            ["PSQL_PASSWORD"] = "",
            ["PSQL_DB"] = "postgres",
            ["CLICKHOUSE_ADDRESS"] = "clickhouse:8123",
            ["CLICKHOUSE_DATABASE"] = "default",
            ["CLICKHOUSE_USERNAME"] = "default",
            ["CLICKHOUSE_PASSWORD"] = "",
            ["KAFKA_SERVERS"] = "kafka:9092",
            ["REDIS_PASSWORD"] = "redispassword",
            ["OBJECT_STORAGE_FS"] = "/highlight-data",
            ["REACT_APP_FRONTEND_URI"] = "http://localhost:3000",
            ["REACT_APP_AUTH_MODE"] = "password",
            ["ADMIN_PASSWORD"] = "password",
        };

        Func<string, string?> getEnv = k => env.GetValueOrDefault(k);

        var pgConn = GoEnvCompat.BuildPostgresConnectionString(getEnv);
        var overrides = GoEnvCompat.GetConfigOverrides(getEnv);

        Assert.NotNull(pgConn);
        Assert.Contains("Host=postgres", pgConn);
        Assert.Equal("kafka:9092", overrides["Kafka:BootstrapServers"]);
        Assert.Equal("clickhouse:8123", overrides["ClickHouse:Address"]);
        Assert.Equal("/highlight-data", overrides["Storage:FilesystemRoot"]);
        Assert.Equal("http://localhost:3000", overrides["Frontend:Uri"]);
    }
}
