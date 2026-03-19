# Env Package

## Purpose

Central configuration for the entire backend. Loads all environment variables into a single `Configuration` struct at startup via `init()`. Every backend package that needs config reads from `env.Config`. This is the **second most imported package** (82 files depend on it) after `model`.

## Module Path

`github.com/BrewingCoder/holdfast/src/backend/env`

## Files

| File | Lines | Purpose |
|------|-------|---------|
| `environment.go` | 232 | Configuration struct (100 fields), loader, helper functions |
| `environment_test.go` | 23 | 1 test — cookie domain parsing |

## How It Works

```go
// Package init() runs automatically at import time
var Config Configuration

func init() {
    Config.load()
}
```

The `load()` method:
1. Reads all env vars via `os.Environ()`
2. Splits `KEY=VALUE` pairs into a map
3. Uses `mapstructure` to decode the map into the `Configuration` struct
4. Calls `log.Fatal()` if decoding fails (immediate crash)

**No validation, no defaults, no type safety.** All 100 fields are strings. Empty string = not set. Type conversion happens at call sites.

## Environment Variables (100 total)

### Core Infrastructure

| Env Var | Field | Purpose |
|---------|-------|---------|
| `PSQL_HOST` | SQLHost | PostgreSQL host |
| `PSQL_PORT` | SQLPort | PostgreSQL port |
| `PSQL_USER` | SQLUser | PostgreSQL user |
| `PSQL_PASSWORD` | SQLPassword | PostgreSQL password |
| `PSQL_DB` | SQLDatabase | PostgreSQL database name |
| `PSQL_DOCKER_HOST` | SQLDockerHost | PostgreSQL host when running in Docker |
| `CLICKHOUSE_ADDRESS` | ClickhouseAddress | ClickHouse connection |
| `CLICKHOUSE_DATABASE` | ClickhouseDatabase | ClickHouse database |
| `CLICKHOUSE_USERNAME` | ClickhouseUsername | ClickHouse user |
| `CLICKHOUSE_PASSWORD` | ClickhousePassword | ClickHouse password |
| `CLICKHOUSE_USERNAME_READONLY` | ClickhouseUsernameReadOnly | ClickHouse read-only user |
| `CLICKHOUSE_TEST_DATABASE` | ClickhouseTestDatabase | ClickHouse test database |
| `KAFKA_SERVERS` | KafkaServers | Kafka broker addresses |
| `KAFKA_TOPIC` | KafkaTopic | Default Kafka topic |
| `KAFKA_ENV_PREFIX` | KafkaEnvPrefix | Kafka topic prefix |
| `KAFKA_SASL_USERNAME` | KafkaSASLUsername | Kafka SASL auth user |
| `KAFKA_SASL_PASSWORD` | KafkaSASLPassword | Kafka SASL auth password |
| `REDIS_EVENTS_STAGING_ENDPOINT` | RedisEndpoint | Redis connection |
| `REDIS_PASSWORD` | RedisPassword | Redis password |

### Application URLs

| Env Var | Field | Purpose |
|---------|-------|---------|
| `REACT_APP_FRONTEND_URI` | FrontendUri | Frontend dashboard URL |
| `REACT_APP_PRIVATE_GRAPH_URI` | PrivateGraphUri | Private GraphQL endpoint |
| `REACT_APP_PUBLIC_GRAPH_URI` | PublicGraphUri | Public GraphQL endpoint |
| `OTLP_ENDPOINT` | OTLPEndpoint | OpenTelemetry collector |
| `OTLP_DOGFOOD_ENDPOINT` | OTLPDogfoodEndpoint | Self-instrumentation OTLP |
| `PUBLIC_GRAPH_FORWARDER_URL` | ForwarderTargetURL | Ingestion forwarder |
| `LANDING_PAGE_STAGING_URI` | LandingStagingURL | Staging URL (legacy) |

### Auth & Security

| Env Var | Field | Purpose |
|---------|-------|---------|
| `REACT_APP_AUTH_MODE` | AuthMode | Auth mode: password, firebase, simple, oauth |
| `ADMIN_PASSWORD` | AuthAdminPassword | Default admin password (simple auth) |
| `FIREBASE_SECRET` | AuthFirebaseSecret | Firebase service account (legacy) |
| `JWT_ACCESS_SECRET` | AuthJWTAccessToken | JWT signing secret |
| `WHITELISTED_FIREBASE_ACCOUNT` | AuthWhitelistedAccount | Firebase allowlist (legacy) |
| `OAUTH_CLIENT_ID` | OAuthClientID | OIDC client ID |
| `OAUTH_CLIENT_SECRET` | OAuthClientSecret | OIDC client secret |
| `OAUTH_PROVIDER_URL` | OAuthProviderUrl | OIDC provider URL |
| `OAUTH_REDIRECT_URL` | OAuthRedirectUrl | OIDC redirect URL |
| `OAUTH_ALLOWED_DOMAINS` | OAuthAllowedDomains | OIDC allowed email domains |
| `SSL` | SSL | Enable SSL |
| `LICENSE_KEY` | LicenseKey | Enterprise license key |
| `ENTERPRISE_ENV_PUBLIC_KEY` | EnterpriseEnvPublicKey | Enterprise public key (also via -ldflags) |

