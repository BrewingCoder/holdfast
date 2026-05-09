-- HOL-26: Postgres analytics schema bootstrap.
--
-- The PostgresMigrationService.EnsureSchemaAsync method already creates this
-- schema before applying any migration (so the migration runner itself can
-- insert into analytics.schema_migrations). This file exists so the schema
-- creation is also recorded in the migration history — operators reading
-- *.up.sql get a complete schema-of-record story without needing to know
-- about the runner's bootstrap step.
CREATE SCHEMA IF NOT EXISTS analytics;

COMMENT ON SCHEMA analytics IS
    'HoldFast analytics tables (logs, traces, sessions, errors, metrics). '
    'Owned by HoldFast.Data.Postgres. Separate from public schema which '
    'holds the relational data managed by HoldFast.Data + EF Core.';
