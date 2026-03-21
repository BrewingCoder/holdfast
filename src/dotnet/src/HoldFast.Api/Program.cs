using Amazon.S3;
using HoldFast.Api;
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

// ── Dev seed ──────────────────────────────────────────────────────────
builder.Services.Configure<HoldFast.Api.DevSeed.DevSeedOptions>(
    builder.Configuration.GetSection("DevSeed"));
builder.Services.AddHostedService<HoldFast.Api.DevSeed.DevSeedService>();

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
var frontendUri = builder.Configuration["Frontend:Uri"] ?? "http://localhost:3000";
builder.Services.AddCors(options =>
{
    if (runtime.IsPrivateGraph())
    {
        options.AddPolicy("Private", policy =>
            policy.WithOrigins(frontendUri)
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
        .AddProjections()
        .AddFiltering()
        .AddSorting()
        .RegisterDbContextFactory<HoldFastDbContext>();
}

if (runtime.IsPublicGraph())
{
    builder.Services
        .AddGraphQLServer("public")
        .AddQueryType<PublicQuery>()
        .AddMutationType<PublicMutation>()
        .RegisterDbContextFactory<HoldFastDbContext>();
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
