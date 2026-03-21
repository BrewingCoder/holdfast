using Amazon.S3;
using HoldFast.Api;
using HoldFast.Data;
using HoldFast.Data.ClickHouse;
using HoldFast.GraphQL.Private;
using HoldFast.GraphQL.Public;
using HoldFast.Shared.Auth;
using HoldFast.Shared.Kafka;
using HoldFast.Shared.Redis;
using HoldFast.Storage;
using HoldFast.Worker;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// ── Database ──────────────────────────────────────────────────────────
builder.Services.AddDbContextPool<HoldFastDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("PostgreSQL")));

// ── HttpContext accessor (needed for ClaimsPrincipal in GraphQL resolvers)
builder.Services.AddHttpContextAccessor();

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

// ── Workers (Kafka consumers as BackgroundServices) ───────────────────
builder.Services.AddSingleton<SessionEventsConsumer>();
builder.Services.AddHostedService<SessionEventsWorker>();
builder.Services.AddSingleton<ErrorGroupingConsumer>();
builder.Services.AddHostedService<ErrorGroupingWorker>();
builder.Services.AddSingleton<MetricsConsumer>();
builder.Services.AddHostedService<MetricsWorker>();

// ── CORS ──────────────────────────────────────────────────────────────
var frontendUri = builder.Configuration["Frontend:Uri"] ?? "http://localhost:3000";
builder.Services.AddCors(options =>
{
    options.AddPolicy("Private", policy =>
        policy.WithOrigins(frontendUri)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials());

    options.AddPolicy("Public", policy =>
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod());
});

// ── GraphQL (Hot Chocolate) — Private endpoint (dashboard API) ───────
builder.Services
    .AddGraphQLServer("private")
    .AddQueryType<PrivateQuery>()
    .AddMutationType<PrivateMutation>()
    .AddProjections()
    .AddFiltering()
    .AddSorting()
    .RegisterDbContextFactory<HoldFastDbContext>();

// ── GraphQL (Hot Chocolate) — Public endpoint (SDK data ingestion) ───
builder.Services
    .AddGraphQLServer("public")
    .AddQueryType<PublicQuery>()
    .AddMutationType<PublicMutation>()
    .RegisterDbContextFactory<HoldFastDbContext>();

// ── Health checks ─────────────────────────────────────────────────────
builder.Services.AddHealthChecks()
    .AddNpgSql(builder.Configuration.GetConnectionString("PostgreSQL")!)
    .AddCheck<ClickHouseHealthCheck>("clickhouse");

var app = builder.Build();

// ── Middleware ─────────────────────────────────────────────────────────
app.UseMiddleware<AuthMiddleware>();

app.MapHealthChecks("/health");
app.MapAuthEndpoints();

// Private GraphQL endpoint (dashboard API) — CORS: frontend only
app.MapGraphQL("/private", "private")
    .RequireCors("Private");

// Public GraphQL endpoint (data ingestion) — CORS: any origin (SDKs)
app.MapGraphQL("/public", "public")
    .RequireCors("Public");

app.Run();
