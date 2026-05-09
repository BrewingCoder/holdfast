-- HOL-31: session field-values catalog.
--
-- Backs PostgresSessionAnalyticsStore.GetSessionsKeyValuesAsync. CH used a
-- shared `fields` table populated by Go-side worker code that's been removed
-- in the .NET migration. Rather than reintroduce a complex shared table, this
-- catalog is per-domain (sessions only) and populated inline by
-- WriteSessionsAsync from the SessionRowInput columns.
--
-- Note: the CH ClickHouseService.GetSessionsKeysAsync returns a hardcoded
-- list of reserved keys without hitting the DB at all. PG matches that
-- behavior — this catalog is only used for the *values* lookup. Schema
-- intentionally identical to log_key_values / trace_key_values for
-- consistency.

CREATE TABLE IF NOT EXISTS analytics.session_field_values (
    project_id INTEGER NOT NULL,
    key        TEXT NOT NULL,
    day        DATE NOT NULL,
    value      TEXT NOT NULL,
    count      BIGINT NOT NULL DEFAULT 0,
    PRIMARY KEY (project_id, key, day, value)
);

CREATE INDEX IF NOT EXISTS idx_session_field_values_project_key_day
    ON analytics.session_field_values (project_id, key, day DESC);

COMMENT ON TABLE analytics.session_field_values IS
    'Reserved-key value catalog populated by HOL-31 PostgresSessionAnalyticsStore.';
