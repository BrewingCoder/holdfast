namespace HoldFast.Data.ClickHouse;

/// <summary>
/// Configuration for ClickHouse connection, matching Go's clickhouse.Options.
/// </summary>
public class ClickHouseOptions
{
    public string Address { get; set; } = "localhost:8123";
    public string Database { get; set; } = "default";
    public string Username { get; set; } = "default";
    public string Password { get; set; } = string.Empty;
    public string ReadonlyUsername { get; set; } = "default";
    public string ReadonlyPassword { get; set; } = string.Empty;
    public int MaxOpenConnections { get; set; } = 100;

    /// <summary>
    /// Build an HTTP connection string for ClickHouse.Client.
    /// </summary>
    public string GetConnectionString(bool readOnly = false)
    {
        var user = readOnly ? ReadonlyUsername : Username;
        var pass = readOnly ? ReadonlyPassword : Password;
        var protocol = Address.EndsWith(":9440") ? "https" : "http";
        return $"Host={Address};Protocol={protocol};Database={Database};Username={user};Password={pass}";
    }
}
