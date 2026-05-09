# HoldFast soak harness

Long-running container that exercises the analytics-ingest surface
(logs / traces / errors / sessions / metrics / events) with variable-rate
randomized scenarios. Designed to run for ~24 hours alongside the hobby
stack so we can observe real-world ingest pressure on the analytics backend.

## Status

- ✅ **HOL-38** (this ticket) — container scaffolding + entrypoint with placeholder
  scenarios. Container boots, emits one INFO log + one trivial trace per
  tick, exits cleanly on SIGTERM.
- 🟡 **HOL-39** — scenario library (logs / traces / errors / sessions / metrics / events)
- 🟡 **HOL-40** — variable-rate scheduler with spike windows
- 🟡 **HOL-41** — operator runbook + verification queries

## Quick start

```bash
cd infra/docker
cp .env.example .env  # if you don't have one yet

# Bring up the stack PLUS the soak harness
docker compose -f compose.yml \
               -f compose.hobby-dotnet.yml \
               -f compose.soak.yml \
               --profile soak \
               up -d

# Watch the harness emit ticks
docker logs -f holdfast-soak

# Stop just the soak (leaves the rest of the stack running)
docker compose --profile soak stop soak
```

## Configuration (env)

| Var | Default | Purpose |
|---|---|---|
| `HOLDFAST_PROJECT_ID` | `2` | Project id under which to emit data. Default matches DevSeed's "HoldFast Dev / default" project. |
| `OTLP_ENDPOINT` | `http://backend:8082/otel` | Where the OTel exporters POST. Compose-network hostname. |
| `SOAK_BASE_INTERVAL_MS` | `60000` | Tick interval. Lower for faster local validation, higher for gentler 24h runs. |
| `SOAK_SERVICE_NAME` | `holdfast-soak` | `service.name` resource attribute on every span/log — use this in queries to filter soak-generated rows from real traffic. |
| `SOAK_SERVICE_VERSION` | `0.0.0-soak` | `service.version` resource attribute. |

## Output protocol

Every tick emits one line of JSON to stdout:

```json
{"ts":"2026-05-09T18:42:00.123Z","event":"soak.tick","tick":3,"elapsed_ms":7,"scenarios_emitted":["placeholder-log","placeholder-trace"]}
```

Pipe through `jq -c` for quick filtering. Special events:
- `soak.started` — emitted once at boot with the resolved config
- `soak.stopping` — emitted on SIGTERM/SIGINT before clean shutdown
- `soak.shutdown.error` / `soak.crash` — exporter-flush or unhandled errors

## Verifying ingest

While the harness runs, the analytics store should accumulate rows tagged with `service.name=holdfast-soak`. From `psql` or `clickhouse-client`:

```sql
-- ClickHouse (if Storage:Analytics=ClickHouse)
SELECT count(), max(Timestamp) FROM logs   WHERE ServiceName = 'holdfast-soak';
SELECT count(), max(Timestamp) FROM traces WHERE ServiceName = 'holdfast-soak';

-- Postgres (if Storage:Analytics=Postgres)
SELECT count(*), max(timestamp) FROM analytics.logs   WHERE service_name = 'holdfast-soak';
SELECT count(*), max(timestamp) FROM analytics.traces WHERE service_name = 'holdfast-soak';
```

HOL-41 will deliver a `verify.sh` that runs all these queries and prints a green/red summary.

## 24-hour soak procedure

1. Confirm the stack is on a fresh backend image (rebuild via `docker compose build backend` if changes have landed since last run).
2. Snapshot baseline memory: `docker stats --no-stream > /tmp/soak-start.txt`
3. Start the soak harness with default config: `docker compose --profile soak up -d soak`
4. Let it run for 24 hours. Check `docker logs --tail 50 holdfast-soak` periodically.
5. After 24h, snapshot memory again and compare: `docker stats --no-stream > /tmp/soak-end.txt`
6. Run the verification queries and confirm rows from each scenario type.
7. Stop the harness: `docker compose --profile soak stop soak`.

Memory growth budget: < 50 MiB delta on the backend container over 24h.
Anything more is a regression worth investigating.
