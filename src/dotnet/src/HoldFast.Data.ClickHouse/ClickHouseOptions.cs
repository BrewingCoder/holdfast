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
    /// Address may be "host:port" or just "host" — split them for the client.
    /// </summary>
    public string GetConnectionString(bool readOnly = false)
    {
        var user = readOnly ? ReadonlyUsername : Username;
        var pass = readOnly ? ReadonlyPassword : Password;

        // Parse host and optional port from Address (e.g. "clickhouse:8123" or "localhost")
        string host;
        string portPart;
        var colonIdx = Address.LastIndexOf(':');
        if (colonIdx > 0 && int.TryParse(Address[(colonIdx + 1)..], out _))
        {
            host = Address[..colonIdx];
            portPart = $";Port={Address[(colonIdx + 1)..]}";
        }
        else
        {
            host = Address;
            portPart = string.Empty;
        }

        var protocol = Address.EndsWith(":9440") ? "https" : "http";
        return $"Host={host}{portPart};Protocol={protocol};Database={Database};Username={user};Password={pass}";
    }
}
