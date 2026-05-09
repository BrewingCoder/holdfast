using Amazon.S3;
using HoldFast.Api;
using HoldFast.Api.Bootstrap;
using HoldFast.Data;
using HoldFast.Data.ClickHouse;
using HoldFast.GraphQL.Private;
using HoldFast.GraphQL.Public;
using HoldFast.Shared.AlertEvaluation;
using HoldFast.Shared.Auth;
using HoldFast.Shared.Notifications;
using HoldFast.Shared.ErrorGrouping;
using HoldFast.Shared.Kafka;
using HoldFast.Shared.Messaging;
using HoldFast.Shared.Runtime;
using HoldFast.Shared.SessionProcessing;
using HoldFast.Storage;
using HoldFast.Worker;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

// One BackgroundService failing should NOT take down the whole host. The
// .NET default is StopHost — fine for stateless web servers but not for
// HoldFast where workers are independent. A transient ClickHouse glitch in
// SessionEventsConsumer should not crash MetricsConsumer or the public API.
// (HOL-12 follow-on.)
builder.Services.Configure<HostOptions>(o =>
    o.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.Ignore);

// ── Go-compatible environment variable fallbacks ─────────────────────
// The Go backend reads PSQL_HOST, KAFKA_SERVERS, etc. Map them to .NET config.
var pgConnStr = GoEnvCompat.BuildPostgresConnectionString(Environment.GetEnvironmentVariable);
if (pgConnStr != null)
    builder.Configuration["ConnectionStrings:PostgreSQL"] = pgConnStr;

foreach (var (key, value) in GoEnvCompat.GetConfigOverrides(Environment.GetEnvironmentVariable))
    builder.Configuration[key] = value;

// ── Runtime Mode ─────────────────────────────────────────────────────
// Matches Go backend: --runtime=all|graph|public-graph|private-graph|worker
// Also readable from HOLDFAST_RUNTIME env var or appsettings "Runtime" key.
var runtimeValue = builder.Configuration["Runtime"]
                   ?? Environment.GetEnvironmentVariable("HOLDFAST_RUNTIME");
var runtime = RuntimeModeExtensions.Parse(runtimeValue);

Console.WriteLine($"HoldFast starting in '{runtime}' mode");

// ── Database ──────────────────────────────────────────────────────────
builder.Services.AddDbContextPool<HoldFastDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("PostgreSQL")));

// ── HttpContext accessor (needed for ClaimsPrincipal in GraphQL resolvers)
builder.Services.AddHttpContextAccessor();

// ── Self-telemetry (eat our own dogfood) ──────────────────────────────
// Every HoldFast deployment automatically sends its own telemetry to itself.
// OTEL_EXPORTER_OTLP_ENDPOINT defaults to the local backend — in Docker Compose
// this is http://backend:8082 so the backend reports to itself via the public endpoint.
var systemProjectState = new SystemProjectState();
builder.Services.AddSingleton(systemProjectState);
builder.Services.AddHostedService<SystemBootstrapService>();

var otlpEndpoint = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]
    ?? builder.Configuration["Otel:Endpoint"]
    ?? "http://localhost:8082";

// Self-instrumentation sampling — defaults to 5% so dogfood traces don't
// dominate ClickHouse footprint. Set Otel:SampleRatio to 1.0 in dev to see
// every trace, or 0 to disable self-instrumentation entirely. Note that
// inbound /health and /otel/* and outbound OTLP-exporter HTTP calls are
// filtered out unconditionally below — the ratio applies to everything else.
var otelSampleRatio = builder.Configuration.GetValue<double?>("Otel:SampleRatio") ?? 0.05;

builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r
        .AddService("holdfast-backend")
        .AddAttributes([new("deployment.environment", builder.Environment.EnvironmentName)]))
    .WithTracing(tracing => tracing
        .SetSampler(new OpenTelemetry.Trace.TraceIdRatioBasedSampler(otelSampleRatio))
        .AddAspNetCoreInstrumentation(opts => opts.Filter = ctx =>
            // Skip health checks and self-ingestion endpoints from trace noise
            !ctx.Request.Path.StartsWithSegments("/health") &&
            !ctx.Request.Path.StartsWithSegments("/otel"))
        .AddHttpClientInstrumentation(opts => opts.FilterHttpRequestMessage = req =>
            // HOL-18: don't trace the OTLP exporter's own outbound calls.
            // Without this, each trace export to /otel/v1/* becomes a new
            // trace, which is exported, which becomes a new trace — infinite
            // feedback loop that filled ClickHouse with 429K traces against
            // 47 MiB of real data.
            req.RequestUri is null ||
            !req.RequestUri.AbsolutePath.StartsWith("/otel/", StringComparison.OrdinalIgnoreCase))
        .AddOtlpExporter(otlp =>
        {
            otlp.Endpoint = new Uri($"{otlpEndpoint}/otel/v1/traces");
            otlp.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.HttpProtobuf;
            otlp.Headers = $"x-highlight-project={systemProjectState.ProjectId}";
        }))
    .WithMetrics(metrics => metrics
        .AddAspNetCoreInstrumentation()
        .AddRuntimeInstrumentation()
        .AddOtlpExporter(otlp =>
        {
            otlp.Endpoint = new Uri($"{otlpEndpoint}/otel/v1/metrics");
            otlp.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.HttpProtobuf;
            otlp.Headers = $"x-highlight-project={systemProjectState.ProjectId}";
        }));

// ── Dev seed ──────────────────────────────────────────────────────────
builder.Services.Configure<HoldFast.Api.DevSeed.DevSeedOptions>(
    builder.Configuration.GetSection("DevSeed"));
builder.Services.AddHostedService<HoldFast.Api.DevSeed.DevSeedService>();

// ── HTTP context accessor + ClaimsPrincipal DI binding ───────────────
// HC resolves ClaimsPrincipal resolver parameters from DI. Register it as a
// scoped service backed by IHttpContextAccessor so resolvers receive the user
// that AuthMiddleware populated, rather than an empty unauthenticated principal.
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<System.Security.Claims.ClaimsPrincipal>(sp =>
{
    var accessor = sp.GetRequiredService<Microsoft.AspNetCore.Http.IHttpContextAccessor>();
    return accessor.HttpContext?.User ?? new System.Security.Claims.ClaimsPrincipal();
});

// ── Auth ──────────────────────────────────────────────────────────────
builder.Services.Configure<AuthOptions>(
    builder.Configuration.GetSection("Auth"));

var authMode = builder.Configuration["Auth:Mode"] ?? "Password";
builder.Services.AddSingleton<IAuthService>(sp =>
{
    var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<AuthOptions>>();
    return authMode.Equals("Simple", StringComparison.OrdinalIgnoreCase)
        ? new SimpleAuthService(options)
        : new JwtAuthService(options);
});
builder.Services.AddScoped<IAuthorizationService, AuthorizationService>();

// ── Message bus ──────────────────────────────────────────────────────
// HOL-23: in-process Channel<T>-backed bus replaces Kafka for hobby/lean
// self-hosted deployments. Producer and consumers both run in the same
// .NET host (all-in-one runtime mode); see HoldFast.Shared.Messaging.
// To run the worker in a separate process from the API, swap in a
// Kafka-backed IMessageBus implementation that honors the same shape.
builder.Services.AddSingleton<IMessageBus, InProcessMessageBus>();
builder.Services.AddSingleton<IKafkaProducer, KafkaProducerAdapter>();

// ── ClickHouse ────────────────────────────────────────────────────────
builder.Services.Configure<ClickHouseOptions>(
    builder.Configuration.GetSection("ClickHouse"));

// HOL-25/29-33: ClickHouseService and the seven Postgres-backed
// PostgresXxxStore classes both exist as DI singletons. The seven analytics
// interfaces (ILogStore, ITraceStore, ISessionAnalyticsStore,
// IErrorAnalyticsStore, IMetricStore, IEventFieldStore, IAlertStateStore)
// are bound to one or the other based on a single config knob.
//
// HOL-34 consolidation: previously each store had its own
// Storage:Analytics:<StoreName> override. The single Storage:Analytics knob
// is the supported deploy-time choice; per-domain overrides survive only as
// optional escape hatches for testing mixed configurations.
//
// Default: ClickHouse (zero-config behavior matches the pre-EPIC stack).
// Set Storage:Analytics=Postgres (or env STORAGE__ANALYTICS=Postgres) to
// run the entire analytics layer on PG and drop the ClickHouse container.
builder.Services.AddSingleton<ClickHouseService>();
builder.Services.AddSingleton<IClickHouseService>(sp => sp.GetRequiredService<ClickHouseService>());

