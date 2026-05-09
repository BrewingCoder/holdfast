namespace HoldFast.Data.Postgres;

/// <summary>
/// Configuration for the Postgres analytics backend.
///
/// HOL-26: HoldFast's hobby deployment runs a single Postgres container that
/// holds both the relational data (users, projects, workspaces — owned by
/// HoldFast.Data via EF Core) AND the analytics data (logs, traces, sessions,
/// errors, metrics — owned by HoldFast.Data.Postgres via direct Npgsql).
/// The two share a host but live in separate schemas (relational uses
/// `public`; analytics uses `analytics`).
///
/// Production deployments can point AnalyticsConnectionString at a separate
/// Postgres instance for capacity isolation; HoldFast doesn't care.
/// </summary>
public class PostgresAnalyticsOptions
{
    /// <summary>
    /// Npgsql connection string for the analytics database. If unset, falls
    /// back to ConnectionStrings:PostgreSQL (the relational connection),
    /// which is the right default for the hobby/lean stack where one PG
    /// container hosts both schemas.
    /// </summary>
    public string? ConnectionString { get; set; }

    /// <summary>
    /// Schema name for analytics tables. Default `analytics` keeps the
    /// relational `public` schema clean.
    /// </summary>
    public string Schema { get; set; } = "analytics";
}

/// <summary>
/// Configuration for the Postgres analytics migrations runner.
/// </summary>
public class PostgresAnalyticsMigrationOptions
{
    /// <summary>
    /// Filesystem path to the directory containing *.up.sql migration files.
    /// In the Docker image these are copied to /app/postgres-analytics-migrations.
    /// Locally (e.g. during dev runs) point this at
    /// src/dotnet/src/HoldFast.Data.Postgres/Migrations.
    /// </summary>
    public string Path { get; set; } = "/app/postgres-analytics-migrations";

    /// <summary>
    /// Skip running migrations. Useful when the analytics schema is managed
    /// externally (Helm pre-jobs, external golang-migrate runs, etc).
    /// </summary>
    public bool Disabled { get; set; }
}
