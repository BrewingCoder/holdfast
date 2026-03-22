using Google.Protobuf;
using HoldFast.GraphQL.Public.InputTypes;

namespace HoldFast.Api;

/// <summary>
/// Parses OTeL binary protobuf payloads (Content-Type: application/x-protobuf).
///
/// Uses Google.Protobuf.CodedInputStream with hardcoded field numbers from the
/// opentelemetry-proto specification. Returns null on any parse failure so
/// callers can fall back gracefully to JSON.
///
/// Field numbers reference: https://github.com/open-telemetry/opentelemetry-proto
/// </summary>
internal static class ProtobufOtelParser
{
    // ── Logs (ExportLogsServiceRequest, field 1 = resource_logs) ─────────

    public static List<LogInput>? ParseLogs(byte[] data)
    {
        try
        {
            var logs = new List<LogInput>();
            var input = new CodedInputStream(data);
            while (!input.IsAtEnd)
            {
                var tag = input.ReadTag();
                if (tag == 0) break;
                if (WireFormat.GetTagFieldNumber(tag) == 1)
                    ParseResourceLogs(input.ReadBytes().ToByteArray(), logs);
                else
                    input.SkipLastField();
            }
            return logs.Count > 0 ? logs : null;
        }
        catch { return null; }
    }

    private static void ParseResourceLogs(byte[] data, List<LogInput> logs)
    {
        var input = new CodedInputStream(data);
        string svcName = "", svcVer = "", env = "";
        int projectId = 0;
        var scopeData = new List<byte[]>();

        while (!input.IsAtEnd)
        {
            var tag = input.ReadTag();
            if (tag == 0) break;
            switch (WireFormat.GetTagFieldNumber(tag))
            {
                case 1:
                    (svcName, svcVer, projectId, env) = ParseResource(input.ReadBytes().ToByteArray());
                    break;
                case 2:
                    scopeData.Add(input.ReadBytes().ToByteArray());
                    break;
                default:
                    input.SkipLastField();
                    break;
            }
        }

        foreach (var sd in scopeData)
            ParseScopeLogs(sd, svcName, svcVer, projectId, env, logs);
    }

    private static void ParseScopeLogs(byte[] data,
        string svcName, string svcVer, int projectId, string env,
        List<LogInput> logs)
    {
        var input = new CodedInputStream(data);
        while (!input.IsAtEnd)
        {
            var tag = input.ReadTag();
            if (tag == 0) break;
            if (WireFormat.GetTagFieldNumber(tag) == 2)
            {
                var log = ParseLogRecord(input.ReadBytes().ToByteArray(), svcName, svcVer, projectId, env);
                if (log != null) logs.Add(log);
            }
            else
                input.SkipLastField();
        }
    }

    private static LogInput? ParseLogRecord(byte[] data,
        string svcName, string svcVer, int projectId, string env)
    {
        var input = new CodedInputStream(data);
        ulong timeNano = 0, observedNano = 0;
        int severityNumber = 0;
        string severityText = "", body = "", traceId = "", spanId = "";
        var attrs = new Dictionary<string, string>();

        while (!input.IsAtEnd)
        {
            var tag = input.ReadTag();
            if (tag == 0) break;
            switch (WireFormat.GetTagFieldNumber(tag))
            {
                case 1:  timeNano = input.ReadFixed64(); break;
                case 2:  severityNumber = input.ReadInt32(); break;
                case 3:  severityText = input.ReadString(); break;
                case 5:  body = ReadAnyValueString(input.ReadBytes().ToByteArray()); break;
                case 6:
                    var (k6, v6) = ReadKeyValue(input.ReadBytes().ToByteArray());
                    if (k6 != null && v6 != null) attrs[k6] = v6;
                    break;
                case 9:  traceId = Convert.ToHexStringLower(input.ReadBytes().ToByteArray()); break;
                case 10: spanId = Convert.ToHexStringLower(input.ReadBytes().ToByteArray()); break;
                case 11: observedNano = input.ReadFixed64(); break;
                default: input.SkipLastField(); break;
            }
        }

        var nanos = timeNano > 0 ? timeNano : observedNano;
        var timestamp = nanos > 0
            ? DateTimeOffset.FromUnixTimeMilliseconds((long)(nanos / 1_000_000)).UtcDateTime
            : DateTime.UtcNow;

        return new LogInput(
            ProjectId: projectId,
            Timestamp: timestamp,
            TraceId: traceId,
            SpanId: spanId,
            SecureSessionId: attrs.GetValueOrDefault("highlight.session_id") ?? "",
            SeverityText: string.IsNullOrEmpty(severityText) ? "INFO" : severityText,
            SeverityNumber: severityNumber,
            Source: "otel",
            ServiceName: svcName,
            ServiceVersion: svcVer,
            Body: body,
            LogAttributes: attrs,
            Environment: env);
    }