builder.Services.AddSingleton<HoldFast.Data.Postgres.PostgresLogStore>();
builder.Services.AddSingleton<HoldFast.Data.Postgres.PostgresTraceStore>();
builder.Services.AddSingleton<HoldFast.Data.Postgres.PostgresSessionAnalyticsStore>();
builder.Services.AddSingleton<HoldFast.Data.Postgres.PostgresErrorAnalyticsStore>();
builder.Services.AddSingleton<HoldFast.Data.Postgres.PostgresMetricStore>();
builder.Services.AddSingleton<HoldFast.Data.Postgres.PostgresEventFieldStore>();
builder.Services.AddSingleton<HoldFast.Data.Postgres.PostgresAlertStateStore>();

// Resolve each interface — top-level Storage:Analytics drives all seven by
// default; the per-domain overrides (Storage:Analytics:<StoreName>) take
// precedence when set, so a mixed config (e.g. logs on PG, everything else
// on CH) still works for testing.
var defaultBackend = builder.Configuration["Storage:Analytics"] ?? "clickhouse";

static bool UsePostgres(IConfiguration config, string overrideKey, string defaultBackend)
{
    var explicitOverride = config[$"Storage:Analytics:{overrideKey}"];
    var effective = !string.IsNullOrEmpty(explicitOverride) ? explicitOverride : defaultBackend;
    return effective.Equals("postgres", StringComparison.OrdinalIgnoreCase);
}

builder.Services.AddSingleton<HoldFast.Analytics.ILogStore>(sp =>
    UsePostgres(builder.Configuration, "LogStore", defaultBackend)
        ? sp.GetRequiredService<HoldFast.Data.Postgres.PostgresLogStore>()
        : sp.GetRequiredService<ClickHouseService>());
builder.Services.AddSingleton<HoldFast.Analytics.ITraceStore>(sp =>
    UsePostgres(builder.Configuration, "TraceStore", defaultBackend)
        ? sp.GetRequiredService<HoldFast.Data.Postgres.PostgresTraceStore>()
        : sp.GetRequiredService<ClickHouseService>());
builder.Services.AddSingleton<HoldFast.Analytics.ISessionAnalyticsStore>(sp =>
    UsePostgres(builder.Configuration, "SessionStore", defaultBackend)
        ? sp.GetRequiredService<HoldFast.Data.Postgres.PostgresSessionAnalyticsStore>()
        : sp.GetRequiredService<ClickHouseService>());
builder.Services.AddSingleton<HoldFast.Analytics.IErrorAnalyticsStore>(sp =>
    UsePostgres(builder.Configuration, "ErrorStore", defaultBackend)
        ? sp.GetRequiredService<HoldFast.Data.Postgres.PostgresErrorAnalyticsStore>()
        : sp.GetRequiredService<ClickHouseService>());
builder.Services.AddSingleton<HoldFast.Analytics.IMetricStore>(sp =>
    UsePostgres(builder.Configuration, "MetricStore", defaultBackend)
        ? sp.GetRequiredService<HoldFast.Data.Postgres.PostgresMetricStore>()
        : sp.GetRequiredService<ClickHouseService>());
builder.Services.AddSingleton<HoldFast.Analytics.IEventFieldStore>(sp =>
    UsePostgres(builder.Configuration, "EventFieldStore", defaultBackend)
        ? sp.GetRequiredService<HoldFast.Data.Postgres.PostgresEventFieldStore>()
        : sp.GetRequiredService<ClickHouseService>());
builder.Services.AddSingleton<HoldFast.Analytics.IAlertStateStore>(sp =>
    UsePostgres(builder.Configuration, "AlertStateStore", defaultBackend)
        ? sp.GetRequiredService<HoldFast.Data.Postgres.PostgresAlertStateStore>()
        : sp.GetRequiredService<ClickHouseService>());

Console.WriteLine($"HoldFast analytics backend: '{defaultBackend}' (override Storage:Analytics)");

// Migration runner — applies src/backend/clickhouse/migrations/*.up.sql at
// startup, idempotently. Disable via ClickHouse__Migrations__Disabled=true
// when the schema is managed externally (Helm pre-job, golang-migrate, etc).
builder.Services.Configure<ClickHouseMigrationOptions>(
    builder.Configuration.GetSection("ClickHouse:Migrations"));
builder.Services.AddHostedService<ClickHouseMigrationService>();

// ── Postgres analytics (HOL-26 scaffolding) ──────────────────────────
// Companion analytics backend; lives alongside ClickHouse rather than
// replacing it (the Storage:Analytics switch in HOL-34 chooses one at
// runtime). For HOL-26 the PG migration runner is the only thing wired
// up — its job is to ensure the analytics schema + schema_migrations
// table exist on a fresh Postgres so HOL-29+ implementations have a
// place to add their per-domain tables.
//
// Disabled by default so existing deployments don't get extra startup
// work; opt in via PostgresAnalytics__Migrations__Disabled=false.
builder.Services.Configure<HoldFast.Data.Postgres.PostgresAnalyticsOptions>(
    builder.Configuration.GetSection("PostgresAnalytics"));
