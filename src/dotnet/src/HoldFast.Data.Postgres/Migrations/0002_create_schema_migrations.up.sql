-- HOL-26: Migration tracking table.
--
-- PostgresMigrationService.EnsureMigrationsTableAsync also creates this table
-- before applying any migration; this file exists for the same documentary
-- reason as 0001 — *.up.sql files together describe the full schema state.
--
-- Schema parity with golang-migrate's postgres driver (version + dirty),
-- plus an applied_at column for operator convenience.
CREATE TABLE IF NOT EXISTS analytics.schema_migrations
(
    version    BIGINT PRIMARY KEY,
    dirty      BOOLEAN NOT NULL DEFAULT false,
    applied_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

COMMENT ON TABLE analytics.schema_migrations IS
    'HOL-26: tracks applied analytics-schema migrations. '
    'dirty=true rows indicate a partial-failure mid-migration that needs '
    'manual investigation (the runner uses transactional DDL so this should '
    'be rare).';
