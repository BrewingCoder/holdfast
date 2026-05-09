-- HOL-33: metrics hypertable.
--
-- Single consolidated table replacing CH's `metrics_sum` + `metrics` (CH had
-- a write-side raw table and a read-side MV). For PG we just use one table —
-- write directly, query directly. Simpler at hobby scale; for higher write
-- volume a future PR can add a TimescaleDB continuous aggregate.
--
-- Tags are JSONB (vs CH's parallel-array Tags.Name / Tags.Value Nested
-- columns). Same trade-off pattern as logs/traces.

CREATE TABLE IF NOT EXISTS analytics.metrics (
    timestamp           TIMESTAMPTZ NOT NULL,
    project_id          INTEGER NOT NULL,
    metric_name         TEXT NOT NULL,
    metric_value        DOUBLE PRECISION NOT NULL,
    category            TEXT NOT NULL DEFAULT '',
    tags                JSONB NOT NULL DEFAULT '{}'::jsonb,
    secure_session_id   TEXT NOT NULL DEFAULT ''
);

DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM pg_extension WHERE extname = 'timescaledb') THEN
        PERFORM create_hypertable(
            'analytics.metrics',
            'timestamp',
            chunk_time_interval => INTERVAL '1 day',
            if_not_exists => TRUE
        );
        PERFORM add_retention_policy(
            'analytics.metrics',
            INTERVAL '30 days',
            if_not_exists => TRUE
        );
        RAISE NOTICE 'HOL-33: metrics hypertable + 30-day retention configured';
    END IF;
END
$$;

CREATE INDEX IF NOT EXISTS idx_metrics_project_name_timestamp
    ON analytics.metrics (project_id, metric_name, timestamp DESC);
CREATE INDEX IF NOT EXISTS idx_metrics_tags_gin
    ON analytics.metrics USING GIN (tags);

COMMENT ON TABLE analytics.metrics IS
    'Application metrics (counters, gauges, histograms) ingested via the '
    'OTLP receiver and HoldFast.Worker.MetricsWorker. Hypertable, 30-day '
    'retention. HOL-33 / EPIC HOL-28.';
