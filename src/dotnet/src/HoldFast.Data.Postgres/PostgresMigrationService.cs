using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace HoldFast.Data.Postgres;

/// <summary>
/// Runs Postgres analytics-schema migrations from a directory at startup, idempotently.
///
/// Mirrors the design of HoldFast.Data.ClickHouse.ClickHouseMigrationService
/// so the operator experience is identical across backends — same file naming
/// convention (NNNN_description.up.sql), same logging shape, same disable knob.
///
/// Differences from the CH runner:
/// - Postgres supports transactional DDL, so each migration file runs in its
///   own transaction. A failed migration leaves the analytics schema unchanged
///   instead of needing a separate dirty flag. (We still record dirty/clean
///   in schema_migrations for parity with golang-migrate's tracking schema,
///   though it's effectively informational here.)
/// - The schema_migrations table is in `analytics` schema (vs CH's `default`),
///   keeping all analytics state in one namespace.
/// - This service ensures the `analytics` schema itself exists before applying
///   any migration. The first migration assumes the schema is present.
/// </summary>
public class PostgresMigrationService : IHostedService
{
    private readonly PostgresAnalyticsOptions _pgOptions;
    private readonly PostgresAnalyticsMigrationOptions _options;
    private readonly IConfiguration _configuration;
    private readonly ILogger<PostgresMigrationService> _logger;

    public PostgresMigrationService(
        IOptions<PostgresAnalyticsOptions> pgOptions,
        IOptions<PostgresAnalyticsMigrationOptions> options,
        IConfiguration configuration,
        ILogger<PostgresMigrationService> logger)
    {
        _pgOptions = pgOptions.Value;
        _options = options.Value;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (_options.Disabled)
        {
            _logger.LogInformation("Analytics PG migrations: disabled by configuration, skipping");
            return;
        }

        if (string.IsNullOrEmpty(_options.Path) || !Directory.Exists(_options.Path))
        {
            _logger.LogWarning(
                "Analytics PG migrations: directory {Path} not found, skipping. " +
                "Set PostgresAnalytics:Migrations:Path or mount the migrations folder.",
                _options.Path);
            return;
        }

        var connStr = ResolveConnectionString();
        if (string.IsNullOrEmpty(connStr))
        {
            _logger.LogWarning(
                "Analytics PG migrations: no connection string configured " +
                "(neither PostgresAnalytics:ConnectionString nor ConnectionStrings:PostgreSQL). Skipping.");
            return;
        }

        await using var conn = new NpgsqlConnection(connStr);
        await conn.OpenAsync(cancellationToken);

        await EnsureSchemaAsync(conn, cancellationToken);
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
            "Analytics PG migrations: {Applied} applied, {Total} total, {AlreadyApplied} already-applied",
            appliedCount, files.Count, applied.Count);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private string? ResolveConnectionString() =>
        !string.IsNullOrEmpty(_pgOptions.ConnectionString)
            ? _pgOptions.ConnectionString
            : _configuration.GetConnectionString("PostgreSQL");

