# HoldFast soak harness

Long-running container that exercises the analytics-ingest surface
(logs / traces / errors / sessions / metrics / events) with variable-rate
randomized scenarios. Designed to run for ~24 hours alongside the hobby
stack so we can observe real-world ingest pressure.

## Status

Full EPIC ([HOL-37](https://yt.brewingcoder.com/issue/HOL-37)) shipped:

- ✅ HOL-38 — container scaffolding + entrypoint
- ✅ HOL-39 — scenario library
- ✅ HOL-40 — variable-rate scheduler with spike windows
- ✅ HOL-41 — this runbook + `verify.sh`

## Quick start

```bash
cd infra/docker
cp .env.example .env  # if you don't already have one

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

### Core
| Var | Default | Purpose |
|---|---|---|
| `HOLDFAST_PROJECT_ID` | `2` | Project id under which to emit data. Default matches DevSeed's "HoldFast Dev / default" project. |
| `OTLP_ENDPOINT` | `http://backend:8082/otel` | Where the OTel exporters POST. |
| `SOAK_SERVICE_NAME` | `holdfast-soak` | `service.name` resource attribute on every span/log — use this in queries to filter soak rows from real traffic. |
| `SOAK_SERVICE_VERSION` | `0.0.0-soak` | `service.version` resource attribute. |

### Scheduler (HOL-40)
| Var | Default | Purpose |
|---|---|---|
| `SOAK_BASE_INTERVAL_MS` | `60000` | Tick interval outside spike windows. |
| `SOAK_SPIKE_INTERVAL_MS` | `5000` | Tick interval during a spike. |
| `SOAK_SPIKE_MIN_DURATION_MS` | `300000` | Minimum spike length (5 min). |
| `SOAK_SPIKE_MAX_DURATION_MS` | `900000` | Maximum spike length (15 min). |
| `SOAK_SPIKE_MIN_GAP_MS` | `1500000` | Minimum quiet window between spikes (25 min). |
| `SOAK_SPIKE_MAX_GAP_MS` | `2700000` | Maximum quiet window between spikes (45 min). |
| `SOAK_SUMMARY_INTERVAL_MS` | `300000` | Cadence of the per-scenario rollup summary line (5 min). |
| `SOAK_DISABLE_SPIKES` | (unset) | Set to `1` for a constant-rate run with no spike windows. |

### Scenario weights (HOL-39)
| Var | Default | Purpose |
|---|---|---|
| `SOAK_WEIGHTS` | (unset) | Override scenario distribution. Format: `logs=80,traces=10,metrics=5`. Unspecified scenarios keep their defaults (logs 40 / traces 20 / metrics 15 / errors 10 / sessions 10 / events 5). |
| `SOAK_METRIC_EXPORT_MS` | `15000` | Periodic metric export interval (the OTel `MeterProvider`'s push cadence). |

## Output protocol

Every tick (and every event from the scheduler) emits one line of JSON to stdout. Pipe through `jq -c` for filtering:

```bash
docker logs -f holdfast-soak | jq -rc 'select(.event=="soak.summary")'
docker logs -f holdfast-soak | jq -rc 'select(.event=="soak.spike.start" or .event=="soak.spike.end")'
docker logs -f holdfast-soak | jq -rc 'select(.event=="soak.tick" and .burst > 5)'
```

Event taxonomy:

- `soak.started` — boot; carries the resolved config
- `soak.tick` — one tick fired; `burst` is the event count, `scenarios` is the per-event names, `in_spike` flags spike-mode ticks
- `soak.summary` — rollup every `SOAK_SUMMARY_INTERVAL_MS` with per-scenario counts
- `soak.spike.start` / `soak.spike.end` — spike-window state changes
- `soak.scenario.error` — a scenario emit threw (rare; the scheduler catches and continues)
- `soak.stopping` — clean SIGTERM/SIGINT received; flushing exporters
- `soak.shutdown.error` — exporter flush failed during shutdown
- `soak.crash` — unhandled error in the main loop

## 24-hour soak procedure

1. **Snapshot baseline.** Confirm the stack is on a fresh backend image (rebuild via `docker compose build backend` if changes have landed since last run). Capture a memory snapshot for the post-run comparison:
   ```bash
   docker stats --no-stream | tee /tmp/soak-start.txt
   date -u +%Y-%m-%dT%H:%M:%SZ > /tmp/soak-start.iso
   ```
2. **Start the harness** with default config (60s base / 5-15min spikes every 25-45min):
   ```bash
   docker compose -f compose.yml -f compose.hobby-dotnet.yml -f compose.soak.yml \
                  --profile soak up -d soak
   ```
3. **Spot-check** every few hours via `docker logs --tail 50 holdfast-soak`. Look for `soak.summary` lines reporting non-zero counts in every scenario column. If a scenario is at 0 for >2 windows, investigate the OTLP endpoint or backend logs.
4. **After 24 hours** capture the post-run snapshot:
   ```bash
   docker stats --no-stream | tee /tmp/soak-end.txt
   date -u +%Y-%m-%dT%H:%M:%SZ > /tmp/soak-end.iso
   ```
5. **Run verification**:
   ```bash
   ./tests/soak/verify.sh
   ```
   Expect green checkmarks for every scenario type. Any red is worth investigating.
6. **Stop the harness**:
   ```bash
   docker compose --profile soak stop soak
   ```

### Memory budget

After 24h, the **backend** container should grow by less than **50 MiB** (compare `MEM USAGE` columns from start/end). Anything more is a regression worth a profiler run. The **clickhouse** container will grow more (steady write workload), but should plateau, not climb continuously.

## Verification queries

`verify.sh` runs these automatically. To run them manually:

### ClickHouse (Storage:Analytics=ClickHouse, default)

```sql
-- Logs by severity
SELECT SeverityText, count(), max(Timestamp)
FROM logs
WHERE ServiceName = 'holdfast-soak'
GROUP BY SeverityText
ORDER BY count() DESC;

-- Traces (any with the soak service should be present)
SELECT count(), max(Timestamp), uniq(TraceId) AS unique_traces
FROM traces
WHERE ServiceName = 'holdfast-soak';

-- Error groups (looking for the 20 stable groups errors.mjs produces)
SELECT count() AS occurrences,
       any(SeverityText) AS sev,
       any(LogAttributes['error.type']) AS error_type,
       any(LogAttributes['error.group_key']) AS group_key
FROM logs
WHERE ServiceName = 'holdfast-soak'
  AND LogAttributes['log.scenario'] = 'errors'
GROUP BY LogAttributes['error.group_key']
ORDER BY occurrences DESC
LIMIT 25;

-- Sessions
SELECT count(), max(Timestamp), uniq(SpanId) AS unique_session_starts
FROM traces
WHERE ServiceName = 'holdfast-soak'
  AND SpanName = 'session.start';

-- Custom events
SELECT LogAttributes['event.name'] AS name, count()
FROM logs
WHERE ServiceName = 'holdfast-soak'
  AND LogAttributes['log.scenario'] = 'events'
GROUP BY name
ORDER BY count() DESC;
```

### Postgres (Storage:Analytics=Postgres)

Same queries against `analytics.logs` / `analytics.traces` with snake_case column names:

```sql
SELECT severity_text, count(*), max(timestamp)
FROM analytics.logs
WHERE service_name = 'holdfast-soak'
GROUP BY severity_text
ORDER BY count(*) DESC;

SELECT count(*), max(timestamp), count(DISTINCT trace_id) AS unique_traces
FROM analytics.traces
WHERE service_name = 'holdfast-soak';
```

(See `verify.sh` for the full set, parameterized on `STORAGE_ANALYTICS`.)

## Troubleshooting

### `verify.sh` reports zero rows for every scenario

The OTLP endpoint isn't ingesting. Check:
1. `docker logs backend | grep -iE "otel|otlp"` — look for `MapOtelEndpoints` boot messages and any OTLP receiver errors.
2. `docker exec holdfast-soak wget -qO- http://backend:8082/health` — should return `Healthy`. If it fails, the soak container can't reach the backend at all.
3. Confirm `Storage:Analytics` is what you expect — verify queries and ingest path must match (CH vs PG).

### `verify.sh` reports rows for some scenarios but not others

Check the `soak.summary` lines. If a scenario is non-zero in summaries but zero in CH/PG, the backend is dropping that domain's writes. Common causes:
- Metrics: `MetricsConsumer` JSON deserialization mismatch (HOL-23 follow-on issue)
- Sessions/events: Synthesized via traces/logs in this harness — make sure your verification queries match the synthesized shape (see queries above), not the dashboard's session-replay path.

### Container restarts repeatedly

`docker logs holdfast-soak` should show `soak.crash` with a stack trace. The most common cause is an OTel SDK init failure (e.g., DNS resolution for `backend:8082`). Confirm the soak container is on the same compose network.

### Backend memory grows without bound

That's the regression the soak is designed to catch. Capture a `dotnet-counters monitor` snapshot or attach `dotnet-trace`. File it as a HOL ticket with the start/end memory snapshots.

## Containment / safety

- **The harness only writes**, never reads. It's safe to run alongside real traffic on the same project; rows are tagged `service.name=holdfast-soak` and easy to filter out in dashboard queries.
- **No authentication required.** The OTLP path uses the project ID header, no API key. Don't run against a project that's also receiving production traffic if you're worried about cardinality pollution.
- **Bounded cardinality.** Scenarios draw from a small fixed catalog (~5 services, 4 regions, 10 features, 7 routes); 24h of soak shouldn't blow up the analytics catalog tables.
