-- HOL-30: traces hypertable + indexes.
--
-- Mirrors ClickHouse's `default.traces` table from migration 000041 +
-- subsequent column-add migrations (Environment in 000061, HasErrors in
-- 000077). Single consolidated final-state DDL — fresh installs don't need
-- the historical churn.
--
-- Partitioning: TimescaleDB hypertable with daily chunks (matches CH's
-- `PARTITION BY toDate(Timestamp)`). 30-day retention via drop_chunks.
--
-- trace_attributes uses JSONB (vs CH's Map). Events + Links are stored as
-- JSONB arrays — CH used Nested columns which require parallel-array
-- handling. Note: ClickHouseService currently leaves Events/Links empty
-- (parallel-array reads not implemented), so PostgresTraceStore matches
-- that behavior — the columns exist for forward compatibility but aren't
-- yet read or written. Future PR can populate them once the OTLP ingest
-- path stops dropping them on the floor.

CREATE TABLE IF NOT EXISTS analytics.traces (
    timestamp         TIMESTAMPTZ NOT NULL,
    uuid              UUID NOT NULL,
    project_id        INTEGER NOT NULL,
    trace_id          TEXT NOT NULL DEFAULT '',
    span_id           TEXT NOT NULL DEFAULT '',
    parent_span_id    TEXT NOT NULL DEFAULT '',
    secure_session_id TEXT NOT NULL DEFAULT '',
    trace_state       TEXT NOT NULL DEFAULT '',
    span_name         TEXT NOT NULL DEFAULT '',
    span_kind         TEXT NOT NULL DEFAULT '',
    duration          BIGINT NOT NULL DEFAULT 0,
    service_name      TEXT NOT NULL DEFAULT '',
    service_version   TEXT NOT NULL DEFAULT '',
    trace_attributes  JSONB NOT NULL DEFAULT '{}'::jsonb,
    status_code       TEXT NOT NULL DEFAULT '',
    status_message    TEXT NOT NULL DEFAULT '',
    environment       TEXT NOT NULL DEFAULT '',
    has_errors        BOOLEAN NOT NULL DEFAULT false,
    events            JSONB NOT NULL DEFAULT '[]'::jsonb,
    links             JSONB NOT NULL DEFAULT '[]'::jsonb
);

DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM pg_extension WHERE extname = 'timescaledb') THEN
        PERFORM create_hypertable(
            'analytics.traces',
            'timestamp',
            chunk_time_interval => INTERVAL '1 day',
            if_not_exists => TRUE
        );
        PERFORM add_retention_policy(
            'analytics.traces',
            INTERVAL '30 days',
            if_not_exists => TRUE
        );
        RAISE NOTICE 'HOL-30: traces hypertable + 30-day retention configured';
    ELSE
        RAISE NOTICE
            'HOL-30: TimescaleDB not installed - traces is a regular table. '
            'Retention falls back to in-app DELETE.';
    END IF;
END
$$;

-- Cursor pagination: same (project_id, timestamp DESC, uuid DESC) leading
-- columns as the logs hypertable.
CREATE INDEX IF NOT EXISTS idx_traces_project_timestamp_uuid
    ON analytics.traces (project_id, timestamp DESC, uuid DESC);

-- Trace-id and session-id lookups (drives "all spans for this trace" /
-- "spans for this session" panels). Partial indexes skip the empty defaults.
CREATE INDEX IF NOT EXISTS idx_traces_trace_id
    ON analytics.traces (trace_id, project_id, timestamp DESC)
    WHERE trace_id <> '';
CREATE INDEX IF NOT EXISTS idx_traces_secure_session_id
    ON analytics.traces (secure_session_id, project_id, timestamp DESC)
    WHERE secure_session_id <> '';

-- Span-id lookup for parent walks (recursive CTE for span tree reconstruction).
CREATE INDEX IF NOT EXISTS idx_traces_span_id
    ON analytics.traces (span_id, project_id)
    WHERE span_id <> '';

-- JSONB attribute search.
CREATE INDEX IF NOT EXISTS idx_traces_attributes_gin
    ON analytics.traces USING GIN (trace_attributes);

-- Has-errors filter for the dashboard "show error spans" view.
CREATE INDEX IF NOT EXISTS idx_traces_has_errors
    ON analytics.traces (project_id, timestamp DESC)
    WHERE has_errors = true;

COMMENT ON TABLE analytics.traces IS
    'Distributed-trace spans ingested via OTLP and written by '
    'HoldFast.Worker.TraceIngestionWorker. Hypertable with daily chunks; '
    '30-day retention via TimescaleDB drop_chunks policy. '
    'Mirrors ClickHouse default.traces schema for cross-backend parity.';
