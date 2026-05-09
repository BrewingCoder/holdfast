using System.IO.Compression;
using System.Text.Json;
using HoldFast.Analytics.Models;
using HoldFast.GraphQL.Public;
using HoldFast.GraphQL.Public.InputTypes;
using HoldFast.Shared.Kafka;
using HoldFast.Shared.Messaging;
using HoldFast.Worker;

namespace HoldFast.Api;

/// <summary>
/// OTeL-compatible HTTP endpoints for log, trace, and metric ingestion.
/// Supports:
/// - Content-Type: application/json (OTeL JSON format + simple array format)
/// - Content-Type: application/x-protobuf (OTeL protobuf binary format)
/// - Content-Encoding: gzip, identity
///
/// The OTeL collector sends ExportLogsServiceRequest/ExportTracesServiceRequest/
/// ExportMetricsServiceRequest payloads in JSON or protobuf. Parsed and forwarded to Kafka.
/// </summary>
public static class OtelEndpoints
{
    internal static readonly JsonSerializerOptions JsonOptions = new()
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

    internal static bool IsProtobufContentType(HttpRequest request) =>
        request.ContentType?.Contains("x-protobuf", StringComparison.OrdinalIgnoreCase) == true;

    private static async Task<IResult> HandleLogs(HttpContext ctx, IKafkaProducer kafka, CancellationToken ct)
    {
        var body = await ReadBodyAsync(ctx.Request);

        // Try protobuf first if content-type indicates it
        List<LogInput>? logs = IsProtobufContentType(ctx.Request)
            ? ProtobufOtelParser.ParseLogs(body)
            : null;

        // Try OTeL ExportLogsServiceRequest JSON format
        if (logs == null || logs.Count == 0)
            logs = ParseOtelLogs(body);

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

        List<TraceInput>? traces = IsProtobufContentType(ctx.Request)
            ? ProtobufOtelParser.ParseTraces(body)
            : null;

        if (traces == null || traces.Count == 0)
            traces = ParseOtelTraces(body);

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

    private static async Task<IResult> HandleMetrics(HttpContext ctx, IMessageBus bus, CancellationToken ct)
    {
        var body = await ReadBodyAsync(ctx.Request);

        // Protobuf path is still narrow (NumberDataPoint only — no histogram
        // bucket fields). Promote each MetricInput to a Gauge MetricsMessage
        // until ProtobufOtelParser is widened. JSON path produces full
        // OTeL-shaped messages directly.
        List<MetricsMessage>? metrics = null;
        if (IsProtobufContentType(ctx.Request))
        {
            var legacyProto = ProtobufOtelParser.ParseMetrics(body);
            if (legacyProto != null)
                metrics = legacyProto.Select(LegacyToGauge).ToList();
        }

        if (metrics == null || metrics.Count == 0)
            metrics = ParseOtelMetricsRich(body);

        if (metrics == null || metrics.Count == 0)
            return Results.BadRequest("No metrics provided");

        foreach (var metric in metrics)
            await bus.PublishAsync(KafkaTopics.Metrics, metric.SecureSessionId, metric, ct);

        return Results.Ok(new { accepted = metrics.Count });
    }

    private static MetricsMessage LegacyToGauge(MetricInput m)
    {
        Dictionary<string, string>? attrs = null;
        if (m.Tags is { Count: > 0 })
        {
            attrs = new Dictionary<string, string>(m.Tags.Count);
            foreach (var t in m.Tags)
                attrs[t.Name] = t.Value;
        }
        return new MetricsMessage(
            ProjectId: 0,
            ServiceName: string.Empty,
            MetricName: m.Name,
            MetricDescription: string.Empty,
            MetricUnit: string.Empty,
            Kind: MetricKind.Gauge,
            StartTimestamp: m.Timestamp,
            Timestamp: m.Timestamp,
            Attributes: attrs,
            SecureSessionId: m.SessionSecureId,
            Value: m.Value,
            AggregationTemporality: 0,
            IsMonotonic: false,
            Count: 0UL,
            Sum: 0.0,
            BucketCounts: null,
            ExplicitBounds: null,
            Min: 0.0,
            Max: 0.0);
    }

    /// <summary>
    /// Read request body with decompression support (gzip, snappy).
    /// </summary>
    internal static async Task<byte[]> ReadBodyAsync(HttpRequest request)
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

            case "snappy":
                var compressed = new MemoryStream();
                await request.Body.CopyToAsync(compressed);
                var decompressed = IronSnappy.Snappy.Decode(compressed.ToArray());
                output.Write(decompressed);
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
    internal static List<LogInput>? ParseOtelLogs(byte[] body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
                return null;

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
    internal static List<TraceInput>? ParseOtelTraces(byte[] body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
                return null;

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
    /// Parse OTeL ExportMetricsServiceRequest JSON into the OTeL-shaped
    /// bus messages MetricsConsumer expects. Replaces the legacy
    /// <see cref="ParseOtelMetrics"/> path which dropped per-kind detail
    /// (no MetricType, no AggregationTemporality, no histogram buckets) and
    /// wrote against an INSERT shape that no longer matched the
    /// metrics_sum schema.
    ///
    /// Structure: { resourceMetrics: [{ scopeMetrics: [{ metrics: [...] }] }] }
    /// Handles Sum, Gauge, Histogram. Summary / ExponentialHistogram are
    /// tolerated but ignored — soak doesn't emit them and they'd need
    /// per-kind table writes.
    /// </summary>
    internal static List<MetricsMessage>? ParseOtelMetricsRich(byte[] body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            if (root.ValueKind != JsonValueKind.Object) return null;
            if (!root.TryGetProperty("resourceMetrics", out var resourceMetrics)) return null;

            var messages = new List<MetricsMessage>();

            foreach (var rm in resourceMetrics.EnumerateArray())
            {
                var (serviceName, _, projectId, _) = ExtractResourceAttributes(rm);
                if (!rm.TryGetProperty("scopeMetrics", out var scopeMetrics)) continue;

                foreach (var sm in scopeMetrics.EnumerateArray())
                {
                    if (!sm.TryGetProperty("metrics", out var metricList)) continue;

                    foreach (var metric in metricList.EnumerateArray())
                    {
                        var name = metric.TryGetProperty("name", out var mn) ? mn.GetString() : null;
                        if (string.IsNullOrEmpty(name)) continue;

                        var description = metric.TryGetProperty("description", out var md)
                            ? md.GetString() ?? string.Empty : string.Empty;
                        var unit = metric.TryGetProperty("unit", out var mu)
                            ? mu.GetString() ?? string.Empty : string.Empty;

                        if (metric.TryGetProperty("sum", out var sumNode))
                        {
                            var aggTemp = sumNode.TryGetProperty("aggregationTemporality", out var at)
                                ? at.GetInt32() : 0;
                            var isMonotonic = sumNode.TryGetProperty("isMonotonic", out var im)
                                && im.GetBoolean();
                            EmitNumberPoints(sumNode, MetricKind.Sum, projectId, serviceName ?? "",
                                name, description, unit, aggTemp, isMonotonic, messages);
                        }
                        else if (metric.TryGetProperty("gauge", out var gaugeNode))
                        {
                            EmitNumberPoints(gaugeNode, MetricKind.Gauge, projectId, serviceName ?? "",
                                name, description, unit, 0, false, messages);
                        }
                        else if (metric.TryGetProperty("histogram", out var histNode))
                        {
                            var aggTemp = histNode.TryGetProperty("aggregationTemporality", out var at)
                                ? at.GetInt32() : 0;
                            EmitHistogramPoints(histNode, projectId, serviceName ?? "",
                                name, description, unit, aggTemp, messages);
                        }
                        // Summary / ExponentialHistogram fall through silently — no
                        // soak workload emits them and they'd require their own writers.
                    }
                }
            }

            return messages.Count > 0 ? messages : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static void EmitNumberPoints(JsonElement node, MetricKind kind,
        int projectId, string serviceName, string name, string description, string unit,
        int aggTemp, bool isMonotonic, List<MetricsMessage> messages)
    {
        if (!node.TryGetProperty("dataPoints", out var dps)) return;
        foreach (var dp in dps.EnumerateArray())
        {
            var value = ReadNumberValue(dp);
            var startTs = ParseTimestamp(dp, "startTimeUnixNano", "timeUnixNano");
            var ts = ParseTimestamp(dp, "timeUnixNano");
            var attrs = ExtractAttributes(dp);
            var sessionId = attrs.GetValueOrDefault("highlight.session_id") ?? string.Empty;

            messages.Add(new MetricsMessage(
                ProjectId: projectId,
                ServiceName: serviceName,
                MetricName: name,
                MetricDescription: description,
                MetricUnit: unit,
                Kind: kind,
                StartTimestamp: startTs,
                Timestamp: ts,
                Attributes: attrs,
                SecureSessionId: sessionId,
                Value: value,
                AggregationTemporality: aggTemp,
                IsMonotonic: isMonotonic,
                Count: 0UL,
                Sum: 0.0,
                BucketCounts: null,
                ExplicitBounds: null,
                Min: 0.0,
                Max: 0.0));
        }
    }

    private static void EmitHistogramPoints(JsonElement node,
        int projectId, string serviceName, string name, string description, string unit,
        int aggTemp, List<MetricsMessage> messages)
    {
        if (!node.TryGetProperty("dataPoints", out var dps)) return;
        foreach (var dp in dps.EnumerateArray())
        {
            var startTs = ParseTimestamp(dp, "startTimeUnixNano", "timeUnixNano");
            var ts = ParseTimestamp(dp, "timeUnixNano");
            var attrs = ExtractAttributes(dp);
            var sessionId = attrs.GetValueOrDefault("highlight.session_id") ?? string.Empty;

            ulong count = 0;
            if (dp.TryGetProperty("count", out var c))
            {
                count = c.ValueKind == JsonValueKind.String && ulong.TryParse(c.GetString(), out var cs)
                    ? cs : c.TryGetUInt64(out var cn) ? cn : 0;
            }

            double sum = dp.TryGetProperty("sum", out var s) && s.ValueKind == JsonValueKind.Number
                ? s.GetDouble() : 0;
            double min = dp.TryGetProperty("min", out var mn) && mn.ValueKind == JsonValueKind.Number
                ? mn.GetDouble() : 0;
            double max = dp.TryGetProperty("max", out var mx) && mx.ValueKind == JsonValueKind.Number
                ? mx.GetDouble() : 0;

            var bucketCounts = new List<ulong>();
            if (dp.TryGetProperty("bucketCounts", out var bcs))
            {
                foreach (var bc in bcs.EnumerateArray())
                {
                    var v = bc.ValueKind == JsonValueKind.String && ulong.TryParse(bc.GetString(), out var bcs2)
                        ? bcs2 : bc.TryGetUInt64(out var bcn) ? bcn : 0;
                    bucketCounts.Add(v);
                }
            }

            var explicitBounds = new List<double>();
            if (dp.TryGetProperty("explicitBounds", out var ebs))
            {
                foreach (var eb in ebs.EnumerateArray())
                    if (eb.ValueKind == JsonValueKind.Number) explicitBounds.Add(eb.GetDouble());
            }

            messages.Add(new MetricsMessage(
                ProjectId: projectId,
                ServiceName: serviceName,
                MetricName: name,
                MetricDescription: description,
                MetricUnit: unit,
                Kind: MetricKind.Histogram,
                StartTimestamp: startTs,
                Timestamp: ts,
                Attributes: attrs,
                SecureSessionId: sessionId,
                Value: 0.0,
                AggregationTemporality: aggTemp,
                IsMonotonic: false,
                Count: count,
                Sum: sum,
                BucketCounts: bucketCounts,
                ExplicitBounds: explicitBounds,
                Min: min,
                Max: max));
        }
    }

    private static double ReadNumberValue(JsonElement dp)
    {
        if (dp.TryGetProperty("asDouble", out var ad) && ad.ValueKind == JsonValueKind.Number)
            return ad.GetDouble();
        if (dp.TryGetProperty("asInt", out var ai))
        {
            if (ai.ValueKind == JsonValueKind.String && long.TryParse(ai.GetString(), out var v))
                return v;
            if (ai.ValueKind == JsonValueKind.Number && ai.TryGetInt64(out var n))
                return n;
        }
        return 0.0;
    }

    /// <summary>
    /// Legacy entry point retained because tests reference it; new code
    /// should call <see cref="ParseOtelMetricsRich"/>. This shim simply
    /// projects the rich messages onto the old narrow record so existing
    /// test assertions still type-check.
    /// </summary>
    internal static List<MetricInput>? ParseOtelMetrics(byte[] body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
                return null;

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

    internal static (string? ServiceName, string? ServiceVersion, int ProjectId, string? Environment)
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

    internal static Dictionary<string, string> ExtractAttributes(JsonElement element)
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

    internal static string? ExtractAttributeValue(JsonElement attr)
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

    internal static string ExtractBody(JsonElement logRecord)
    {
        if (!logRecord.TryGetProperty("body", out var body))
            return "";

        if (body.TryGetProperty("stringValue", out var sv))
            return sv.GetString() ?? "";

        return body.ToString();
    }

    internal static DateTime ParseTimestamp(JsonElement element, params string[] fields)
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

    internal static string ParseSpanKind(JsonElement kind)
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

    internal static string ParseStatusCode(JsonElement code)
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

    internal record MetricDataPoint(double Value, DateTime Timestamp, List<string>? Tags, string? SessionId);

    internal static List<MetricDataPoint> ExtractMetricDataPoints(JsonElement metric)
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