builder.Services.Configure<HoldFast.Data.Postgres.PostgresAnalyticsMigrationOptions>(
    builder.Configuration.GetSection("PostgresAnalytics:Migrations"));
builder.Services.AddHostedService<HoldFast.Data.Postgres.PostgresMigrationService>();

// ── Storage ───────────────────────────────────────────────────────────
builder.Services.Configure<StorageOptions>(
    builder.Configuration.GetSection("Storage"));

var storageType = builder.Configuration["Storage:Type"] ?? "filesystem";
if (storageType.Equals("s3", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddSingleton<IAmazonS3>(_ => new AmazonS3Client());
    builder.Services.AddSingleton<IStorageService, S3StorageService>();
}
else
{
    builder.Services.AddSingleton<IStorageService, FilesystemStorageService>();
}

// ── Business Services ─────────────────────────────────────────────────
builder.Services.AddScoped<IErrorGroupingService, ErrorGroupingService>();
builder.Services.AddScoped<ISessionEventsProcessor, SessionEventsProcessor>();
builder.Services.AddScoped<ISessionProcessingService, SessionProcessingService>();
builder.Services.AddScoped<ISessionInitializationService, SessionInitializationService>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<IAlertEvaluationService, AlertEvaluationService>();
builder.Services.AddHttpClient("AlertWebhooks");

// ── Workers (only in all or worker mode) ──────────────────────────────
if (runtime.IsWorker())
{
    builder.Services.AddSingleton<SessionEventsConsumer>();
    builder.Services.AddHostedService<SessionEventsWorker>();
    builder.Services.AddSingleton<ErrorGroupingConsumer>();
    builder.Services.AddHostedService<ErrorGroupingWorker>();
    builder.Services.AddSingleton<FrontendErrorsConsumer>();
    builder.Services.AddHostedService<FrontendErrorsWorker>();
    builder.Services.AddSingleton<MetricsConsumer>();
    builder.Services.AddHostedService<MetricsWorker>();
    builder.Services.AddSingleton<LogIngestionConsumer>();
    builder.Services.AddHostedService<LogIngestionWorker>();
    builder.Services.AddSingleton<TraceIngestionConsumer>();
    builder.Services.AddHostedService<TraceIngestionWorker>();
    builder.Services.AddHostedService<AutoResolveWorker>();
    builder.Services.AddHostedService<DataRetentionWorker>();
    builder.Services.AddHostedService<DataSyncWorker>();
}

// ── CORS ──────────────────────────────────────────────────────────────
// Frontend:Uri may be a comma-separated list for multi-origin dev setups.
var frontendOrigins = (builder.Configuration["Frontend:Uri"] ?? "http://localhost:3000")
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
builder.Services.AddCors(options =>
{
    if (runtime.IsPrivateGraph())
    {
        options.AddPolicy("Private", policy =>
            policy.WithOrigins(frontendOrigins)
                  .AllowAnyHeader()
                  .AllowAnyMethod()
                  .AllowCredentials());
    }

    if (runtime.IsPublicGraph())
    {
        options.AddPolicy("Public", policy =>
            policy.AllowAnyOrigin()
                  .AllowAnyHeader()
                  .AllowAnyMethod());
    }
});

// ── GraphQL (Hot Chocolate) — conditionally register endpoints ────────
if (runtime.IsPrivateGraph())
{
    builder.Services
        .AddGraphQLServer("private")
        .AddType<TimestampType>()
        .BindRuntimeType<DateTime, TimestampType>()
        .AddQueryType<PrivateQuery>()
        .AddMutationType<PrivateMutation>()
        .AddTypeExtension<HoldFast.GraphQL.Private.Types.ErrorGroupTypeExtension>()
        .AddTypeExtension<HoldFast.GraphQL.Private.Types.ErrorAlertTypeExtension>()
        .AddTypeExtension<HoldFast.GraphQL.Private.Types.SessionAlertTypeExtension>()
        .AddTypeExtension<HoldFast.GraphQL.Private.Types.LogAlertTypeExtension>()
        .AddTypeExtension<HoldFast.GraphQL.Private.Types.MetricMonitorTypeExtension>()
        .AddTypeExtension<HoldFast.GraphQL.Private.Types.SessionTypeExtension>()
        .AddTypeExtension<HoldFast.GraphQL.Private.Types.QueryKeyTypeExtension>()
        .AddTypeExtension<HoldFast.GraphQL.Private.Types.LogRowTypeExtension>()
        .AddTypeExtension<HoldFast.GraphQL.Private.Types.LogConnectionTypeExtension>()
        .AddTypeExtension<HoldFast.GraphQL.Private.Types.TraceRowTypeExtension>()
        .AddTypeExtension<HoldFast.GraphQL.Private.Types.TraceConnectionTypeExtension>()
        .AddTypeExtension<HoldFast.GraphQL.Private.Types.TraceEventTypeExtension>()
        .AddTypeExtension<HoldFast.GraphQL.Private.Types.PageInfoTypeExtension>()
        .AddProjections()
        .AddFiltering()
        .AddSorting()
        .RegisterDbContextFactory<HoldFastDbContext>()
        .AddConvention<HotChocolate.Types.Descriptors.INamingConventions>(_ => new SnakeCaseNamingConventions())
        .TryAddTypeInterceptor<HoldFast.GraphQL.Private.IdFieldTypeInterceptor>()
        .AddHttpRequestInterceptor<UserRequestInterceptor>();
}

if (runtime.IsPublicGraph())
{
    builder.Services
        .AddGraphQLServer("public")
        .AddQueryType<PublicQuery>()
        .AddMutationType<PublicMutation>()
        .RegisterDbContextFactory<HoldFastDbContext>()
        .AddConvention<HotChocolate.Types.Descriptors.INamingConventions>(_ => new SnakeCaseNamingConventions());
}

// ── Health checks ─────────────────────────────────────────────────────
builder.Services.AddHealthChecks()
    .AddNpgSql(builder.Configuration.GetConnectionString("PostgreSQL")!)
    .AddCheck<ClickHouseHealthCheck>("clickhouse");

var app = builder.Build();

// ── Database initialization ────────────────────────────────────────────
// EnsureCreated creates the PostgreSQL schema from the EF model if it doesn't exist.
// In production the schema should already exist from the Go migration system,
// but this ensures a clean bootstrap on first run.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<HoldFastDbContext>();
    await db.Database.EnsureCreatedAsync();
}

