using System.IO.Compression;
using System.Text.Json;
using HoldFast.GraphQL.Public;
using HoldFast.GraphQL.Public.InputTypes;

namespace HoldFast.Api;

/// <summary>
/// OTeL-compatible HTTP endpoints for log, trace, and metric ingestion.
/// Supports:
/// - Content-Type: application/json (OTeL JSON format + simple array format)
/// - Content-Encoding: gzip, identity
///
/// The OTeL collector sends ExportLogsServiceRequest/ExportTracesServiceRequest/
/// ExportMetricsServiceRequest JSON payloads. These are parsed and forwarded to Kafka.
///
/// Protobuf support (Content-Type: application/x-protobuf) is planned for a future phase
/// once the OpenTelemetry .NET proto package stabilizes.
/// </summary>
public static class OtelEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static void MapOtelEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/otel/v1").RequireCors("Public");

        group.MapPost("/logs", HandleLogs);
        group.MapPost("/traces", HandleTraces);
        group.MapPost("/metrics", HandleMetrics);
    }

    private static async Task<IResult> HandleLogs(HttpContext ctx, IKafkaProducer kafka, CancellationToken ct)
    {
        var body = await ReadBodyAsync(ctx.Request);

        // Try OTeL ExportLogsServiceRequest format first
        var logs = ParseOtelLogs(body);

        // Fall back to simple array format
        if (logs == null || logs.Count == 0)
        {
            var simple = JsonSerializer.Deserialize<LogInput[]>(body, JsonOptions);
            if (simple != null)
                logs = simple.ToList();
        }

        if (logs == null || logs.Count == 0)
            return Results.BadRequest("No logs provided");

        foreach (var log in logs)
            await kafka.ProduceLogAsync(log, ct);

        return Results.Ok(new { accepted = logs.Count });
    }

    private static async Task<IResult> HandleTraces(HttpContext ctx, IKafkaProducer kafka, CancellationToken ct)
    {
        var body = await ReadBodyAsync(ctx.Request);

        var traces = ParseOtelTraces(body);

        if (traces == null || traces.Count == 0)
        {
            var simple = JsonSerializer.Deserialize<TraceInput[]>(body, JsonOptions);
            if (simple != null)
                traces = simple.ToList();
        }

        if (traces == null || traces.Count == 0)
            return Results.BadRequest("No traces provided");

        foreach (var trace in traces)
            await kafka.ProduceTraceAsync(trace, ct);

        return Results.Ok(new { accepted = traces.Count });
    }

    private static async Task<IResult> HandleMetrics(HttpContext ctx, IKafkaProducer kafka, CancellationToken ct)
    {
        var body = await ReadBodyAsync(ctx.Request);

        var metrics = ParseOtelMetrics(body);

        if (metrics == null || metrics.Count == 0)
        {
            var simple = JsonSerializer.Deserialize<MetricInput[]>(body, JsonOptions);
            if (simple != null)
                metrics = simple.ToList();
        }

        if (metrics == null || metrics.Count == 0)
            return Results.BadRequest("No metrics provided");

        foreach (var metric in metrics)
            await kafka.ProduceMetricAsync(metric, ct);

        return Results.Ok(new { accepted = metrics.Count });
    }

    /// <summary>
    /// Read request body with decompression support (gzip, snappy).
    /// </summary>
    private static async Task<byte[]> ReadBodyAsync(HttpRequest request)
    {
        var encoding = request.Headers.ContentEncoding.FirstOrDefault()?.ToLowerInvariant();

        using var output = new MemoryStream();

        switch (encoding)
        {
            case "gzip":
                await using (var gzip = new GZipStream(request.Body, CompressionMode.Decompress))
                {
                    await gzip.CopyToAsync(output);
                }
                break;

            default:
                await request.Body.CopyToAsync(output);
                break;
        }

        return output.ToArray();
    }

    /// <summary>
    /// Parse OTeL ExportLogsServiceRequest JSON format.
    /// Structure: { resourceLogs: [{ scopeLogs: [{ logRecords: [...] }] }] }
    /// </summary>
    private static List<LogInput>? ParseOtelLogs(byte[] body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            if (!root.TryGetProperty("resourceLogs", out var resourceLogs))
                return null;

            var logs = new List<LogInput>();

            foreach (var rl in resourceLogs.EnumerateArray())
            {
                var (serviceName, serviceVersion, projectId, environment) = ExtractResourceAttributes(rl);

                if (!rl.TryGetProperty("scopeLogs", out var scopeLogs)) continue;

                foreach (var sl in scopeLogs.EnumerateArray())
                {
                    if (!sl.TryGetProperty("logRecords", out var logRecords)) continue;

                    foreach (var lr in logRecords.EnumerateArray())
                    {
                        var timestamp = ParseTimestamp(lr, "timeUnixNano", "observedTimeUnixNano");
                        var severityText = lr.TryGetProperty("severityText", out var st) ? st.GetString() : null;
                        var severityNumber = lr.TryGetProperty("severityNumber", out var sn) ? sn.GetInt32() : 0;
                        var bodyText = ExtractBody(lr);
                        var traceId = lr.TryGetProperty("traceId", out var ti) ? ti.GetString() : null;
                        var spanId = lr.TryGetProperty("spanId", out var si) ? si.GetString() : null;
                        var attributes = ExtractAttributes(lr);

                        logs.Add(new LogInput(
                            ProjectId: projectId,
                            Timestamp: timestamp,
                            TraceId: traceId ?? "",
                            SpanId: spanId ?? "",
                            SecureSessionId: attributes.GetValueOrDefault("highlight.session_id") ?? "",
                            SeverityText: severityText ?? "INFO",
                            SeverityNumber: severityNumber,
                            Source: "otel",
                            ServiceName: serviceName ?? "",
                            ServiceVersion: serviceVersion ?? "",
                            Body: bodyText,
                            LogAttributes: attributes,
                            Environment: environment ?? ""));
                    }
                }
            }

            return logs;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Parse OTeL ExportTraceServiceRequest JSON format.
    /// Structure: { resourceSpans: [{ scopeSpans: [{ spans: [...] }] }] }
    /// </summary>
    private static List<TraceInput>? ParseOtelTraces(byte[] body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            if (!root.TryGetProperty("resourceSpans", out var resourceSpans))
                return null;

            var traces = new List<TraceInput>();

            foreach (var rs in resourceSpans.EnumerateArray())
            {
                var (serviceName, serviceVersion, projectId, environment) = ExtractResourceAttributes(rs);

                if (!rs.TryGetProperty("scopeSpans", out var scopeSpans)) continue;

                foreach (var ss in scopeSpans.EnumerateArray())
                {
                    if (!ss.TryGetProperty("spans", out var spans)) continue;

                    foreach (var span in spans.EnumerateArray())
                    {
                        var traceId = span.TryGetProperty("traceId", out var ti) ? ti.GetString() : "";
                        var spanId = span.TryGetProperty("spanId", out var si) ? si.GetString() : "";
                        var parentSpanId = span.TryGetProperty("parentSpanId", out var psi) ? psi.GetString() : "";
                        var spanName = span.TryGetProperty("name", out var n) ? n.GetString() : "";
                        var spanKind = span.TryGetProperty("kind", out var k) ? ParseSpanKind(k) : "INTERNAL";
                        var startTime = ParseTimestamp(span, "startTimeUnixNano");
                        var endTime = ParseTimestamp(span, "endTimeUnixNano");
                        var duration = (long)(endTime - startTime).TotalMicroseconds;
                        var attributes = ExtractAttributes(span);
                        var secureSessionId = attributes.GetValueOrDefault("highlight.session_id") ?? "";

                        var statusCode = "UNSET";
                        var statusMessage = "";
                        var hasErrors = false;
                        if (span.TryGetProperty("status", out var status))
                        {
                            if (status.TryGetProperty("code", out var sc))
                                statusCode = ParseStatusCode(sc);
                            if (status.TryGetProperty("message", out var sm))
                                statusMessage = sm.GetString() ?? "";
                            hasErrors = statusCode == "ERROR";
                        }

                        traces.Add(new TraceInput(
                            ProjectId: projectId,
                            Timestamp: startTime,
                            TraceId: traceId ?? "",
                            SpanId: spanId ?? "",
                            ParentSpanId: parentSpanId ?? "",
                            SecureSessionId: secureSessionId,
                            ServiceName: serviceName ?? "",
                            ServiceVersion: serviceVersion ?? "",
                            Environment: environment ?? "",
                            SpanName: spanName ?? "",
                            SpanKind: spanKind,
                            Duration: duration,
                            StatusCode: statusCode,
                            StatusMessage: statusMessage,
                            TraceAttributes: attributes,
                            HasErrors: hasErrors));
                    }
                }
            }

            return traces;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Parse OTeL ExportMetricsServiceRequest JSON format.
    /// Structure: { resourceMetrics: [{ scopeMetrics: [{ metrics: [...] }] }] }
    /// </summary>
    private static List<MetricInput>? ParseOtelMetrics(byte[] body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            if (!root.TryGetProperty("resourceMetrics", out var resourceMetrics))
                return null;

            var metrics = new List<MetricInput>();

            foreach (var rm in resourceMetrics.EnumerateArray())
            {
                var (serviceName, serviceVersion, projectId, environment) = ExtractResourceAttributes(rm);

                if (!rm.TryGetProperty("scopeMetrics", out var scopeMetrics)) continue;

                foreach (var sm in scopeMetrics.EnumerateArray())
                {
                    if (!sm.TryGetProperty("metrics", out var metricList)) continue;

                    foreach (var metric in metricList.EnumerateArray())
                    {
                        var metricName = metric.TryGetProperty("name", out var mn) ? mn.GetString() : null;
                        if (string.IsNullOrEmpty(metricName)) continue;

                        // Extract data points from sum, gauge, histogram, etc.
                        var dataPoints = ExtractMetricDataPoints(metric);

                        foreach (var dp in dataPoints)
                        {
                            metrics.Add(new MetricInput(
                                SessionSecureId: dp.SessionId ?? "",
                                SpanId: null,
                                ParentSpanId: null,
                                TraceId: null,
                                Group: null,
                                Name: metricName,
                                Value: dp.Value,
                                Category: null,
                                Timestamp: dp.Timestamp,
                                Tags: dp.Tags?.Select(t =>
                                {
                                    var parts = t.Split(':', 2);
                                    return new MetricTag(parts[0], parts.Length > 1 ? parts[1] : "");
                                }).ToList()));
                        }
                    }
                }
            }

            return metrics;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // ── Helper methods ───────────────────────────────────────────────────

    private static (string? ServiceName, string? ServiceVersion, int ProjectId, string? Environment)
        ExtractResourceAttributes(JsonElement resource)
    {
        string? serviceName = null, serviceVersion = null, environment = null;
        int projectId = 0;

        if (!resource.TryGetProperty("resource", out var res)) return (null, null, 0, null);
        if (!res.TryGetProperty("attributes", out var attrs)) return (null, null, 0, null);

        foreach (var attr in attrs.EnumerateArray())
        {
            var key = attr.TryGetProperty("key", out var k) ? k.GetString() : null;
            if (key == null) continue;

            var value = ExtractAttributeValue(attr);

            switch (key)
            {
                case "service.name":
                    serviceName = value;
                    break;
                case "service.version":
                    serviceVersion = value;
                    break;
                case "highlight.project_id" or "highlight_project_id":
                    int.TryParse(value, out projectId);
                    break;
                case "deployment.environment" or "highlight.environment":
                    environment = value;
                    break;
            }
        }

        return (serviceName, serviceVersion, projectId, environment);
    }

    private static Dictionary<string, string> ExtractAttributes(JsonElement element)
    {
        var result = new Dictionary<string, string>();

        if (!element.TryGetProperty("attributes", out var attrs))
            return result;

        foreach (var attr in attrs.EnumerateArray())
        {
            var key = attr.TryGetProperty("key", out var k) ? k.GetString() : null;
            if (key == null) continue;
            var value = ExtractAttributeValue(attr);
            if (value != null)
                result[key] = value;
        }

        return result;
    }

    private static string? ExtractAttributeValue(JsonElement attr)
    {
        if (!attr.TryGetProperty("value", out var val))
            return null;

        if (val.TryGetProperty("stringValue", out var sv))
            return sv.GetString();
        if (val.TryGetProperty("intValue", out var iv))
            return iv.ToString();
        if (val.TryGetProperty("doubleValue", out var dv))
            return dv.ToString();
        if (val.TryGetProperty("boolValue", out var bv))
            return bv.GetBoolean().ToString();

        return val.ToString();
    }

    private static string ExtractBody(JsonElement logRecord)
    {
        if (!logRecord.TryGetProperty("body", out var body))
            return "";

        if (body.TryGetProperty("stringValue", out var sv))
            return sv.GetString() ?? "";

        return body.ToString();
    }

    private static DateTime ParseTimestamp(JsonElement element, params string[] fields)
    {
        foreach (var field in fields)
        {
            if (element.TryGetProperty(field, out var ts))
            {
                if (ts.ValueKind == JsonValueKind.String && long.TryParse(ts.GetString(), out var nanos))
                    return DateTimeOffset.FromUnixTimeMilliseconds(nanos / 1_000_000).UtcDateTime;
                if (ts.ValueKind == JsonValueKind.Number && ts.TryGetInt64(out var nanosNum))
                    return DateTimeOffset.FromUnixTimeMilliseconds(nanosNum / 1_000_000).UtcDateTime;
            }
        }

        return DateTime.UtcNow;
    }

    private static string ParseSpanKind(JsonElement kind)
    {
        if (kind.ValueKind == JsonValueKind.Number)
        {
            return kind.GetInt32() switch
            {
                1 => "INTERNAL",
                2 => "SERVER",
                3 => "CLIENT",
                4 => "PRODUCER",
                5 => "CONSUMER",
                _ => "INTERNAL",
            };
        }

        var str = kind.GetString() ?? "";
        return str.Replace("SPAN_KIND_", "");
    }

    private static string ParseStatusCode(JsonElement code)
    {
        if (code.ValueKind == JsonValueKind.Number)
        {
            return code.GetInt32() switch
            {
                0 => "UNSET",
                1 => "OK",
                2 => "ERROR",
                _ => "UNSET",
            };
        }

        return code.GetString() ?? "UNSET";
    }

    private record MetricDataPoint(double Value, DateTime Timestamp, List<string>? Tags, string? SessionId);

    private static List<MetricDataPoint> ExtractMetricDataPoints(JsonElement metric)
    {
        var points = new List<MetricDataPoint>();

        // Try sum, gauge, histogram in order
        string[] dataFields = ["sum", "gauge", "histogram"];
        foreach (var field in dataFields)
        {
            if (!metric.TryGetProperty(field, out var data)) continue;

            var dpField = field == "histogram" ? "dataPoints" : "dataPoints";
            if (!data.TryGetProperty(dpField, out var dataPoints)) continue;

            foreach (var dp in dataPoints.EnumerateArray())
            {
                var value = 0.0;
                if (dp.TryGetProperty("asDouble", out var ad))
                    value = ad.GetDouble();
                else if (dp.TryGetProperty("asInt", out var ai))
                    value = ai.GetInt64();
                else if (dp.TryGetProperty("sum", out var s))
                    value = s.GetDouble();

                var timestamp = ParseTimestamp(dp, "timeUnixNano");
                var attrs = ExtractAttributes(dp);
                var tags = attrs.Select(a => $"{a.Key}:{a.Value}").ToList();
                var sessionId = attrs.GetValueOrDefault("highlight.session_id");

                points.Add(new MetricDataPoint(value, timestamp, tags, sessionId));
            }
        }

        return points;
    }
}
