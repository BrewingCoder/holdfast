using System.Text.Json;
using HoldFast.GraphQL.Public;
using HoldFast.GraphQL.Public.InputTypes;

namespace HoldFast.Api;

/// <summary>
/// OTeL-compatible HTTP endpoints for log, trace, and metric ingestion.
/// Accepts JSON payloads and forwards to Kafka for processing.
/// These complement the GraphQL public API for non-GraphQL SDK clients.
/// </summary>
public static class OtelEndpoints
{
    public static void MapOtelEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/otel/v1").RequireCors("Public");

        group.MapPost("/logs", async (HttpContext ctx, IKafkaProducer kafka, CancellationToken ct) =>
        {
            var logs = await JsonSerializer.DeserializeAsync<LogInput[]>(ctx.Request.Body,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, ct);

            if (logs == null || logs.Length == 0)
                return Results.BadRequest("No logs provided");

            foreach (var log in logs)
                await kafka.ProduceLogAsync(log, ct);

            return Results.Ok(new { accepted = logs.Length });
        });

        group.MapPost("/traces", async (HttpContext ctx, IKafkaProducer kafka, CancellationToken ct) =>
        {
            var traces = await JsonSerializer.DeserializeAsync<TraceInput[]>(ctx.Request.Body,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, ct);

            if (traces == null || traces.Length == 0)
                return Results.BadRequest("No traces provided");

            foreach (var trace in traces)
                await kafka.ProduceTraceAsync(trace, ct);

            return Results.Ok(new { accepted = traces.Length });
        });

        group.MapPost("/metrics", async (HttpContext ctx, IKafkaProducer kafka, CancellationToken ct) =>
        {
            var metrics = await JsonSerializer.DeserializeAsync<MetricInput[]>(ctx.Request.Body,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, ct);

            if (metrics == null || metrics.Length == 0)
                return Results.BadRequest("No metrics provided");

            foreach (var metric in metrics)
                await kafka.ProduceMetricAsync(metric, ct);

            return Results.Ok(new { accepted = metrics.Length });
        });
    }
}