    // ── Traces (ExportTraceServiceRequest, field 1 = resource_spans) ─────

    public static List<TraceInput>? ParseTraces(byte[] data)
    {
        try
        {
            var traces = new List<TraceInput>();
            var input = new CodedInputStream(data);
            while (!input.IsAtEnd)
            {
                var tag = input.ReadTag();
                if (tag == 0) break;
                if (WireFormat.GetTagFieldNumber(tag) == 1)
                    ParseResourceSpans(input.ReadBytes().ToByteArray(), traces);
                else
                    input.SkipLastField();
            }
            return traces.Count > 0 ? traces : null;
        }
        catch { return null; }
    }

    private static void ParseResourceSpans(byte[] data, List<TraceInput> traces)
    {
        var input = new CodedInputStream(data);
        string svcName = "", svcVer = "", env = "";
        int projectId = 0;
        var scopeData = new List<byte[]>();

        while (!input.IsAtEnd)
        {
            var tag = input.ReadTag();
            if (tag == 0) break;
            switch (WireFormat.GetTagFieldNumber(tag))
            {
                case 1:
                    (svcName, svcVer, projectId, env) = ParseResource(input.ReadBytes().ToByteArray());
                    break;
                case 2:
                    scopeData.Add(input.ReadBytes().ToByteArray());
                    break;
                default:
                    input.SkipLastField();
                    break;
            }
        }

        foreach (var sd in scopeData)
            ParseScopeSpans(sd, svcName, svcVer, projectId, env, traces);
    }

    private static void ParseScopeSpans(byte[] data,
        string svcName, string svcVer, int projectId, string env,
        List<TraceInput> traces)
    {
        var input = new CodedInputStream(data);
        while (!input.IsAtEnd)
        {
            var tag = input.ReadTag();
            if (tag == 0) break;
            if (WireFormat.GetTagFieldNumber(tag) == 2)
            {
                var trace = ParseSpan(input.ReadBytes().ToByteArray(), svcName, svcVer, projectId, env);
                if (trace != null) traces.Add(trace);
            }
            else
                input.SkipLastField();
        }
    }

