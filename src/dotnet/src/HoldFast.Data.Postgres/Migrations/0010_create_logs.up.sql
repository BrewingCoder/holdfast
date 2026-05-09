-- HOL-29: logs hypertable + indexes.
--
-- Mirrors ClickHouse's `default.logs` table from src/backend/clickhouse/migrations/
-- 000006_create_logs_new + 000011 (service_version) + 000060 (environment) + the
-- Source column from one of the Source-adding migrations. Single consolidated
-- final-state DDL — fresh installs don't need the historical churn.
--
-- Partitioning: TimescaleDB hypertable with daily chunks (matches CH's
-- `PARTITION BY toDate(Timestamp)`). Retention policy drops chunks > 30 days
-- old, mirroring CH's `TTL Timestamp + toIntervalDay(30)`.
--
-- log_attributes uses JSONB (vs CH's Map) — better PG ergonomics, GIN-indexable
-- for key/value lookups, and round-trips cleanly to Dictionary<string,string>
-- in the .NET layer via Npgsql's built-in JSONB support.

CREATE TABLE IF NOT EXISTS analytics.logs (
    timestamp         TIMESTAMPTZ NOT NULL,
    uuid              UUID NOT NULL,
    project_id        INTEGER NOT NULL,
    trace_id          TEXT NOT NULL DEFAULT '',
    span_id           TEXT NOT NULL DEFAULT '',
    secure_session_id TEXT NOT NULL DEFAULT '',
    trace_flags       INTEGER NOT NULL DEFAULT 0,
    severity_text     TEXT NOT NULL DEFAULT '',
    severity_number   INTEGER NOT NULL DEFAULT 0,
    source            TEXT NOT NULL DEFAULT '',
    service_name      TEXT NOT NULL DEFAULT '',
    service_version   TEXT NOT NULL DEFAULT '',
    body              TEXT NOT NULL DEFAULT '',
    log_attributes    JSONB NOT NULL DEFAULT '{}'::jsonb,
    environment       TEXT NOT NULL DEFAULT ''
);

-- TimescaleDB hypertable. The `if_not_exists` flag makes this re-runnable even
-- when no TS extension is present (the function call would fail without
-- TimescaleDB; we let migrations 0003 enable the extension first).
DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM pg_extension WHERE extname = 'timescaledb') THEN
        PERFORM create_hypertable(
            'analytics.logs',
            'timestamp',
            chunk_time_interval => INTERVAL '1 day',
            if_not_exists => TRUE
        );
        -- Drop chunks older than 30 days (replaces CH's TTL).
        PERFORM add_retention_policy(
            'analytics.logs',
            INTERVAL '30 days',
            if_not_exists => TRUE
        );
        RAISE NOTICE 'HOL-29: logs hypertable + 30-day retention configured';
    ELSE
        RAISE NOTICE
            'HOL-29: TimescaleDB not installed - logs is a regular table. '
            'Retention falls back to in-app DELETE (DataRetentionWorker).';
    END IF;
END
$$;

-- Common query indexes. TimescaleDB partitions on timestamp so queries that
-- filter on (project_id, timestamp range) prune chunks before hitting indexes;
-- we still need a btree to support the cursor-paginated read pattern within
-- a chunk.
CREATE INDEX IF NOT EXISTS idx_logs_project_timestamp_uuid
    ON analytics.logs (project_id, timestamp DESC, uuid DESC);

-- Trace-id and session-id lookups are common from the dashboard "logs for this
-- trace" / "logs for this session" panels. Partial indexes skip the empty-string
-- defaults so we don't bloat the index with 'no trace' rows.
CREATE INDEX IF NOT EXISTS idx_logs_trace_id
    ON analytics.logs (trace_id, project_id, timestamp DESC)
    WHERE trace_id <> '';
CREATE INDEX IF NOT EXISTS idx_logs_secure_session_id
    ON analytics.logs (secure_session_id, project_id, timestamp DESC)
    WHERE secure_session_id <> '';

-- JSONB attribute search via GIN. Supports `log_attributes @> '{"key":"val"}'`
-- and `log_attributes ? 'key'` containment/existence ops.
CREATE INDEX IF NOT EXISTS idx_logs_attributes_gin
    ON analytics.logs USING GIN (log_attributes);

COMMENT ON TABLE analytics.logs IS
    'Application logs ingested via OTLP and written by HoldFast.Worker.LogIngestionWorker. '
    'Hypertable with daily chunks; 30-day retention via TimescaleDB drop_chunks policy. '
    'Mirrors ClickHouse default.logs schema for cross-backend parity.';
