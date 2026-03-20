namespace HoldFast.Domain.Enums;

/// <summary>
/// Log severity levels matching OpenTelemetry conventions.
/// </summary>
public enum LogLevel
{
    Trace = 1,
    Debug = 5,
    Info = 9,
    Warn = 13,
    Error = 17,
    Fatal = 21,
}

/// <summary>
/// Source of the log entry.
/// </summary>
public enum LogSource
{
    Frontend,
    Backend,
}

/// <summary>
/// OpenTelemetry span kinds.
/// </summary>
public enum SpanKind
{
    Internal,
    Server,
    Client,
    Producer,
    Consumer,
}

/// <summary>
/// Metric aggregation functions used in analytics queries.
/// </summary>
public enum MetricAggregator
{
    Count,
    CountDistinct,
    Sum,
    Avg,
    Min,
    Max,
    P50,
    P90,
    P95,
    P99,
}

/// <summary>
/// Product types for cross-product queries.
/// </summary>
public enum ProductType
{
    Sessions,
    Errors,
    Logs,
    Traces,
    Metrics,
    Events,
}