    private static TraceInput? ParseSpan(byte[] data,
        string svcName, string svcVer, int projectId, string env)
    {
        var input = new CodedInputStream(data);
        string traceId = "", spanId = "", parentSpanId = "", name = "";
        int kind = 0;
        ulong startNano = 0, endNano = 0;
        string statusCode = "UNSET", statusMessage = "";
        var attrs = new Dictionary<string, string>();

        while (!input.IsAtEnd)
        {
            var tag = input.ReadTag();
            if (tag == 0) break;
            switch (WireFormat.GetTagFieldNumber(tag))
            {
                case 1:  traceId = Convert.ToHexStringLower(input.ReadBytes().ToByteArray()); break;
                case 2:  spanId = Convert.ToHexStringLower(input.ReadBytes().ToByteArray()); break;
                case 4:  parentSpanId = Convert.ToHexStringLower(input.ReadBytes().ToByteArray()); break;
                case 5:  name = input.ReadString(); break;
                case 6:  kind = input.ReadInt32(); break;
                case 7:  startNano = input.ReadFixed64(); break;
                case 8:  endNano = input.ReadFixed64(); break;
                case 9:
                    var (k9, v9) = ReadKeyValue(input.ReadBytes().ToByteArray());
                    if (k9 != null && v9 != null) attrs[k9] = v9;
                    break;
                case 15:
                    (statusCode, statusMessage) = ParseStatus(input.ReadBytes().ToByteArray());
                    break;
                default: input.SkipLastField(); break;
            }
        }

        var startTime = startNano > 0
            ? DateTimeOffset.FromUnixTimeMilliseconds((long)(startNano / 1_000_000)).UtcDateTime
            : DateTime.UtcNow;
        var endTime = endNano > 0
            ? DateTimeOffset.FromUnixTimeMilliseconds((long)(endNano / 1_000_000)).UtcDateTime
            : startTime;
        var duration = (long)(endTime - startTime).TotalMicroseconds;

        return new TraceInput(
            ProjectId: projectId,
            Timestamp: startTime,
            TraceId: traceId,
            SpanId: spanId,
            ParentSpanId: parentSpanId,
            SecureSessionId: attrs.GetValueOrDefault("highlight.session_id") ?? "",
            ServiceName: svcName,
            ServiceVersion: svcVer,
            Environment: env,
            SpanName: name,
            SpanKind: SpanKindToString(kind),
            Duration: duration,
            StatusCode: statusCode,
            StatusMessage: statusMessage,
            TraceAttributes: attrs,
            HasErrors: statusCode == "ERROR");
    }

    private static (string Code, string Message) ParseStatus(byte[] data)
    {
        var input = new CodedInputStream(data);
        string code = "UNSET", message = "";
        while (!input.IsAtEnd)
        {
            var tag = input.ReadTag();
            if (tag == 0) break;
            switch (WireFormat.GetTagFieldNumber(tag))
            {
                case 2: message = input.ReadString(); break;
                case 3: code = StatusCodeToString(input.ReadInt32()); break;
                default: input.SkipLastField(); break;
            }
        }
        return (code, message);
    }

    // ── Metrics (ExportMetricsServiceRequest, field 1 = resource_metrics) ─

    public static List<MetricInput>? ParseMetrics(byte[] data)
    {
        try
        {
            var metrics = new List<MetricInput>();
            var input = new CodedInputStream(data);
            while (!input.IsAtEnd)
            {
                var tag = input.ReadTag();
                if (tag == 0) break;
                if (WireFormat.GetTagFieldNumber(tag) == 1)
                    ParseResourceMetrics(input.ReadBytes().ToByteArray(), metrics);
                else
                    input.SkipLastField();
            }
            return metrics.Count > 0 ? metrics : null;
        }
        catch { return null; }
    }

    private static void ParseResourceMetrics(byte[] data, List<MetricInput> metrics)
    {
        var input = new CodedInputStream(data);
        string svcName = "", svcVer = "", env = "";
        int projectId = 0;
        var scopeData = new List<byte[]>();

        while (!input.IsAtEnd)
        {
            var tag = input.ReadTag();
            if (tag == 0) break;
            switch (WireFormat.GetTagFieldNumber(tag))
            {
                case 1:
                    (svcName, svcVer, projectId, env) = ParseResource(input.ReadBytes().ToByteArray());
                    break;
                case 2:
                    scopeData.Add(input.ReadBytes().ToByteArray());
                    break;
                default:
                    input.SkipLastField();
                    break;
            }
        }

        foreach (var sd in scopeData)
            ParseScopeMetrics(sd, projectId, svcName, svcVer, metrics);
    }

    private static void ParseScopeMetrics(byte[] data,
        int projectId, string svcName, string svcVer,
        List<MetricInput> metrics)
    {
        var input = new CodedInputStream(data);
        while (!input.IsAtEnd)
        {
            var tag = input.ReadTag();
            if (tag == 0) break;
            if (WireFormat.GetTagFieldNumber(tag) == 2)
                ParseMetric(input.ReadBytes().ToByteArray(), projectId, svcName, svcVer, metrics);
            else
                input.SkipLastField();
        }
    }

