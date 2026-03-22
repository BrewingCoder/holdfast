namespace HoldFast.Domain.Entities;

/// <summary>
/// A named dashboard within a project, containing metrics and a layout configuration.
/// </summary>
public class Dashboard : BaseEntity
{
    public int ProjectId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int? LastAdminToEditId { get; set; }
    public string? Layout { get; set; }
    public bool? IsDefault { get; set; }

    public ICollection<DashboardMetric> Metrics { get; set; } = [];
}

/// <summary>
/// A metric tile on a dashboard (e.g., P50 latency, error rate). Configures aggregation,
/// thresholds, and display parameters.
/// </summary>
public class DashboardMetric : BaseEntity
{
    public int DashboardId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? ComponentType { get; set; }
    public string? ChartType { get; set; }
    public string? Aggregator { get; set; } = "P50";
    public double? MaxGoodValue { get; set; }
    public double? MaxNeedsImprovementValue { get; set; }
    public double? PoorValue { get; set; }
    public string? Units { get; set; }
    public string? HelpArticle { get; set; }
    public double? MinValue { get; set; }
    public double? MinPercentile { get; set; }
    public double? MaxValue { get; set; }
    public double? MaxPercentile { get; set; }
    public List<string> Groups { get; set; } = [];

    public Dashboard Dashboard { get; set; } = null!;
    public ICollection<DashboardMetricFilter> Filters { get; set; } = [];
}

/// <summary>
/// Filter applied to a dashboard metric (e.g., tag="environment", op="equals", value="production").
/// </summary>
public class DashboardMetricFilter : BaseEntity
{
    public int MetricId { get; set; }
    public int MetricMonitorId { get; set; }
    public string Tag { get; set; } = string.Empty;
    public string Op { get; set; } = "equals";
    public string Value { get; set; } = string.Empty;
}

/// <summary>
/// A graph within a visualization. Configures what data to query (ProductType, Query),
/// how to aggregate (GroupByKey, BucketByKey, BucketCount), and how to display (Display).
/// </summary>
public class Graph : BaseEntity
{
    public int ProjectId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? ProductType { get; set; }
    public string? Query { get; set; }
    public string? MetricViewComponentType { get; set; }
    public string? FunctionType { get; set; }
    public string? GroupByKey { get; set; }
    public string? BucketByKey { get; set; }
    public int? BucketCount { get; set; }
    public int? Limit { get; set; }
    public string? LimitFunctionType { get; set; }
    public string? LimitMetric { get; set; }
    public string? Display { get; set; }
    public bool? NullHandling { get; set; }
    public int? VisualizationId { get; set; }

    public Project Project { get; set; } = null!;
}

/// <summary>
/// A named visualization container that holds one or more graphs. Used for the custom
/// dashboard builder.
/// </summary>
public class Visualization : BaseEntity
{
    public int ProjectId { get; set; }
    public string Name { get; set; } = string.Empty;

    public ICollection<Graph> Graphs { get; set; } = [];
}
