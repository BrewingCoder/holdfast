-- HOL-29: log key/value catalog tables.
--
-- These power the dashboard's autocomplete UI for the logs filter:
-- - GetLogKeysAsync returns the distinct attribute keys for a project+date range
-- - GetLogKeyValuesAsync returns the distinct values for a (project, key) pair
--
-- ClickHouse used SummingMergeTree + materialized views to maintain these
-- catalogs. PG's equivalent for hobby scale: small tables that PostgresLogStore
-- upserts into inline during WriteLogsAsync. The trade-off vs continuous
-- aggregates is more work per insert, but that work is bounded by the number of
-- unique (project, key, day) tuples in a batch — small for typical workloads.
-- For high-volume deployments a future PR can swap to TimescaleDB continuous
-- aggregates over `analytics.logs.log_attributes`.

CREATE TABLE IF NOT EXISTS analytics.log_keys (
    project_id INTEGER NOT NULL,
    key        TEXT NOT NULL,
    day        DATE NOT NULL,
    count      BIGINT NOT NULL DEFAULT 0,
    type       TEXT NOT NULL DEFAULT 'String',
    PRIMARY KEY (project_id, key, day)
);

CREATE TABLE IF NOT EXISTS analytics.log_key_values (
    project_id INTEGER NOT NULL,
    key        TEXT NOT NULL,
    day        DATE NOT NULL,
    value      TEXT NOT NULL,
    count      BIGINT NOT NULL DEFAULT 0,
    PRIMARY KEY (project_id, key, day, value)
);

-- Lookup by (project, key) for the values autocomplete; (project) for the keys
-- autocomplete. Day filtering is handled by the PK leading columns.
CREATE INDEX IF NOT EXISTS idx_log_keys_project_day
    ON analytics.log_keys (project_id, day DESC);
CREATE INDEX IF NOT EXISTS idx_log_key_values_project_key_day
    ON analytics.log_key_values (project_id, key, day DESC);

COMMENT ON TABLE analytics.log_keys IS
    'Log attribute key catalog, populated inline by HOL-29 PostgresLogStore.WriteLogsAsync. '
    'Used by GetLogKeysAsync to drive the dashboard logs-filter autocomplete. '
    'Mirrors ClickHouse default.log_keys schema.';
COMMENT ON TABLE analytics.log_key_values IS
    'Log attribute (key, value) catalog. Same population strategy as log_keys.';
