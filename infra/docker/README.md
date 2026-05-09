# HoldFast — Local Docker Stack

Quickstart for running HoldFast end-to-end on a single machine. Works on Linux,
macOS, and Windows (via Docker Desktop). The stack runs as 8 services in a
single `docker compose` project: backend, frontend, collector,
postgres, clickhouse, kafka, zookeeper, redis.

(See [HOL-17](https://yt.brewingcoder.com/issue/HOL-17) for the in-flight
work to reduce this further.)

> Production deployments should use a Helm chart (TODO) or a managed-service
> compose. This compose file is for development and demos only — secrets are
> in plaintext and the cluster is single-broker single-replica.

## Prerequisites

- Docker 24+ with Docker Compose v2
- ~6 GB free RAM (kafka + clickhouse + jvm overhead)
- Ports free on the host: 3000, 5432, 6379, 8082, 8123, 8889, 9000, 9092, 4317-4319

If you're on Windows, also ensure `*.sh` files in your checkout have LF line
endings. The `.gitattributes` rule should handle this for fresh clones; if
you forked before that rule landed, run `dos2unix infra/docker/*.sh` once.

## First-time setup

```bash
cd infra/docker
cp .env.example .env       # copy the template — .env itself is gitignored
docker compose -f compose.yml -f compose.hobby-dotnet.yml up -d
```

The first build takes 10–15 minutes (frontend has the largest layer). Subsequent
runs reuse cached layers and complete in seconds.

## What auto-runs on startup

A handful of bootstrap services run inside the backend on first boot:

1. **`SystemBootstrapService`** — creates the system project (id 1) for self-telemetry.
2. **`ClickHouseMigrationService`** — applies `src/backend/clickhouse/migrations/*.up.sql`
   idempotently. Tracks applied versions in `default.schema_migrations`. Set
   `ClickHouse__Migrations__Disabled=true` to skip (for environments where
   the schema is managed externally).
3. **`KafkaTopicBootstrapService`** — creates the topics consumers will subscribe to
   (`session-events`, `backend-errors`, `frontend-errors`, `metrics`, `logs`,
   `traces`). Idempotent. Disable via `Kafka__TopicBootstrap__Disabled=true`.
4. **`DevSeedService`** — creates an admin user and four demo workspaces with
   one default project each. **Hobby/dev only** — production sets
   `DevSeed__Enabled=false`.

After ~30 seconds you should see in `docker compose logs backend`:
- `ClickHouse migrations: 146 applied, 146 total`
- `Kafka topic bootstrap: created N of 6 topics`
- `DevSeed: complete — admin=dev@holdfast.local, workspaces=4`

## Logging in

```
URL:      http://localhost:3000
Email:    dev@holdfast.local
Password: $ADMIN_PASSWORD from .env (default "password")
```

The seeded workspaces are: HoldFast Dev, Koinon Dev, SignalClaude Dev,
The Brewery Dev. Each has one project named `default`.

## Retrieving project API keys

After DevSeed runs, the project API keys live in `postgres.projects.secret`.
Easiest way:

```bash
./infra/docker/get-project-keys.sh
```

Output:
```
WORKSPACE         PROJECT  API_KEY
HoldFast          HoldFast  8bfb3a2dfb774175b095dd65e8b546b1
HoldFast Dev      default   76594a41529a46adbb130a7c6505f977
Koinon Dev        default   053361e0f51f478fb211a1f9ad3e4a39
SignalClaude Dev  default   90fd658e91ba4476ac72467bec5e3aa7
The Brewery Dev   default   954b54ff786f45e59df8617399f8cd3c
```

## Verifying ingest

The `smoke-test-ingest.sh` script opens an SDK-style session, pushes one
synthetic error, and waits for the row to appear in ClickHouse:

```bash
./infra/docker/smoke-test-ingest.sh
```

Expected output (within ~10 seconds):
```
[1/3] initializeSession (session_secure_id=smoke-…) → 200
[2/3] pushPayload with 1 synthetic error → 200
[3/3] waiting for ClickHouse error_objects… 1 row found ✓

Smoke test passed. The ingest pipeline is working end-to-end.
```

## Common issues

- **Backend keeps restarting**. The first 60–90 seconds after `up -d` are
  warmup. Once `Application started.` appears in the backend logs, it should
  stay healthy. If it crashes after, check `docker compose logs backend |
  grep fail`.
- **Port 8888 collision**. The collector's Prometheus self-metrics endpoint is
  remapped to host port 8889 to dodge collisions with Jupyter, etc. (see
  HOL-7). The container internally still uses 8888.
- **`Table default.X does not exist`** in worker logs after a fresh `up`.
  The migration runner may have failed. Look for `ClickHouse migrations:`
  lines in the backend logs. If you see `Hosting failed to start`, drop the
  ClickHouse data volume and try again: `docker compose down -v`.
- **Kafka consumers crashing on subscribe**. Topics aren't created. Check for
  `Kafka topic bootstrap:` lines in the backend logs.

## Stopping and restarting

```bash
# Stop everything (data preserved)
docker compose -f compose.yml -f compose.hobby-dotnet.yml down

# Stop AND wipe data (postgres, clickhouse, kafka volumes)
docker compose -f compose.yml -f compose.hobby-dotnet.yml down -v

# Restart only the backend (e.g. after rebuilding the image)
docker compose -f compose.yml -f compose.hobby-dotnet.yml up -d --force-recreate backend
```

## Compose file layering

- `compose.yml` — base infra (postgres, clickhouse, kafka, redis, etc.).
  Always required.
- `compose.hobby-dotnet.yml` — adds the .NET backend, frontend, and collector
  with hobby/dev defaults (DevSeed on, SSL off, plaintext password).
- `compose.hobby.yml` — adds the **legacy Go** backend instead of .NET. Pick
  one or the other, not both. Default for new deployments is `hobby-dotnet`.
- `compose.dev-frontend.yml` — overlay for frontend hot-reload during local
  development. See `frontend.Dockerfile` and PR #66 for context.
- `compose.enterprise*.yml` — production-shaped variants. Out of scope for
  this README.

## Environment variables that matter

The full list is in `.env.example`. The most commonly tweaked ones:

| Variable | Default | What it does |
|---|---|---|
| `ADMIN_PASSWORD` | `password` | Auth password for the seeded admin user |
| `DEV_SEED_ENABLED` | `true` | Auto-create demo workspaces + projects |
| `CLICKHOUSE_MIGRATIONS_DISABLED` | `false` | Skip the .NET-side migration runner |
| `KAFKA_AUTO_CREATE_TOPICS_ENABLE` | `true` | Kafka broker setting (Confluent default off) |
| `REACT_APP_FRONTEND_URI` | `http://localhost:3000` | Where the frontend is reachable |
| `REACT_APP_PRIVATE_GRAPH_URI` | `http://localhost:8082/private` | Dashboard GraphQL URL |
| `REACT_APP_PUBLIC_GRAPH_URI` | `http://localhost:8082/public` | SDK ingest URL |