### Storage

| Env Var | Field | Purpose |
|---------|-------|---------|
| `AWS_S3_BUCKET_NAME_NEW` | AwsS3BucketName | Primary S3 bucket |
| `AWS_S3_SOURCE_MAP_BUCKET_NAME_NEW` | AwsS3SourceMapBucketName | Source maps bucket |
| `AWS_S3_GITHUB_BUCKET_NAME` | AwsS3GithubBucketName | GitHub integration bucket |
| `AWS_S3_RESOURCES_BUCKET` | AwsS3ResourcesBucketName | Resources bucket |
| `AWS_S3_STAGING_BUCKET_NAME` | AwsS3StagingBucketName | Staging bucket |
| `AWS_ROLE_ARN` | AwsRoleArn | AWS IAM role |
| `AWS_CLOUDFRONT_DOMAIN` | AwsCloudfrontDomain | CloudFront CDN domain |
| `AWS_CLOUDFRONT_PRIVATE_KEY` | AwsCloudfrontPrivateKey | CloudFront signing key |
| `AWS_CLOUDFRONT_PUBLIC_KEY_ID` | AwsCloudfrontPublicKeyID | CloudFront key ID |
| `OBJECT_STORAGE_FS` | ObjectStorageFS | Filesystem storage path (alt to S3) |
| `SESSION_FILE_PATH_PREFIX` | SessionFilePathPrefix | Session data path prefix |

### Integrations

| Env Var | Field | Purpose |
|---------|-------|---------|
| `GITHUB_APP_ID` | GithubAppId | GitHub App |
| `GITHUB_CLIENT_ID` | GithubClientId | GitHub OAuth |
| `GITHUB_CLIENT_SECRET` | GithubClientSecret | GitHub OAuth |
| `GITHUB_PRIVATE_KEY` | GithubPrivateKey | GitHub App private key |
| `GITLAB_CLIENT_ID` | GitlabClientId | GitLab OAuth |
| `GITLAB_CLIENT_SECRET` | GitlabClientSecret | GitLab OAuth |
| `JIRA_CLIENT_ID` | JiraClientId | Jira OAuth |
| `JIRA_CLIENT_SECRET` | JiraClientSecret | Jira OAuth |
| `SLACK_CLIENT_ID` | SlackClientId | Slack OAuth |
| `SLACK_CLIENT_SECRET` | SlackClientSecret | Slack OAuth |
| `SLACK_SIGNING_SECRET` | SlackSigningSecret | Slack webhook verification |
| `DISCORD_CLIENT_ID` | DiscordClientId | Discord OAuth |
| `DISCORD_CLIENT_SECRET` | DiscordClientSecret | Discord OAuth |
| `DISCORD_BOT_ID` | DiscordBotId | Discord bot |
| `DISCORD_BOT_SECRET` | DiscordBotSecret | Discord bot |
| `MICROSOFT_TEAMS_BOT_ID` | MicrosoftTeamsBotId | Teams bot |
| `MICROSOFT_TEAMS_BOT_PASSWORD` | MicrosoftTeamsBotPassword | Teams bot |
| `LINEAR_CLIENT_ID` | LinearClientId | Linear OAuth |
| `LINEAR_CLIENT_SECRET` | LinearClientSecret | Linear OAuth |
| `CLICKUP_CLIENT_ID` | ClickUpClientID | ClickUp OAuth |
| `CLICKUP_CLIENT_SECRET` | ClickUpClientSecret | ClickUp OAuth |
| `HEIGHT_CLIENT_ID` | HeightClientId | Height OAuth |
| `HEIGHT_CLIENT_SECRET` | HeightClientSecret | Height OAuth |
| `FRONT_CLIENT_ID` | FrontClientId | Front OAuth |
| `FRONT_CLIENT_SECRET` | FrontClientSecret | Front OAuth |
| `VERCEL_CLIENT_ID` | VercelClientId | Vercel integration |
| `VERCEL_CLIENT_SECRET` | VercelClientSecret | Vercel integration |
| `ZAPIER_INTEGRATION_SIGNING_KEY` | ZapierIntegrationSigningKey | Zapier webhook signing |

### AI & ML

| Env Var | Field | Purpose |
|---------|-------|---------|
| `OPENAI_API_KEY` | OpenAIApiKey | OpenAI (to be replaced with Anthropic) |
| `HUGGINGFACE_API_TOKEN` | HuggingfaceApiToken | HuggingFace embeddings |
| `HUGGINGFACE_MODEL_URL` | HuggingfaceModelUrl | HuggingFace model endpoint |
| `PREDICTIONS_ENDPOINT` | PredictionsEndpoint | ML predictions service |

### Runtime & Ops

