# HoldFast Helm Chart

Self-hosted HoldFast — AGPL-3.0 observability platform — packaged for Kubernetes.

## TL;DR

```bash
helm install holdfast oci://ghcr.io/brewingcoder/charts/holdfast \
  --namespace holdfast --create-namespace \
  --set publicUrl=https://holdfast.example.com \
  --set publicGraphUri=https://holdfast.example.com/public \
  --set privateGraphUri=https://holdfast.example.com/private \
  --set collectorOtlpEndpoint=https://holdfast.example.com/otel \
  --set postgres.auth.password=$(openssl rand -base64 24)
```

## Architecture

Two pods. That's the whole deployment.

| Workload | Image | Role |
|---|---|---|
| `Deployment/<release>-backend` | `holdfast-backend-dotnet` | .NET 10 Kestrel — API + frontend bundle + workers + OTLP receivers (`/otel/v1/{logs,traces,metrics}`) all in one binary |
| `StatefulSet/<release>-postgres` | `timescale/timescaledb-ha:pg16` | Postgres 16 + TimescaleDB extensions, with the full analytics columnar path |

Stripped from the upstream Highlight.io 9-container architecture: Kafka, Zookeeper, Redis, the OpenTelemetry Collector, the Python predictions service, and the nginx frontend container. All folded into the backend or removed.

## Requirements

- Kubernetes 1.27+
- Helm 3.13+
- A StorageClass that supports `ReadWriteOnce` (default works fine; lab clusters can override via `postgres.persistence.storageClassName`)
- An ingress / reverse proxy / Cloudflare tunnel pointing at the backend Service on port 8080 (this chart does not create an `Ingress` — operators wire that up to their preferred edge)

## Required values

These must be set or the chart won't render usefully (the backend can't compute its own URLs):

| Key | Example |
|---|---|
| `publicUrl` | `https://holdfast.example.com` |
| `publicGraphUri` | `https://holdfast.example.com/public` |
| `privateGraphUri` | `https://holdfast.example.com/private` |
| `collectorOtlpEndpoint` | `https://holdfast.example.com/otel` |
| `postgres.auth.password` *or* `postgres.auth.existingSecret` | (set, or referenced existing Secret) |

## Authentication

**v1 of this chart only supports `auth.mode=dev`** — the backend runs with no in-app authentication. **Do not expose to anything beyond a trusted network without fronting it with a zero-trust proxy** (Cloudflare Access, Authelia, oauth2-proxy, etc).

`auth.mode=enterprise` (in-app JWT auth) is planned but not yet wired into the chart.

## Storage backend

- `storage.analytics=Postgres` (default): all analytics paths run through Postgres. The TimescaleDB extensions provide the columnar performance HoldFast needs. **Recommended for most operators.**
- `storage.analytics=ClickHouse`: HoldFast also supports an OTeL-shaped ClickHouse backend, but **this chart does not yet manage the ClickHouse pod**. Bring your own ClickHouse and configure connection via `backend.extraEnv`.

## Bring-your-own Postgres

Set `postgres.enabled=false` and configure `externalPostgres.*`:

```yaml
postgres:
  enabled: false

externalPostgres:
  host: my-pg.example.com
  port: 5432
  user: holdfast
  passwordSecret:
    name: my-existing-secret
    key: password
```

## Lab cluster note

The `values.lab.yaml` file in this directory is **specific to the BrewingCoder microk8s QA cluster**. Operators self-hosting elsewhere should write their own values file (or set on the command line); `values.lab.yaml` is preserved in-tree only because it serves as a working example of overrides.

## Development

```bash
# Render templates without applying (handy for diff'ing against running state):
helm template holdfast . -f values.yaml --set postgres.auth.password=test

# Lint the chart before committing:
helm lint .

# Package for OCI registry distribution:
helm package . -d ../../artifacts/helm
```

## License

[AGPL-3.0](https://github.com/BrewingCoder/holdfast/blob/main/LICENSE).
