# HoldFast — Local Docker Stack

Quickstart for running HoldFast end-to-end on a single machine. Works on Linux,
macOS, and Windows (via Docker Desktop). The hobby stack runs as **3 services**
in a single `docker compose` project: `backend`, `postgres`, `clickhouse`.

The backend image bundles the SPA frontend, embeds an OTLP receiver, and uses
an in-process message bus, so kafka / zookeeper / redis / collector / nginx
have all been retired (see [HOL-17](https://yt.brewingcoder.com/issue/HOL-17)
and its subtasks).

> Production deployments should use a Helm chart (TODO) or a managed-service
> compose. This compose file is for development and demos only — secrets are
> in plaintext and the cluster is single-broker single-replica.

## Prerequisites

- Docker 24+ with Docker Compose v2
- ~3 GB free RAM (clickhouse is the dominant consumer; tuned by HOL-18)
- Ports free on the host: 5432 (postgres), 8082 (backend + UI), 8123 / 9000 (clickhouse)

If you're on Windows, also ensure `*.sh` files in your checkout have LF line
endings. The `.gitattributes` rule should handle this for fresh clones; if
you forked before that rule landed, run `dos2unix infra/docker/*.sh` once.

## First-time setup

```bash
cd infra/docker
cp .env.example .env       # copy the template — .env itself is gitignored
docker compose -f compose.yml -f compose.hobby-dotnet.yml up -d
```

The first build takes 10–15 minutes (the frontend bundle stage inside
`backend-dotnet.Dockerfile` has the largest layer). Subsequent runs reuse
cached layers and complete in seconds.

## What auto-runs on startup

A handful of bootstrap services run inside the backend on first boot:

1. **`SystemBootstrapService`** — creates the system project (id 1) for self-telemetry.
2. **`ClickHouseMigrationService`** — applies `src/backend/clickhouse/migrations/*.up.sql`
   idempotently. Tracks applied versions in `default.schema_migrations`. Set
   `ClickHouse__Migrations__Disabled=true` to skip (for environments where
   the schema is managed externally).
3. **`DevSeedService`** — creates an admin user and four demo workspaces with
   one default project each. **Hobby/dev only** — production sets
   `DevSeed__Enabled=false`.

After ~30 seconds you should see in `docker compose logs backend`:
- `ClickHouse migrations: 146 applied, 146 total`
- `DevSeed: complete — admin=dev@holdfast.local, workspaces=4`

## Logging in

```
URL:      http://localhost:8082
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
- **`Table default.X does not exist`** in worker logs after a fresh `up`.
  The migration runner may have failed. Look for `ClickHouse migrations:`
  lines in the backend logs. If you see `Hosting failed to start`, drop the
  ClickHouse data volume and try again: `docker compose down -v`.

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

- `compose.yml` — base infra (postgres, clickhouse). Always required.
- `compose.hobby-dotnet.yml` — adds the .NET backend (which now bundles the
  SPA + OTLP receiver) with hobby/dev defaults (DevSeed on, SSL off, plaintext
  password).
- `compose.hobby.yml` — adds the **legacy Go** backend + a separate nginx
  frontend container. Pick one or the other, not both. Default for new
  deployments is `hobby-dotnet`.
- `compose.enterprise*.yml` — production-shaped variants. Out of scope for
  this README.

## Environment variables that matter

The full list is in `.env.example`. The most commonly tweaked ones:

| Variable | Default | What it does |
|---|---|---|
| `ADMIN_PASSWORD` | `password` | Auth password for the seeded admin user |
| `DEV_SEED_ENABLED` | `true` | Auto-create demo workspaces + projects |
| `CLICKHOUSE_MIGRATIONS_DISABLED` | `false` | Skip the .NET-side migration runner |
| `REACT_APP_FRONTEND_URI` | `http://localhost:8082` | Where the dashboard is reachable |
| `REACT_APP_PRIVATE_GRAPH_URI` | `http://localhost:8082/private` | Dashboard GraphQL URL |
| `REACT_APP_PUBLIC_GRAPH_URI` | `http://localhost:8082/public` | SDK ingest URL |

The `REACT_APP_*` URLs are baked into the SPA at backend image build time
(see `backend-dotnet.Dockerfile`). Override the build args and rebuild to
deploy behind a custom domain.
