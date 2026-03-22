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
using HoldFast.Shared.Runtime;
using HoldFast.Shared.SessionProcessing;
using HoldFast.Shared.Redis;
using HoldFast.Storage;
using HoldFast.Worker;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

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

builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r
        .AddService("holdfast-backend")
        .AddAttributes([new("deployment.environment", builder.Environment.EnvironmentName)]))
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation(opts => opts.Filter = ctx =>
            // Skip health checks and self-ingestion endpoints from trace noise
            !ctx.Request.Path.StartsWithSegments("/health") &&
            !ctx.Request.Path.StartsWithSegments("/otel"))
        .AddHttpClientInstrumentation()
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

// ── Kafka ─────────────────────────────────────────────────────────────
builder.Services.Configure<KafkaOptions>(
    builder.Configuration.GetSection("Kafka"));
builder.Services.AddSingleton<KafkaProducerService>();
builder.Services.AddSingleton<IKafkaProducer, KafkaProducerAdapter>();

// ── Redis ─────────────────────────────────────────────────────────────
builder.Services.Configure<RedisOptions>(
    builder.Configuration.GetSection("Redis"));
builder.Services.AddSingleton<RedisService>();

// ── ClickHouse ────────────────────────────────────────────────────────
builder.Services.Configure<ClickHouseOptions>(
    builder.Configuration.GetSection("ClickHouse"));
builder.Services.AddSingleton<IClickHouseService, ClickHouseService>();

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
        .AddQueryType<PrivateQuery>()
        .AddMutationType<PrivateMutation>()
        .AddTypeExtension<HoldFast.GraphQL.Private.Types.ErrorGroupTypeExtension>()
        .AddProjections()
        .AddFiltering()
        .AddSorting()
        .RegisterDbContextFactory<HoldFastDbContext>()
        .AddConvention<HotChocolate.Types.Descriptors.INamingConventions>(_ => new SnakeCaseNamingConventions())
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

app.Run();
