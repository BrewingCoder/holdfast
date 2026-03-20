using HoldFast.Data;
using HoldFast.GraphQL.Private;
using HoldFast.GraphQL.Public;
using HoldFast.Shared.Kafka;
using HoldFast.Shared.Redis;
using HoldFast.Worker;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// ── Database ──────────────────────────────────────────────────────────
builder.Services.AddDbContextPool<HoldFastDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("PostgreSQL")));

// ── Kafka ─────────────────────────────────────────────────────────────
builder.Services.Configure<KafkaOptions>(
    builder.Configuration.GetSection("Kafka"));
builder.Services.AddSingleton<KafkaProducerService>();
builder.Services.AddSingleton<IKafkaProducer, KafkaProducerAdapter>();

// ── Redis ─────────────────────────────────────────────────────────────
builder.Services.Configure<RedisOptions>(
    builder.Configuration.GetSection("Redis"));
builder.Services.AddSingleton<RedisService>();

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
    .AddNpgSql(builder.Configuration.GetConnectionString("PostgreSQL")!);

var app = builder.Build();

// ── Middleware ─────────────────────────────────────────────────────────
app.MapHealthChecks("/health");

// Private GraphQL endpoint (dashboard API) — CORS: frontend only
app.MapGraphQL("/private", "private")
    .RequireCors("Private");

// Public GraphQL endpoint (data ingestion) — CORS: any origin (SDKs)
app.MapGraphQL("/public", "public")
    .RequireCors("Public");

app.Run();
