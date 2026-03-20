using HoldFast.Data;
using HoldFast.GraphQL.Private;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// ── Database ──────────────────────────────────────────────────────────
builder.Services.AddDbContextPool<HoldFastDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("PostgreSQL")));

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

// ── GraphQL (Hot Chocolate) ───────────────────────────────────────────
builder.Services
    .AddGraphQLServer()
    .AddQueryType<PrivateQuery>()
    .AddMutationType<PrivateMutation>()
    .AddProjections()
    .AddFiltering()
    .AddSorting()
    .RegisterDbContextFactory<HoldFastDbContext>();

// ── Health checks ─────────────────────────────────────────────────────
builder.Services.AddHealthChecks()
    .AddNpgSql(builder.Configuration.GetConnectionString("PostgreSQL")!);

var app = builder.Build();

// ── Middleware ─────────────────────────────────────────────────────────
app.UseCors("Private");

app.MapHealthChecks("/health");

// Private GraphQL endpoint (dashboard API)
app.MapGraphQL("/private");

// Public GraphQL endpoint (data ingestion) — Phase 2
// app.MapGraphQL("/public").WithOptions(new() { ... });

app.Run();