    private static void ParseMetric(byte[] data,
        int projectId, string svcName, string svcVer,
        List<MetricInput> metrics)
    {
        var input = new CodedInputStream(data);
        string name = "";
        var dataPoints = new List<(double Value, DateTime Timestamp, Dictionary<string, string> Attrs)>();

        while (!input.IsAtEnd)
        {
            var tag = input.ReadTag();
            if (tag == 0) break;
            switch (WireFormat.GetTagFieldNumber(tag))
            {
                case 1: name = input.ReadString(); break;
                case 4: // gauge — field 1 = data_points (NumberDataPoint)
                case 5: // sum  — field 1 = data_points (NumberDataPoint)
                    ParseNumberDataPoints(input.ReadBytes().ToByteArray(), dataPoints);
                    break;
                case 9: // histogram — field 1 = data_points (HistogramDataPoint)
                    ParseHistogramDataPoints(input.ReadBytes().ToByteArray(), dataPoints);
                    break;
                default: input.SkipLastField(); break;
            }
        }

        if (string.IsNullOrEmpty(name)) return;

        foreach (var (value, timestamp, attrs) in dataPoints)
        {
            var tags = attrs.Select(a => new MetricTag(a.Key, a.Value)).ToList();
            metrics.Add(new MetricInput(
                SessionSecureId: attrs.GetValueOrDefault("highlight.session_id") ?? "",
                SpanId: null,
                ParentSpanId: null,
                TraceId: null,
                Group: null,
                Name: name,
                Value: value,
                Category: null,
                Timestamp: timestamp,
                Tags: tags));
        }
    }

    // NumberDataPoint: field 1=attributes, 3=time_unix_nano(fixed64),
    //                  4=as_double(double), 6=as_int(int64)
    private static void ParseNumberDataPoints(byte[] data,
        List<(double, DateTime, Dictionary<string, string>)> points)
    {
        var input = new CodedInputStream(data);
        while (!input.IsAtEnd)
        {
            var tag = input.ReadTag();
            if (tag == 0) break;
            if (WireFormat.GetTagFieldNumber(tag) == 1)
            {
                var dp = ReadNumberDataPoint(input.ReadBytes().ToByteArray());
                if (dp.HasValue) points.Add(dp.Value);
            }
            else
                input.SkipLastField();
        }
    }

    // HistogramDataPoint: field 1=attributes, 3=time_unix_nano(fixed64), 5=sum(double)
    private static void ParseHistogramDataPoints(byte[] data,
        List<(double, DateTime, Dictionary<string, string>)> points)
    {
        var input = new CodedInputStream(data);
        while (!input.IsAtEnd)
        {
            var tag = input.ReadTag();
            if (tag == 0) break;
            if (WireFormat.GetTagFieldNumber(tag) == 1)
            {
                var dp = ReadHistogramDataPoint(input.ReadBytes().ToByteArray());
                if (dp.HasValue) points.Add(dp.Value);
            }
            else
                input.SkipLastField();
        }
    }

    private static (double, DateTime, Dictionary<string, string>)? ReadNumberDataPoint(byte[] data)
    {
        var input = new CodedInputStream(data);
        double value = 0;
        ulong timeNano = 0;
        var attrs = new Dictionary<string, string>();
        bool hasValue = false;

        while (!input.IsAtEnd)
        {
            var tag = input.ReadTag();
            if (tag == 0) break;
            switch (WireFormat.GetTagFieldNumber(tag))
            {
                case 1:
                    var (k1, v1) = ReadKeyValue(input.ReadBytes().ToByteArray());
                    if (k1 != null && v1 != null) attrs[k1] = v1;
                    break;
                case 3: timeNano = input.ReadFixed64(); break;
                case 4: value = input.ReadDouble(); hasValue = true; break;
                case 6: value = input.ReadInt64(); hasValue = true; break;
                default: input.SkipLastField(); break;
            }
        }

        if (!hasValue) return null;
        var ts = timeNano > 0
            ? DateTimeOffset.FromUnixTimeMilliseconds((long)(timeNano / 1_000_000)).UtcDateTime
            : DateTime.UtcNow;
        return (value, ts, attrs);
    }

