using ClickHouse.Client.ADO;
using ClickHouse.Client.Utility;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HoldFast.Data.ClickHouse;

/// <summary>
/// Configuration for the ClickHouse migrations runner.
/// </summary>
public class ClickHouseMigrationOptions
{
    /// <summary>
    /// Filesystem path to the directory containing *.up.sql migration files.
    /// In the Docker image these are copied to /app/clickhouse-migrations.
    /// Locally (e.g. during tests) point this at src/backend/clickhouse/migrations.
    /// Set to empty/null to disable migrations (e.g. when an external system manages the schema).
    /// </summary>
    public string Path { get; set; } = "/app/clickhouse-migrations";

    /// <summary>
    /// Skip running migrations. Useful when the schema is managed externally
    /// (production deployments using golang-migrate, ops-managed Helm pre-jobs, etc).
    /// </summary>
    public bool Disabled { get; set; }
}

/// <summary>
/// Runs ClickHouse SQL migrations from a directory at startup, idempotently.
///
/// Schema parity with the Go backend's golang-migrate runner: tracks applied
/// versions in a `schema_migrations(version Int64, dirty Bool)` table so the
/// existing migration files can be shared between Go and .NET deployments
/// without divergence.
///
/// Replaces the manual `for f in *.up.sql; do clickhouse-client -i $f` step
/// that fresh stacks otherwise required (HOL-11).
/// </summary>
public class ClickHouseMigrationService : IHostedService
{
    private readonly ClickHouseOptions _chOptions;
    private readonly ClickHouseMigrationOptions _options;
    private readonly ILogger<ClickHouseMigrationService> _logger;

    public ClickHouseMigrationService(
        IOptions<ClickHouseOptions> chOptions,
        IOptions<ClickHouseMigrationOptions> options,
        ILogger<ClickHouseMigrationService> logger)
    {
        _chOptions = chOptions.Value;
        _options = options.Value;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (_options.Disabled)
        {
            _logger.LogInformation("ClickHouse migrations: disabled by configuration, skipping");
            return;
        }

        if (string.IsNullOrEmpty(_options.Path) || !Directory.Exists(_options.Path))
        {
            _logger.LogWarning(
                "ClickHouse migrations: directory {Path} not found, skipping. " +
                "Set ClickHouse:Migrations:Path or mount the migrations folder.",
                _options.Path);
            return;
        }

        await using var conn = new ClickHouseConnection(_chOptions.GetConnectionString(readOnly: false));
        await conn.OpenAsync(cancellationToken);

        await EnsureMigrationsTableAsync(conn, cancellationToken);
        var applied = await GetAppliedVersionsAsync(conn, cancellationToken);

        var files = Directory.GetFiles(_options.Path, "*.up.sql")
            .OrderBy(f => Path.GetFileName(f), StringComparer.Ordinal)
            .ToList();

        var appliedCount = 0;
        foreach (var file in files)
        {
            var version = ParseVersion(Path.GetFileName(file));
            if (version is null) continue; // not a migration we recognize
            if (applied.Contains(version.Value)) continue;

            await ApplyMigrationAsync(conn, version.Value, file, cancellationToken);
            appliedCount++;
        }

        _logger.LogInformation(
            "ClickHouse migrations: {Applied} applied, {Total} total, {AlreadyApplied} already-applied",
            appliedCount, files.Count, applied.Count);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static async Task EnsureMigrationsTableAsync(ClickHouseConnection conn, CancellationToken ct)
    {
        // Schema matches golang-migrate's clickhouse driver so the tracking table
        // is interchangeable between Go and .NET deployments.
        const string sql = @"
            CREATE TABLE IF NOT EXISTS schema_migrations
            (
                version Int64,
                dirty   UInt8,
                sequence UInt64
            )
            ENGINE = TinyLog";
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task<HashSet<long>> GetAppliedVersionsAsync(
        ClickHouseConnection conn, CancellationToken ct)
    {
        // Latest entry per version determines current state; clean (dirty=0)
        // means the migration completed successfully.
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT version
            FROM (
                SELECT version, argMax(dirty, sequence) AS latest_dirty
                FROM schema_migrations
                GROUP BY version
            )
            WHERE latest_dirty = 0";
        using var reader = await cmd.ExecuteReaderAsync(ct);
        var set = new HashSet<long>();
        while (await reader.ReadAsync(ct))
            set.Add(Convert.ToInt64(reader.GetValue(0)));
        return set;
    }

    private async Task ApplyMigrationAsync(
        ClickHouseConnection conn, long version, string file, CancellationToken ct)
    {
        var name = Path.GetFileName(file);
        _logger.LogInformation("ClickHouse migration: applying {Version} {Name}", version, name);

        var sql = await File.ReadAllTextAsync(file, ct);

        // Mark dirty before running so a partial failure leaves a visible
        // trail. INSERT-only — golang-migrate's pattern, since TinyLog
        // (and other Log-family engines) doesn't support mutations.
        await RecordVersionAsync(conn, version, dirty: true, ct);

        // Migration files contain semicolon-separated statements; split on
        // top-level semicolons (rough but matches what golang-migrate does for
        // the same files — these don't contain string literals with embedded ";").
        foreach (var statement in SplitStatements(sql))
        {
            var trimmed = statement.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;
            using var cmd = conn.CreateCommand();
            cmd.CommandText = trimmed;
            await cmd.ExecuteNonQueryAsync(ct);
        }

        // Mark clean — newer sequence wins in the latest-per-version query.
        await RecordVersionAsync(conn, version, dirty: false, ct);
    }

    private static async Task RecordVersionAsync(
        ClickHouseConnection conn, long version, bool dirty, CancellationToken ct)
    {
        using var ins = conn.CreateCommand();
        ins.CommandText =
            "INSERT INTO schema_migrations (version, dirty, sequence) VALUES " +
            "({v:Int64}, {d:UInt8}, {s:UInt64})";
        ins.AddParameter("v", version);
        ins.AddParameter("d", dirty ? (byte)1 : (byte)0);
        ins.AddParameter("s", (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        await ins.ExecuteNonQueryAsync(ct);
    }

    private static long? ParseVersion(string filename)
    {
        // Migration filenames look like "000001_create_logs_table.up.sql" — the
        // numeric prefix is the version.
        var idx = filename.IndexOf('_');
        if (idx <= 0) return null;
        return long.TryParse(filename[..idx], out var n) ? n : null;
    }

    /// <summary>
    /// Splits a migration body on top-level semicolons. ClickHouse migration
    /// files don't typically contain string literals with embedded semicolons,
    /// so this simple split is sufficient (and matches how golang-migrate handles
    /// the same files for parity).
    /// </summary>
    private static IEnumerable<string> SplitStatements(string sql)
    {
        var current = new System.Text.StringBuilder();
        foreach (var line in sql.Split('\n'))
        {
            var stripped = line.TrimStart();
            // Skip line comments
            if (stripped.StartsWith("--")) continue;
            current.AppendLine(line);
        }
        var cleaned = current.ToString();

        // Split on ';' followed by whitespace/newline, keep statements
        var parts = cleaned.Split(';');
        foreach (var p in parts)
            if (!string.IsNullOrWhiteSpace(p))
                yield return p;
    }
}