    private async Task EnsureSchemaAsync(NpgsqlConnection conn, CancellationToken ct)
    {
        // CREATE SCHEMA IF NOT EXISTS is idempotent and safe to run on every start.
        // The schema name is config-driven but defaults to `analytics`; we quote-escape
        // the identifier rather than parameterize it because PG doesn't accept bound
        // parameters for DDL identifiers.
        var schema = SanitizeIdentifier(_pgOptions.Schema);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"CREATE SCHEMA IF NOT EXISTS \"{schema}\"";
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private async Task EnsureMigrationsTableAsync(NpgsqlConnection conn, CancellationToken ct)
    {
        // Schema parity with golang-migrate's postgres driver: version + dirty.
        // We add applied_at for operator convenience; golang-migrate doesn't, but
        // since this table isn't shared with a Go runner the extra column is fine.
        var schema = SanitizeIdentifier(_pgOptions.Schema);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            CREATE TABLE IF NOT EXISTS ""{schema}"".schema_migrations
            (
                version    BIGINT PRIMARY KEY,
                dirty      BOOLEAN NOT NULL DEFAULT false,
                applied_at TIMESTAMPTZ NOT NULL DEFAULT now()
            )";
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private async Task<HashSet<long>> GetAppliedVersionsAsync(
        NpgsqlConnection conn, CancellationToken ct)
    {
        var schema = SanitizeIdentifier(_pgOptions.Schema);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            SELECT version
            FROM ""{schema}"".schema_migrations
            WHERE dirty = false";
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var set = new HashSet<long>();
        while (await reader.ReadAsync(ct))
            set.Add(reader.GetInt64(0));
        return set;
    }

    private async Task ApplyMigrationAsync(
        NpgsqlConnection conn, long version, string file, CancellationToken ct)
    {
        var name = Path.GetFileName(file);
        _logger.LogInformation("Analytics PG migration: applying {Version} {Name}", version, name);

        var sql = await File.ReadAllTextAsync(file, ct);
        var schema = SanitizeIdentifier(_pgOptions.Schema);

        // Run the whole migration in one transaction. PG handles DDL transactionally,
        // so a failure rolls back the schema changes AND the dirty-row insert below
        // (rather than leaving the analytics schema half-built).
        //
        // Edge case: migrations that need CREATE INDEX CONCURRENTLY can't run inside
        // a transaction. We don't have any of those in HOL-26's foundational set, but
        // when we hit one in HOL-29+ we'll add a `-- migrate:no-tx` marker and split
        // the path. Out of scope for this PR.
        await using var tx = await conn.BeginTransactionAsync(ct);
        try
        {
            // Insert dirty marker first so a crash mid-migration leaves a row
            // we can detect and fix manually.
            await using (var ins = conn.CreateCommand())
            {
                ins.Transaction = tx;
                ins.CommandText = $@"
                    INSERT INTO ""{schema}"".schema_migrations (version, dirty)
                    VALUES (@v, true)
                    ON CONFLICT (version) DO UPDATE SET dirty = true, applied_at = now()";
                ins.Parameters.AddWithValue("v", version);
                await ins.ExecuteNonQueryAsync(ct);
            }

            // Apply the migration body. Npgsql can run a multi-statement batch
            // as a single command — semicolon-separated DDL works directly.
            await using (var migrate = conn.CreateCommand())
            {
                migrate.Transaction = tx;
                migrate.CommandText = sql;
                await migrate.ExecuteNonQueryAsync(ct);
            }

            // Mark clean.
            await using (var clean = conn.CreateCommand())
            {
                clean.Transaction = tx;
                clean.CommandText = $@"
                    UPDATE ""{schema}"".schema_migrations
                    SET dirty = false, applied_at = now()
                    WHERE version = @v";
                clean.Parameters.AddWithValue("v", version);
                await clean.ExecuteNonQueryAsync(ct);
            }

            await tx.CommitAsync(ct);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    internal static long? ParseVersion(string filename)
    {
        var idx = filename.IndexOf('_');
        if (idx <= 0) return null;
        return long.TryParse(filename[..idx], out var n) ? n : null;
    }

    /// <summary>
    /// Defensive identifier sanitization for the schema name. The schema is
    /// operator-controlled (config), not user input, but we still strip
    /// anything that isn't an alphanumeric or underscore so a typo can't
    /// produce SQL injection.
    /// </summary>
    internal static string SanitizeIdentifier(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            throw new InvalidOperationException("PostgresAnalytics:Schema cannot be empty");

        var clean = new string(raw.Where(c => char.IsLetterOrDigit(c) || c == '_').ToArray());
        if (string.IsNullOrEmpty(clean))
            throw new InvalidOperationException(
                $"PostgresAnalytics:Schema '{raw}' has no valid identifier characters");
        return clean;
    }
}
