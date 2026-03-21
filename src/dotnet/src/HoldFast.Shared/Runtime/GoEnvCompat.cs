namespace HoldFast.Shared.Runtime;

/// <summary>
/// Maps Go backend environment variable names to .NET configuration keys.
/// The Go backend reads PSQL_HOST, KAFKA_SERVERS, etc. directly from env vars.
/// This class builds the equivalent .NET connection strings and config values
/// so the same .env file works for both backends.
/// </summary>
public static class GoEnvCompat
{
    /// <summary>
    /// Build a PostgreSQL connection string from Go-style env vars.
    /// Returns null if PSQL_HOST / PSQL_DOCKER_HOST is not set.
    /// </summary>
    public static string? BuildPostgresConnectionString(
        Func<string, string?> getEnv)
    {
        var host = getEnv("PSQL_DOCKER_HOST") ?? getEnv("PSQL_HOST");
        if (host == null) return null;

        var port = getEnv("PSQL_PORT") ?? "5432";
        var user = getEnv("PSQL_USER") ?? "postgres";
        var pass = getEnv("PSQL_PASSWORD") ?? "";
        var db = getEnv("PSQL_DB") ?? "postgres";

        return $"Host={host};Port={port};Database={db};Username={user};Password={pass}";
    }

    /// <summary>
    /// Returns a dictionary of .NET config key → value for all recognized Go env vars.
    /// Only includes vars that are actually set in the environment.
    /// </summary>
    public static Dictionary<string, string> GetConfigOverrides(
        Func<string, string?> getEnv)
    {
        var overrides = new Dictionary<string, string>();

        var mappings = new (string EnvVar, string ConfigKey)[]
        {
            ("KAFKA_SERVERS", "Kafka:BootstrapServers"),
            ("REDIS_PASSWORD", "Redis:Password"),
            ("OBJECT_STORAGE_FS", "Storage:FilesystemRoot"),
            ("ADMIN_PASSWORD", "Auth:AdminPassword"),
            ("REACT_APP_FRONTEND_URI", "Frontend:Uri"),
            ("REACT_APP_AUTH_MODE", "Auth:Mode"),
            ("CLICKHOUSE_ADDRESS", "ClickHouse:Address"),
            ("CLICKHOUSE_DATABASE", "ClickHouse:Database"),
            ("CLICKHOUSE_USERNAME", "ClickHouse:Username"),
            ("CLICKHOUSE_PASSWORD", "ClickHouse:Password"),
        };

        foreach (var (envVar, configKey) in mappings)
        {
            var value = getEnv(envVar);
            if (value != null)
                overrides[configKey] = value;
        }

        return overrides;
    }
}
