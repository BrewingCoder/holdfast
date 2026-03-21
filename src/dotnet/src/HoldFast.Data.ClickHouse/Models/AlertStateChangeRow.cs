namespace HoldFast.Data.ClickHouse.Models;

/// <summary>
/// Row from the alert_state_changes ClickHouse table.
/// Mirrors Go's clickhouse.AlertStateChangeRow.
/// </summary>
public class AlertStateChangeRow
{
    public int ProjectId { get; set; }
    public int AlertId { get; set; }
    public DateTime Timestamp { get; set; }
    public string State { get; set; } = string.Empty;
    public string GroupByKey { get; set; } = string.Empty;
}
