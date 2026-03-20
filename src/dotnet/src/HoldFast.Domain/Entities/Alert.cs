namespace HoldFast.Domain.Entities;

public class Alert : BaseEntity
{
    public int ProjectId { get; set; }
    public string? Name { get; set; }
    public string ProductType { get; set; } = string.Empty;
    public string? FunctionType { get; set; }
    public string? Query { get; set; }
    public string? FunctionColumn { get; set; }
    public string? GroupByKey { get; set; }
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

public class AlertDestination : BaseEntity
{
    public int AlertId { get; set; }
    public string DestinationType { get; set; } = string.Empty;
    public string? TypeId { get; set; }
    public string? TypeName { get; set; }

    public Alert Alert { get; set; } = null!;
}

// Legacy alert types — kept for migration compatibility

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

public class LogAlert : BaseEntity
{
    public int ProjectId { get; set; }
    public bool Disabled { get; set; }
    public string? Name { get; set; }
    public int? CountThreshold { get; set; }
    public int? ThresholdWindow { get; set; }
    public string? ChannelsToNotify { get; set; }
    public string? EmailsToNotify { get; set; }
    public string? WebhookDestinations { get; set; }
    public int? BelowThreshold { get; set; }
    public int? LastAdminToEditId { get; set; }
    public string? Query { get; set; }

    public Project Project { get; set; } = null!;
}

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
public class ErrorAlertEvent : BaseEntity
{
    public int ErrorAlertId { get; set; }
    public int ErrorGroupId { get; set; }
}

public class SessionAlertEvent : BaseEntity
{
    public int SessionAlertId { get; set; }
    public int SessionId { get; set; }
}

public class LogAlertEvent : BaseEntity
{
    public int LogAlertId { get; set; }
    public string? Query { get; set; }
    public int? Count { get; set; }
}