// ── Middleware ─────────────────────────────────────────────────────────
app.MapHealthChecks("/health");
app.UseCors(); // Must be before UseMiddleware and MapGraphQL

// HOL-20: serve the SPA frontend bundle from wwwroot. The dedicated nginx
// frontend container was removed in favor of letting Kestrel handle static
// files. UseDefaultFiles maps "/" → "/index.html"; UseStaticFiles handles
// /assets/*, /static/*, etc. The MapFallbackToFile call further down catches
// SPA routes (e.g. /sessions/123) so the SPA's own router can resolve them.
// Backed by /app/wwwroot inside the container; the directory is missing in
// dev runs from `dotnet run`, so guard the registration.
if (Directory.Exists(System.IO.Path.Combine(app.Environment.ContentRootPath, "wwwroot")))
{
    app.UseDefaultFiles();
    app.UseStaticFiles();
}

// Only register auth middleware and endpoints for graph modes
if (runtime.IsPrivateGraph() || runtime.IsPublicGraph())
{
    app.UseMiddleware<AuthMiddleware>();
    app.MapAuthEndpoints();
}

// Private GraphQL endpoint (dashboard API) — CORS: frontend only
if (runtime.IsPrivateGraph())
{
    var privateEndpoint = runtime == RuntimeMode.PrivateGraph ? "/" : "/private";
    app.MapGraphQL(privateEndpoint, "private")
        .RequireCors("Private");
}

// Public GraphQL endpoint (data ingestion) — CORS: any origin (SDKs)
if (runtime.IsPublicGraph())
{
    var publicEndpoint = runtime == RuntimeMode.PublicGraph ? "/" : "/public";
    app.MapGraphQL(publicEndpoint, "public")
        .RequireCors("Public");

    // OTeL-compatible HTTP endpoints for non-GraphQL ingestion
    app.MapOtelEndpoints();
}

// HOL-20: SPA fallback — any unmatched GET hits the SPA index, letting
// React Router handle client-side routes. Registered last so it doesn't
// shadow /health, /private, /public, /otel/*, /api/auth/*, etc.
if (Directory.Exists(System.IO.Path.Combine(app.Environment.ContentRootPath, "wwwroot")))
{
    app.MapFallbackToFile("index.html");
}

app.Run();