    private static (double, DateTime, Dictionary<string, string>)? ReadHistogramDataPoint(byte[] data)
    {
        var input = new CodedInputStream(data);
        double sum = 0;
        ulong timeNano = 0;
        var attrs = new Dictionary<string, string>();
        bool hasSum = false;

        while (!input.IsAtEnd)
        {
            var tag = input.ReadTag();
            if (tag == 0) break;
            switch (WireFormat.GetTagFieldNumber(tag))
            {
                case 1:
                    var (k1, v1) = ReadKeyValue(input.ReadBytes().ToByteArray());
                    if (k1 != null && v1 != null) attrs[k1] = v1;
                    break;
                case 3: timeNano = input.ReadFixed64(); break;
                case 5: sum = input.ReadDouble(); hasSum = true; break;
                default: input.SkipLastField(); break;
            }
        }

        if (!hasSum) return null;
        var ts = timeNano > 0
            ? DateTimeOffset.FromUnixTimeMilliseconds((long)(timeNano / 1_000_000)).UtcDateTime
            : DateTime.UtcNow;
        return (sum, ts, attrs);
    }

    // ── Common helpers ────────────────────────────────────────────────────

    // Resource: field 1 = attributes (repeated KeyValue)
    private static (string SvcName, string SvcVer, int ProjectId, string Env) ParseResource(byte[] data)
    {
        var input = new CodedInputStream(data);
        string svcName = "", svcVer = "", env = "";
        int projectId = 0;

        while (!input.IsAtEnd)
        {
            var tag = input.ReadTag();
            if (tag == 0) break;
            if (WireFormat.GetTagFieldNumber(tag) == 1)
            {
                var (k, v) = ReadKeyValue(input.ReadBytes().ToByteArray());
                switch (k)
                {
                    case "service.name": svcName = v ?? ""; break;
                    case "service.version": svcVer = v ?? ""; break;
                    case "highlight.project_id" or "highlight_project_id":
                        if (v != null) int.TryParse(v, out projectId);
                        break;
                    case "deployment.environment" or "highlight.environment":
                        env = v ?? "";
                        break;
                }
            }
            else
                input.SkipLastField();
        }

        return (svcName, svcVer, projectId, env);
    }

    // KeyValue: field 1 = key (string), field 2 = value (AnyValue)
    private static (string? Key, string? Value) ReadKeyValue(byte[] data)
    {
        var input = new CodedInputStream(data);
        string? key = null, value = null;
        while (!input.IsAtEnd)
        {
            var tag = input.ReadTag();
            if (tag == 0) break;
            switch (WireFormat.GetTagFieldNumber(tag))
            {
                case 1: key = input.ReadString(); break;
                case 2: value = ReadAnyValueString(input.ReadBytes().ToByteArray()); break;
                default: input.SkipLastField(); break;
            }
        }
        return (key, value);
    }

    // AnyValue: field 1=string, 2=bool, 3=int64, 4=double
    private static string ReadAnyValueString(byte[] data)
    {
        var input = new CodedInputStream(data);
        while (!input.IsAtEnd)
        {
            var tag = input.ReadTag();
            if (tag == 0) break;
            switch (WireFormat.GetTagFieldNumber(tag))
            {
                case 1: return input.ReadString();
                case 2: return input.ReadBool().ToString();
                case 3: return input.ReadInt64().ToString();
                case 4: return input.ReadDouble().ToString();
                default: input.SkipLastField(); break;
            }
        }
        return "";
    }

    private static string SpanKindToString(int kind) => kind switch
    {
        1 => "INTERNAL",
        2 => "SERVER",
        3 => "CLIENT",
        4 => "PRODUCER",
        5 => "CONSUMER",
        _ => "INTERNAL",
    };

    private static string StatusCodeToString(int code) => code switch
    {
        1 => "OK",
        2 => "ERROR",
        _ => "UNSET",
    };
}
