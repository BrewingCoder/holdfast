namespace HoldFast.Domain.Enums;

/// <summary>
/// Data retention periods for workspace telemetry. Stored as strings in the database.
/// Self-hosted deployments default to SixMonths for sessions/errors, ThirtyDays for logs/traces/metrics.
/// </summary>
public enum RetentionPeriod
{
    SevenDays,
    ThirtyDays,
    ThreeMonths,
    SixMonths,
    TwelveMonths,
    TwoYears,
    ThreeYears
}
