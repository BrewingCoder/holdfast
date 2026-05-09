-- HOL-32: error_groups + error_objects tables.
--
-- Mirrors a subset of CH's default.error_groups + default.error_objects.
-- The .NET ingest pipeline only writes columns covered by ErrorGroupRowInput
-- and ErrorObjectRowInput; legacy SaaS columns (Status enum-style, ClientID,
-- etc.) aren't populated.
--
-- error_groups is small (one row per dedup key) — keep as a regular table
-- with a btree on (project_id, created_at). error_objects is high-volume
-- (one row per error occurrence) — hypertable with daily chunks + 30-day
-- retention.

CREATE TABLE IF NOT EXISTS analytics.error_groups (
    project_id      INTEGER NOT NULL,
    error_group_id  INTEGER NOT NULL,
    secure_id       TEXT NOT NULL DEFAULT '',
    created_at      TIMESTAMPTZ NOT NULL,
    updated_at      TIMESTAMPTZ NOT NULL,
    event           TEXT NOT NULL DEFAULT '',
    type            TEXT NOT NULL DEFAULT '',
    state           TEXT NOT NULL DEFAULT 'OPEN',
    service_name    TEXT NOT NULL DEFAULT '',
    environments    TEXT NOT NULL DEFAULT '',
    PRIMARY KEY (project_id, error_group_id)
);

CREATE INDEX IF NOT EXISTS idx_error_groups_project_created
    ON analytics.error_groups (project_id, created_at DESC);
CREATE INDEX IF NOT EXISTS idx_error_groups_secure_id
    ON analytics.error_groups (secure_id) WHERE secure_id <> '';

CREATE TABLE IF NOT EXISTS analytics.error_objects (
    timestamp         TIMESTAMPTZ NOT NULL,
    project_id        INTEGER NOT NULL,
    error_object_id   BIGINT NOT NULL,
    error_group_id    INTEGER NOT NULL,
    event             TEXT NOT NULL DEFAULT '',
    type              TEXT NOT NULL DEFAULT '',
    url               TEXT NOT NULL DEFAULT '',
    environment       TEXT NOT NULL DEFAULT '',
    os                TEXT NOT NULL DEFAULT '',
    browser           TEXT NOT NULL DEFAULT '',
    service_name      TEXT NOT NULL DEFAULT '',
    service_version   TEXT NOT NULL DEFAULT ''
);

DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM pg_extension WHERE extname = 'timescaledb') THEN
        PERFORM create_hypertable(
            'analytics.error_objects',
            'timestamp',
            chunk_time_interval => INTERVAL '1 day',
            if_not_exists => TRUE
        );
        PERFORM add_retention_policy(
            'analytics.error_objects',
            INTERVAL '30 days',
            if_not_exists => TRUE
        );
        RAISE NOTICE 'HOL-32: error_objects hypertable + 30-day retention configured';
    END IF;
END
$$;

CREATE INDEX IF NOT EXISTS idx_error_objects_project_timestamp
    ON analytics.error_objects (project_id, timestamp DESC);
CREATE INDEX IF NOT EXISTS idx_error_objects_group_id
    ON analytics.error_objects (error_group_id, timestamp DESC);

COMMENT ON TABLE analytics.error_groups IS
    'Error group catalog (one row per dedup key). Updated when an error '
    'recurs (UpdatedAt advances). HOL-32 / EPIC HOL-28.';
COMMENT ON TABLE analytics.error_objects IS
    'Per-occurrence error rows (one row per crash/exception). Hypertable '
    'with 30-day retention. HOL-32 / EPIC HOL-28.';
