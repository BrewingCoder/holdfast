-- HOL-26: install Postgres extensions used by the analytics schema.
--
-- Both extensions are conditional — CREATE EXTENSION IF NOT EXISTS is a
-- no-op when already installed, and the surrounding DO blocks let us
-- gracefully no-op when the extension isn't available in the running PG
-- image (e.g. vanilla `postgres:16` doesn't ship TimescaleDB; the hobby
-- compose uses `ankane/pgvector` which doesn't either).
--
-- When the extensions ARE present (e.g. `timescale/timescaledb-ha`), HOL-29+
-- migrations will use them for hypertable partitioning + retention.
-- When absent, HOL-29+ falls back to native PG partitioning via pg_partman
-- (also conditional) or per-month declarative partitions managed in-app.
--
-- Reference: docs.timescale.com/self-hosted/latest/install/installation-docker

DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM pg_available_extensions WHERE name = 'timescaledb') THEN
        CREATE EXTENSION IF NOT EXISTS timescaledb;
        RAISE NOTICE 'HOL-26: TimescaleDB extension enabled';
    ELSE
        RAISE NOTICE
            'HOL-26: TimescaleDB extension not available in this PG image. '
            'Analytics tables will use native partitioning. '
            'For larger deployments switch the postgres image to '
            'timescale/timescaledb-ha and re-run migrations.';
    END IF;
END
$$;

DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM pg_available_extensions WHERE name = 'pg_partman') THEN
        CREATE EXTENSION IF NOT EXISTS pg_partman;
        RAISE NOTICE 'HOL-26: pg_partman extension enabled';
    ELSE
        RAISE NOTICE
            'HOL-26: pg_partman not available. Retention will fall back to '
            'in-app DELETE WHERE timestamp < … instead of partition drops.';
    END IF;
END
$$;
