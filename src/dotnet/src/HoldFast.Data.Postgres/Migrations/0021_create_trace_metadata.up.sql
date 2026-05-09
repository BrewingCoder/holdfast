-- HOL-30: trace key/value catalog tables.
--
-- Identical structure to analytics.log_keys / analytics.log_key_values
-- (created in HOL-29 migration 0011). PostgresTraceStore upserts inline
-- during WriteTracesAsync.

CREATE TABLE IF NOT EXISTS analytics.trace_keys (
    project_id INTEGER NOT NULL,
    key        TEXT NOT NULL,
    day        DATE NOT NULL,
    count      BIGINT NOT NULL DEFAULT 0,
    type       TEXT NOT NULL DEFAULT 'String',
    PRIMARY KEY (project_id, key, day)
);

CREATE TABLE IF NOT EXISTS analytics.trace_key_values (
    project_id INTEGER NOT NULL,
    key        TEXT NOT NULL,
    day        DATE NOT NULL,
    value      TEXT NOT NULL,
    count      BIGINT NOT NULL DEFAULT 0,
    PRIMARY KEY (project_id, key, day, value)
);

CREATE INDEX IF NOT EXISTS idx_trace_keys_project_day
    ON analytics.trace_keys (project_id, day DESC);
CREATE INDEX IF NOT EXISTS idx_trace_key_values_project_key_day
    ON analytics.trace_key_values (project_id, key, day DESC);

COMMENT ON TABLE analytics.trace_keys IS
    'Trace attribute key catalog, populated inline by HOL-30 PostgresTraceStore.WriteTracesAsync.';
COMMENT ON TABLE analytics.trace_key_values IS
    'Trace attribute (key, value) catalog. Same population strategy as trace_keys.';
