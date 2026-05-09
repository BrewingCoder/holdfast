-- HOL-31: sessions table + indexes.
--
-- Mirrors a subset of ClickHouse's `default.sessions` schema — only the
-- columns that the .NET ingest pipeline actually fills (per SessionRowInput
-- in HoldFast.Analytics.Models). The CH table has ~30 columns including
-- legacy SaaS-era fields (Fingerprint, FieldKeys/FieldKeyValues arrays,
-- EventCounts, ViewedByAdmins, Normalness, etc.) that HoldFast doesn't
-- write because they were tied to the SaaS billing/admin tooling that's
-- been stripped (see CHANGELOG-FORK).
--
-- Sessions are partitioned by created_at (daily chunks via TimescaleDB).
-- Retention is intentionally NOT set here — sessions are referenced by
-- the relational schema (admin annotations, comments) and shouldn't
-- silently disappear. Operators can opt in via a manual
-- add_retention_policy if their deployment doesn't keep session
-- replays indefinitely.

CREATE TABLE IF NOT EXISTS analytics.sessions (
    created_at         TIMESTAMPTZ NOT NULL,
    project_id         INTEGER NOT NULL,
    session_id         BIGINT NOT NULL,
    secure_session_id  TEXT NOT NULL DEFAULT '',
    identifier         TEXT NOT NULL DEFAULT '',
    os_name            TEXT NOT NULL DEFAULT '',
    os_version         TEXT NOT NULL DEFAULT '',
    browser_name       TEXT NOT NULL DEFAULT '',
    browser_version    TEXT NOT NULL DEFAULT '',
    city               TEXT NOT NULL DEFAULT '',
    state              TEXT NOT NULL DEFAULT '',
    country            TEXT NOT NULL DEFAULT '',
    environment        TEXT NOT NULL DEFAULT '',
    app_version        TEXT NOT NULL DEFAULT '',
    service_name       TEXT NOT NULL DEFAULT '',
    active_length      INTEGER NOT NULL DEFAULT 0,
    length             INTEGER NOT NULL DEFAULT 0,
    pages_visited      INTEGER NOT NULL DEFAULT 0,
    has_errors         BOOLEAN NOT NULL DEFAULT false,
    has_rage_clicks    BOOLEAN NOT NULL DEFAULT false,
    processed          BOOLEAN NOT NULL DEFAULT false,
    first_time         BOOLEAN NOT NULL DEFAULT false
);

DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM pg_extension WHERE extname = 'timescaledb') THEN
        PERFORM create_hypertable(
            'analytics.sessions',
            'created_at',
            chunk_time_interval => INTERVAL '1 day',
            if_not_exists => TRUE
        );
        RAISE NOTICE 'HOL-31: sessions hypertable configured (no retention - operators set manually)';
    END IF;
END
$$;

-- Common dashboard-query indexes
CREATE INDEX IF NOT EXISTS idx_sessions_project_created
    ON analytics.sessions (project_id, created_at DESC);
CREATE INDEX IF NOT EXISTS idx_sessions_secure_id
    ON analytics.sessions (secure_session_id, project_id)
    WHERE secure_session_id <> '';
CREATE INDEX IF NOT EXISTS idx_sessions_identifier
    ON analytics.sessions (project_id, identifier, created_at DESC)
    WHERE identifier <> '';
CREATE INDEX IF NOT EXISTS idx_sessions_has_errors
    ON analytics.sessions (project_id, created_at DESC)
    WHERE has_errors = true;

COMMENT ON TABLE analytics.sessions IS
    'Session analytics records written by HoldFast.Worker.SessionEventsWorker. '
    'Subset of CH default.sessions columns - the SaaS-era columns (Fingerprint, '
    'EventCounts, ViewedByAdmins, etc.) are not present because HoldFast strips '
    'them. No automatic retention - operators set add_retention_policy manually.';
