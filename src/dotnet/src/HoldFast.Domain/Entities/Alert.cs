namespace HoldFast.Domain.Entities;

/// <summary>
/// A unified alert rule. Monitors a product type (sessions, errors, logs, traces, metrics)
/// against threshold conditions. Replaces the legacy per-type alert entities.
/// </summary>
public class Alert : BaseEntity
{
    public int ProjectId { get; set; }
    public string? Name { get; set; }
    public string ProductType { get; set; } = string.Empty;
    public string? FunctionType { get; set; }
    public string? Query { get; set; }
    public string? FunctionColumn { get; set; }
    public string? GroupByKey { get; set; }
    public double? ThresholdValue { get; set; }
    public double? BelowThreshold { get; set; }
    public double? AboveThreshold { get; set; }
    public int? ThresholdWindow { get; set; }
    public int? ThresholdCooldown { get; set; }
    public int? LastAdminToEditId { get; set; }
    public bool Disabled { get; set; }
    public bool Default { get; set; }

    // Navigation
    public Project Project { get; set; } = null!;
    public ICollection<AlertDestination> Destinations { get; set; } = [];
}

/// <summary>
/// A notification target for an alert (e.g., Slack channel, email address, webhook URL).
/// DestinationType identifies the channel kind; TypeId/TypeName identify the specific target.
/// </summary>
public class AlertDestination : BaseEntity
{
    public int AlertId { get; set; }
    public string DestinationType { get; set; } = string.Empty;
    public string? TypeId { get; set; }
    public string? TypeName { get; set; }

    public Alert Alert { get; set; } = null!;
}

// Legacy alert types — kept for migration compatibility

/// <summary>
/// Legacy error alert. Fires when error count exceeds CountThreshold within ThresholdWindow seconds.
/// Superseded by <see cref="Alert"/> with ProductType="ERRORS_ALERT".
/// </summary>
public class ErrorAlert : BaseEntity
{
    public int ProjectId { get; set; }
    public bool Disabled { get; set; }
    public string? Name { get; set; }
    public int? CountThreshold { get; set; }
    public int? ThresholdWindow { get; set; }
    public string? ChannelsToNotify { get; set; }
    public string? EmailsToNotify { get; set; }
    public string? WebhookDestinations { get; set; }
    public string? RegexGroups { get; set; }
    public int? Frequency { get; set; }
    public int? LastAdminToEditId { get; set; }
    public string? Query { get; set; }

    public Project Project { get; set; } = null!;
}

/// <summary>
/// Legacy session alert. Fires on session events matching Type (e.g., NEW_USER_ALERT, RAGE_CLICK_ALERT).
/// TrackProperties/UserProperties filter by session metadata. Superseded by <see cref="Alert"/>.
/// </summary>
public class SessionAlert : BaseEntity
{
    public int ProjectId { get; set; }
    public bool Disabled { get; set; }
    public string? Name { get; set; }
    public string? Type { get; set; }
    public int? CountThreshold { get; set; }
    public int? ThresholdWindow { get; set; }
    public string? ChannelsToNotify { get; set; }
    public string? EmailsToNotify { get; set; }
    public string? WebhookDestinations { get; set; }
    public string? TrackProperties { get; set; }
    public string? UserProperties { get; set; }
    public string? ExcludeRules { get; set; }
    public int? LastAdminToEditId { get; set; }
    public string? Query { get; set; }

    public Project Project { get; set; } = null!;
}

/// <summary>
/// Legacy log alert. Fires when log entries matching Query exceed CountThreshold
/// within ThresholdWindow seconds. BelowThreshold triggers when count drops below a floor.
/// </summary>
public class LogAlert : BaseEntity
{
    public int ProjectId { get; set; }
    public bool Disabled { get; set; }
    public string? Name { get; set; }
    public int? CountThreshold { get; set; }
    public int? ThresholdWindow { get; set; }
    public int? Frequency { get; set; }
    public string? ChannelsToNotify { get; set; }
    public string? EmailsToNotify { get; set; }
    public string? WebhookDestinations { get; set; }
    public int? BelowThreshold { get; set; }
    public int? LastAdminToEditId { get; set; }
    public string? Query { get; set; }

    public Project Project { get; set; } = null!;
}

/// <summary>
/// Monitors a named metric (e.g., latency, memory) with an aggregation function (P50, AVG, etc.)
/// and fires when the value crosses Threshold. Supports Slack, email, and webhook destinations.
/// </summary>
public class MetricMonitor : BaseEntity
{
    public int ProjectId { get; set; }
    public bool Disabled { get; set; }
    public string? Name { get; set; }
    public string? MetricToMonitor { get; set; }
    public string? Aggregator { get; set; }
    public double? Threshold { get; set; }
    public string? ChannelsToNotify { get; set; }
    public string? EmailsToNotify { get; set; }
    public string? WebhookDestinations { get; set; }
    public string? Filters { get; set; }
    public string? Units { get; set; }
    public int? LastAdminToEditId { get; set; }

    public Project Project { get; set; } = null!;
}

// Event log tables for legacy alerts

/// <summary>
/// Records that an error alert fired for a specific error group.
/// </summary>
public class ErrorAlertEvent : BaseEntity
{
    public int ErrorAlertId { get; set; }
    public int ErrorGroupId { get; set; }
}

/// <summary>
/// Records that a session alert fired for a specific session.
/// </summary>
public class SessionAlertEvent : BaseEntity
{
    public int SessionAlertId { get; set; }
    public int SessionId { get; set; }
}

/// <summary>
/// Records that a log alert fired, including the matched query and result count.
/// </summary>
public class LogAlertEvent : BaseEntity
{
    public int LogAlertId { get; set; }
    public string? Query { get; set; }
    public int? Count { get; set; }
}