| Env Var | Field | Purpose |
|---------|-------|---------|
| `ENVIRONMENT` | Environment | dev, test, production |
| `ON_PREM` | OnPrem | On-premise deployment flag |
| `IN_DOCKER` | InDocker | Running in Docker flag |
| `DOPPLER_CONFIG` | Doppler | Doppler environment name |
| `REACT_APP_COMMIT_SHA` | Version | Build version/commit SHA |
| `RELEASE` | Release | Release identifier |
| `SENDGRID_API_KEY` | SendgridKey | SendGrid for transactional email |
| `EMAIL_OPT_OUT_SALT` | EmailOptOutSalt | HMAC salt for email unsubscribe |
| `DEMO_PROJECT_ID` | DemoProjectID | Demo project (for onboarding) |
| `DISABLE_CORS` | DisableCors | Disable CORS checks |
| `CONSUMER_SPAN_SAMPLING_FRACTION` | ConsumerFraction | Span sampling (inverse: 100 = 1%) |
| `SESSION_RETENTION_DAYS` | SessionRetentionDays | Session retention override |
| `WORKER_MAX_MEMORY_THRESHOLD` | WorkerMaxMemoryThreshold | Worker OOM threshold |
| `DELETE_SESSIONS_ARN` | DeleteSessionsArn | AWS Step Functions ARN |
| `ECS_CONTAINER_METADATA_URI_V4` | ECSContainerMetadataUri | ECS metadata (legacy) |

## Helper Functions

| Function | Purpose |
|----------|---------|
| `IsDevEnv()` | `Environment == "dev"` |
| `IsTestEnv()` | `Environment == "test"` |
| `IsDevOrTestEnv()` | Either dev or test |
| `IsOnPrem()` | `OnPrem == "true"` — always true for HoldFast |
| `IsInDocker()` | `InDocker == "true"` |
| `IsProduction()` | `Doppler` starts with `"prod"` |
| `IsEnterpriseDeploy()` | InDocker AND RuntimeFlag != "all" |
| `EnvironmentName()` | Returns env name, overrides to `"on-prem"` if on-prem |
| `GetFrontendCookieDomain()` | Parses FrontendUri → cookie domain (`.example.com`) |
| `GetEnterpriseEnvPublicKey()` | Returns base64-decoded enterprise public key |
| `ConsumerSpanSamplingRate()` | Parses ConsumerFraction as inverse rate (100 → 0.01) |
| `CopyTo(dest)` | Reflection-based config merge (copies non-empty fields) |

## CLI Flags

Two runtime flags (not env vars) are defined:

| Flag | Default | Options | Purpose |
|------|---------|---------|---------|
| `-runtime` | `all` | all, dev, worker, public-graph, private-graph | Runtime mode selection |
| `-worker-handler` | (empty) | handler function name | Worker-specific handler |

## Dependencies

**What imports this package (82 files):**
- `model/` — database setup
- `main.go` — runtime mode selection
- `worker/` — worker configuration
- All integration packages (GitHub, Slack, Jira, Discord, etc.)
- `clickhouse/`, `redis/`, `kafka-queue/` — connection config
- `storage/` — S3 bucket selection
- `email/` — SendGrid key
- `embeddings/`, `openai_client/` — AI API keys

**What this package imports:**
- `mapstructure` — env var → struct mapping
- `samber/lo` — slice utilities
- `logrus` — logging
- Standard lib only (no internal HoldFast deps)

## Testing

### Current State

1 test, 1 function tested — `TestGetFrontendCookieDomain` with 3 cases (localhost, subdomain, deep subdomain).

### Priority Test Targets

1. **`load()`** — verify mapstructure decoding with mock env vars
2. **Environment detection** — `IsDevEnv`, `IsOnPrem`, `IsProduction`, `IsEnterpriseDeploy`
3. **`ConsumerSpanSamplingRate()`** — valid input, invalid input, empty string
4. **`CopyTo()`** — reflection-based merge, empty vs non-empty fields
5. **`GetEnterpriseEnvPublicKey()`** — base64 decoding, build-time vs runtime key
6. **`GetFrontendCookieDomain()`** — edge cases (IP address, port, invalid URL)

## Gotchas

- **No defaults** — every field defaults to empty string. Code consuming config must handle empty strings. There is no "required vs optional" distinction.
- **No validation** — invalid URLs, malformed numbers, and missing required config are all silent. Failures surface at runtime when the value is actually used.
- **All strings** — even numeric values like ports and sampling rates are strings. Type conversion happens at each usage site.
- **`IsProduction()` uses Doppler** — production detection checks `DOPPLER_CONFIG` prefix, not `ENVIRONMENT`. For HoldFast (no Doppler), this always returns false.
- **Cookie domain parsing** — `GetFrontendCookieDomain()` assumes 2-level TLDs. `.co.uk` will break (returns `.uk` instead).
- **Sampling rate is inverse** — `CONSUMER_SPAN_SAMPLING_FRACTION=100` means sample 1%, not 100%. The function returns `1/N`.
- **Global mutable state** — `Config` is a package-level variable with no synchronization. Don't modify after init.
- **Enterprise key injection** — `EnterpriseEnvPublicKey` can be set via `-ldflags` at build time. The package-level var (line 21) is separate from the struct field.
