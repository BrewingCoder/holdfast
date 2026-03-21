using Google.Protobuf;
using HoldFast.Api;
using Xunit;

namespace HoldFast.Api.Tests;

/// <summary>
/// Tests for ProtobufOtelParser — binary protobuf (Content-Type: application/x-protobuf)
/// parsing for OTeL logs, traces, and metrics.
///
/// Test payloads are hand-built using Google.Protobuf.CodedOutputStream with OTeL proto
/// field numbers. Field numbering matches opentelemetry-proto spec.
/// </summary>
public class ProtobufOtelParserTests
{
    // ── Proto payload builder helpers ─────────────────────────────────────

    /// <summary>Serialize a message using CodedOutputStream, returning raw bytes.</summary>
    private static byte[] Pb(Action<CodedOutputStream> write)
    {
        var ms = new MemoryStream();
        using var w = new CodedOutputStream(ms, leaveOpen: true);
        write(w);
        w.Flush();
        return ms.ToArray();
    }

    /// <summary>Serialize to ByteString (for use as a sub-message field value).</summary>
    private static ByteString PbMsg(Action<CodedOutputStream> write) =>
        ByteString.CopyFrom(Pb(write));

    // AnyValue: field 1 = string_value
    private static ByteString AnyValueString(string s) => PbMsg(w =>
    {
        w.WriteTag(1, WireFormat.WireType.LengthDelimited);
        w.WriteString(s);
    });

    // AnyValue: field 3 = int_value
    private static ByteString AnyValueInt(long v) => PbMsg(w =>
    {
        w.WriteTag(3, WireFormat.WireType.Varint);
        w.WriteInt64(v);
    });

    // KeyValue: field 1 = key (string), field 2 = value (AnyValue sub-message)
    private static ByteString Kv(string key, string value) => PbMsg(w =>
    {
        w.WriteTag(1, WireFormat.WireType.LengthDelimited);
        w.WriteString(key);
        w.WriteTag(2, WireFormat.WireType.LengthDelimited);
        w.WriteBytes(AnyValueString(value));
    });

    private static ByteString KvInt(string key, long value) => PbMsg(w =>
    {
        w.WriteTag(1, WireFormat.WireType.LengthDelimited);
        w.WriteString(key);
        w.WriteTag(2, WireFormat.WireType.LengthDelimited);
        w.WriteBytes(AnyValueInt(value));
    });

    // Resource: field 1 = attributes (repeated KeyValue)
    private static ByteString Resource(params ByteString[] kvPairs) => PbMsg(w =>
    {
        foreach (var kv in kvPairs)
        {
            w.WriteTag(1, WireFormat.WireType.LengthDelimited);
            w.WriteBytes(kv);
        }
    });

    // ════════════════════════════════════════════════════════════════════
    //  ParseLogs
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void ParseLogs_MinimalValidPayload_ReturnsOneLog()
    {
        // Build: LogRecord → ScopeLogs → ResourceLogs → ExportLogsServiceRequest
        var logRecord = PbMsg(w =>
        {
            // field 1: time_unix_nano (fixed64)
            w.WriteTag(1, WireFormat.WireType.Fixed64);
            w.WriteFixed64(1700000000000000000UL);
            // field 3: severity_text (string)
            w.WriteTag(3, WireFormat.WireType.LengthDelimited);
            w.WriteString("ERROR");
            // field 2: severity_number (int32)
            w.WriteTag(2, WireFormat.WireType.Varint);
            w.WriteInt32(17);
            // field 5: body (AnyValue)
            w.WriteTag(5, WireFormat.WireType.LengthDelimited);
            w.WriteBytes(AnyValueString("something broke"));
        });

        var scopeLogs = PbMsg(w =>
        {
            w.WriteTag(2, WireFormat.WireType.LengthDelimited); // log_records
            w.WriteBytes(logRecord);
        });

        var resource = Resource(
            Kv("service.name", "proto-svc"),
            Kv("service.version", "2.0"),
            Kv("highlight.project_id", "42"),
            Kv("deployment.environment", "staging"));

        var resourceLogs = PbMsg(w =>
        {
            w.WriteTag(1, WireFormat.WireType.LengthDelimited); // resource
            w.WriteBytes(resource);
            w.WriteTag(2, WireFormat.WireType.LengthDelimited); // scope_logs
            w.WriteBytes(scopeLogs);
        });

        var request = Pb(w =>
        {
            w.WriteTag(1, WireFormat.WireType.LengthDelimited); // resource_logs
            w.WriteBytes(resourceLogs);
        });

        var logs = ProtobufOtelParser.ParseLogs(request);

        Assert.NotNull(logs);
        Assert.Single(logs);
        var log = logs[0];
        Assert.Equal(42, log.ProjectId);
        Assert.Equal("proto-svc", log.ServiceName);
        Assert.Equal("2.0", log.ServiceVersion);
        Assert.Equal("staging", log.Environment);
        Assert.Equal("ERROR", log.SeverityText);
        Assert.Equal(17, log.SeverityNumber);
        Assert.Equal("something broke", log.Body);
        Assert.Equal("otel", log.Source);
    }

    [Fact]
    public void ParseLogs_WithTraceIdSpanId_HexEncoded()
    {
        var traceIdBytes = new byte[16];
        traceIdBytes[0] = 0xAB;
        traceIdBytes[15] = 0xCD;

        var spanIdBytes = new byte[8];
        spanIdBytes[0] = 0xFF;

        var logRecord = PbMsg(w =>
        {
            w.WriteTag(1, WireFormat.WireType.Fixed64);
            w.WriteFixed64(1700000000000000000UL);
            w.WriteTag(9, WireFormat.WireType.LengthDelimited);  // trace_id
            w.WriteBytes(ByteString.CopyFrom(traceIdBytes));
            w.WriteTag(10, WireFormat.WireType.LengthDelimited); // span_id
            w.WriteBytes(ByteString.CopyFrom(spanIdBytes));
        });

        var scopeLogs = PbMsg(w =>
        {
            w.WriteTag(2, WireFormat.WireType.LengthDelimited);
            w.WriteBytes(logRecord);
        });

        var resourceLogs = PbMsg(w =>
        {
            w.WriteTag(1, WireFormat.WireType.LengthDelimited);
            w.WriteBytes(Resource());
            w.WriteTag(2, WireFormat.WireType.LengthDelimited);
            w.WriteBytes(scopeLogs);
        });

        var request = Pb(w =>
        {
            w.WriteTag(1, WireFormat.WireType.LengthDelimited);
            w.WriteBytes(resourceLogs);
        });

        var logs = ProtobufOtelParser.ParseLogs(request);
        Assert.NotNull(logs);
        Assert.Single(logs);
        Assert.Equal("ab0000000000000000000000000000cd", logs[0].TraceId);
        // Leading ff in span_id
        Assert.StartsWith("ff", logs[0].SpanId);
    }

    [Fact]
    public void ParseLogs_WithSessionIdAttribute_Extracted()
    {
        var logRecord = PbMsg(w =>
        {
            w.WriteTag(1, WireFormat.WireType.Fixed64);
            w.WriteFixed64(1700000000000000000UL);
            w.WriteTag(5, WireFormat.WireType.LengthDelimited);
            w.WriteBytes(AnyValueString("msg"));
            w.WriteTag(6, WireFormat.WireType.LengthDelimited); // attributes
            w.WriteBytes(Kv("highlight.session_id", "sess-proto-42"));
        });

        var scopeLogs = PbMsg(w =>
        {
            w.WriteTag(2, WireFormat.WireType.LengthDelimited);
            w.WriteBytes(logRecord);
        });

        var resourceLogs = PbMsg(w =>
        {
            w.WriteTag(1, WireFormat.WireType.LengthDelimited);
            w.WriteBytes(Resource());
            w.WriteTag(2, WireFormat.WireType.LengthDelimited);
            w.WriteBytes(scopeLogs);
        });

        var request = Pb(w =>
        {
            w.WriteTag(1, WireFormat.WireType.LengthDelimited);
            w.WriteBytes(resourceLogs);
        });

        var logs = ProtobufOtelParser.ParseLogs(request);
        Assert.NotNull(logs);
        Assert.Equal("sess-proto-42", logs[0].SecureSessionId);
    }

    [Fact]
    public void ParseLogs_UsesObservedTimeWhenTimeMissing()
    {
        // Only set observed_time_unix_nano (field 11), not time_unix_nano (field 1)
        var logRecord = PbMsg(w =>
        {
            w.WriteTag(11, WireFormat.WireType.Fixed64); // observed_time_unix_nano
            w.WriteFixed64(1700000000000000000UL);
            w.WriteTag(5, WireFormat.WireType.LengthDelimited);
            w.WriteBytes(AnyValueString("observed"));
        });

        var scopeLogs = PbMsg(w =>
        {
            w.WriteTag(2, WireFormat.WireType.LengthDelimited);
            w.WriteBytes(logRecord);
        });

        var resourceLogs = PbMsg(w =>
        {
            w.WriteTag(1, WireFormat.WireType.LengthDelimited);
            w.WriteBytes(Resource());
            w.WriteTag(2, WireFormat.WireType.LengthDelimited);
            w.WriteBytes(scopeLogs);
        });

        var request = Pb(w =>
        {
            w.WriteTag(1, WireFormat.WireType.LengthDelimited);
            w.WriteBytes(resourceLogs);
        });

        var logs = ProtobufOtelParser.ParseLogs(request);
        Assert.NotNull(logs);
        var expected = new DateTime(2023, 11, 14, 22, 13, 20, DateTimeKind.Utc);
        Assert.Equal(expected, logs[0].Timestamp);
    }

    [Fact]
    public void ParseLogs_EmptyBytes_ReturnsNull()
    {
        Assert.Null(ProtobufOtelParser.ParseLogs(Array.Empty<byte>()));
    }

    [Fact]
    public void ParseLogs_InvalidBytes_ReturnsNull()
    {
        Assert.Null(ProtobufOtelParser.ParseLogs(new byte[] { 0xFF, 0xFE, 0x01, 0x02 }));
    }

    [Fact]
    public void ParseLogs_NoResourceLogs_ReturnsNull()
    {
        // Field 99 instead of field 1
        var request = Pb(w =>
        {
            w.WriteTag(99, WireFormat.WireType.LengthDelimited);
            w.WriteBytes(ByteString.Empty);
        });

        Assert.Null(ProtobufOtelParser.ParseLogs(request));
    }

    [Fact]
    public void ParseLogs_MultipleLogs_AllReturned()
    {
        var makeLog = (string body) => PbMsg(w =>
        {
            w.WriteTag(1, WireFormat.WireType.Fixed64);
            w.WriteFixed64(1700000000000000000UL);
            w.WriteTag(5, WireFormat.WireType.LengthDelimited);
            w.WriteBytes(AnyValueString(body));
        });

        var scopeLogs = PbMsg(w =>
        {
            w.WriteTag(2, WireFormat.WireType.LengthDelimited);
            w.WriteBytes(makeLog("log-a"));
            w.WriteTag(2, WireFormat.WireType.LengthDelimited);
            w.WriteBytes(makeLog("log-b"));
            w.WriteTag(2, WireFormat.WireType.LengthDelimited);
            w.WriteBytes(makeLog("log-c"));
        });

        var resourceLogs = PbMsg(w =>
        {
            w.WriteTag(1, WireFormat.WireType.LengthDelimited);
            w.WriteBytes(Resource());
            w.WriteTag(2, WireFormat.WireType.LengthDelimited);
            w.WriteBytes(scopeLogs);
        });

        var request = Pb(w =>
        {
            w.WriteTag(1, WireFormat.WireType.LengthDelimited);
            w.WriteBytes(resourceLogs);
        });

        var logs = ProtobufOtelParser.ParseLogs(request);
        Assert.NotNull(logs);
        Assert.Equal(3, logs.Count);
        Assert.Equal("log-a", logs[0].Body);
        Assert.Equal("log-b", logs[1].Body);
        Assert.Equal("log-c", logs[2].Body);
    }

    [Fact]
    public void ParseLogs_MissingSeverityText_DefaultsToInfo()
    {
        var logRecord = PbMsg(w =>
        {
            w.WriteTag(1, WireFormat.WireType.Fixed64);
            w.WriteFixed64(1700000000000000000UL);
            // no severity_text field
            w.WriteTag(5, WireFormat.WireType.LengthDelimited);
            w.WriteBytes(AnyValueString("default severity"));
        });

        var scopeLogs = PbMsg(w =>
        {
            w.WriteTag(2, WireFormat.WireType.LengthDelimited);
            w.WriteBytes(logRecord);
        });

        var resourceLogs = PbMsg(w =>
        {
            w.WriteTag(1, WireFormat.WireType.LengthDelimited);
            w.WriteBytes(Resource());
            w.WriteTag(2, WireFormat.WireType.LengthDelimited);
            w.WriteBytes(scopeLogs);
        });

        var request = Pb(w =>
        {
            w.WriteTag(1, WireFormat.WireType.LengthDelimited);
            w.WriteBytes(resourceLogs);
        });

        var logs = ProtobufOtelParser.ParseLogs(request);
        Assert.NotNull(logs);
        Assert.Equal("INFO", logs[0].SeverityText);
    }

    // ════════════════════════════════════════════════════════════════════
    //  ParseTraces
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void ParseTraces_MinimalValidPayload_ReturnsOneTrace()
    {
        var traceIdBytes = new byte[16];
        traceIdBytes[0] = 0x01;
        var spanIdBytes = new byte[8];
        spanIdBytes[0] = 0x02;
        var parentSpanIdBytes = new byte[8];
        parentSpanIdBytes[0] = 0x03;

        var span = PbMsg(w =>
        {
            w.WriteTag(1, WireFormat.WireType.LengthDelimited); // trace_id
            w.WriteBytes(ByteString.CopyFrom(traceIdBytes));
            w.WriteTag(2, WireFormat.WireType.LengthDelimited); // span_id
            w.WriteBytes(ByteString.CopyFrom(spanIdBytes));
            w.WriteTag(4, WireFormat.WireType.LengthDelimited); // parent_span_id
            w.WriteBytes(ByteString.CopyFrom(parentSpanIdBytes));
            w.WriteTag(5, WireFormat.WireType.LengthDelimited); // name
            w.WriteString("GET /api/health");
            w.WriteTag(6, WireFormat.WireType.Varint); // kind = SERVER
            w.WriteInt32(2);
            w.WriteTag(7, WireFormat.WireType.Fixed64); // start_time_unix_nano
            w.WriteFixed64(1700000000000000000UL);
            w.WriteTag(8, WireFormat.WireType.Fixed64); // end_time_unix_nano
            w.WriteFixed64(1700000001000000000UL);
            // status: field 15 = Status message {field 3 = code OK=1, field 2 = message}
            var status = PbMsg(sw =>
            {
                sw.WriteTag(3, WireFormat.WireType.Varint);
                sw.WriteInt32(1); // OK
                sw.WriteTag(2, WireFormat.WireType.LengthDelimited);
                sw.WriteString("all good");
            });
            w.WriteTag(15, WireFormat.WireType.LengthDelimited);
            w.WriteBytes(status);
        });

        var scopeSpans = PbMsg(w =>
        {
            w.WriteTag(2, WireFormat.WireType.LengthDelimited); // spans
            w.WriteBytes(span);
        });

        var resource = Resource(
            Kv("service.name", "trace-svc"),
            Kv("highlight.project_id", "7"),
            Kv("deployment.environment", "prod"));

        var resourceSpans = PbMsg(w =>
        {
            w.WriteTag(1, WireFormat.WireType.LengthDelimited);
            w.WriteBytes(resource);
            w.WriteTag(2, WireFormat.WireType.LengthDelimited);
            w.WriteBytes(scopeSpans);
        });

        var request = Pb(w =>
        {
            w.WriteTag(1, WireFormat.WireType.LengthDelimited);
            w.WriteBytes(resourceSpans);
        });

        var traces = ProtobufOtelParser.ParseTraces(request);
        Assert.NotNull(traces);
        Assert.Single(traces);
        var t = traces[0];
        Assert.Equal(7, t.ProjectId);
        Assert.Equal("trace-svc", t.ServiceName);
        Assert.Equal("prod", t.Environment);
        Assert.Equal("GET /api/health", t.SpanName);
        Assert.Equal("SERVER", t.SpanKind);
        Assert.Equal("OK", t.StatusCode);
        Assert.Equal("all good", t.StatusMessage);
        Assert.False(t.HasErrors);
        Assert.Equal(1_000_000, t.Duration); // 1 second = 1_000_000 microseconds
    }

    [Fact]
    public void ParseTraces_ErrorStatus_HasErrorsTrue()
    {
        var span = PbMsg(w =>
        {
            w.WriteTag(5, WireFormat.WireType.LengthDelimited);
            w.WriteString("broken-span");
            w.WriteTag(7, WireFormat.WireType.Fixed64);
            w.WriteFixed64(1700000000000000000UL);
            w.WriteTag(8, WireFormat.WireType.Fixed64);
            w.WriteFixed64(1700000001000000000UL);
            var status = PbMsg(sw =>
            {
                sw.WriteTag(3, WireFormat.WireType.Varint);
                sw.WriteInt32(2); // ERROR
                sw.WriteTag(2, WireFormat.WireType.LengthDelimited);
                sw.WriteString("it failed");
            });
            w.WriteTag(15, WireFormat.WireType.LengthDelimited);
            w.WriteBytes(status);
        });

        var request = Pb(w =>
        {
            var rl = PbMsg(rlw =>
            {
                rlw.WriteTag(1, WireFormat.WireType.LengthDelimited);
                rlw.WriteBytes(Resource());
                rlw.WriteTag(2, WireFormat.WireType.LengthDelimited);
                rlw.WriteBytes(PbMsg(sl =>
                {
                    sl.WriteTag(2, WireFormat.WireType.LengthDelimited);
                    sl.WriteBytes(span);
                }));
            });
            w.WriteTag(1, WireFormat.WireType.LengthDelimited);
            w.WriteBytes(rl);
        });

        var traces = ProtobufOtelParser.ParseTraces(request);
        Assert.NotNull(traces);
        Assert.True(traces[0].HasErrors);
        Assert.Equal("ERROR", traces[0].StatusCode);
        Assert.Equal("it failed", traces[0].StatusMessage);
    }

    [Fact]
    public void ParseTraces_SpanKindMapping_AllKinds()
    {
        var kinds = new[] { (1, "INTERNAL"), (2, "SERVER"), (3, "CLIENT"), (4, "PRODUCER"), (5, "CONSUMER") };
        foreach (var (kindNum, expected) in kinds)
        {
            var span = PbMsg(w =>
            {
                w.WriteTag(5, WireFormat.WireType.LengthDelimited);
                w.WriteString("x");
                w.WriteTag(6, WireFormat.WireType.Varint);
                w.WriteInt32(kindNum);
                w.WriteTag(7, WireFormat.WireType.Fixed64);
                w.WriteFixed64(1700000000000000000UL);
                w.WriteTag(8, WireFormat.WireType.Fixed64);
                w.WriteFixed64(1700000000000000000UL);
            });

            var request = Pb(w =>
            {
                var rl = PbMsg(rlw =>
                {
                    rlw.WriteTag(1, WireFormat.WireType.LengthDelimited);
                    rlw.WriteBytes(Resource());
                    rlw.WriteTag(2, WireFormat.WireType.LengthDelimited);
                    rlw.WriteBytes(PbMsg(sl =>
                    {
                        sl.WriteTag(2, WireFormat.WireType.LengthDelimited);
                        sl.WriteBytes(span);
                    }));
                });
                w.WriteTag(1, WireFormat.WireType.LengthDelimited);
                w.WriteBytes(rl);
            });

            var traces = ProtobufOtelParser.ParseTraces(request);
            Assert.NotNull(traces);
            Assert.Equal(expected, traces[0].SpanKind);
        }
    }

    [Fact]
    public void ParseTraces_EmptyBytes_ReturnsNull()
    {
        Assert.Null(ProtobufOtelParser.ParseTraces(Array.Empty<byte>()));
    }

    [Fact]
    public void ParseTraces_InvalidBytes_ReturnsNull()
    {
        // Arbitrary non-protobuf data that will fail parsing
        Assert.Null(ProtobufOtelParser.ParseTraces(new byte[] { 0xFF, 0xFE, 0xFD }));
    }

    [Fact]
    public void ParseTraces_SessionIdFromAttributes()
    {
        var span = PbMsg(w =>
        {
            w.WriteTag(5, WireFormat.WireType.LengthDelimited);
            w.WriteString("sess-span");
            w.WriteTag(7, WireFormat.WireType.Fixed64);
            w.WriteFixed64(1700000000000000000UL);
            w.WriteTag(8, WireFormat.WireType.Fixed64);
            w.WriteFixed64(1700000000000000000UL);
            w.WriteTag(9, WireFormat.WireType.LengthDelimited); // attributes
            w.WriteBytes(Kv("highlight.session_id", "session-xyz"));
        });

        var request = Pb(w =>
        {
            var rl = PbMsg(rlw =>
            {
                rlw.WriteTag(1, WireFormat.WireType.LengthDelimited);
                rlw.WriteBytes(Resource());
                rlw.WriteTag(2, WireFormat.WireType.LengthDelimited);
                rlw.WriteBytes(PbMsg(sl =>
                {
                    sl.WriteTag(2, WireFormat.WireType.LengthDelimited);
                    sl.WriteBytes(span);
                }));
            });
            w.WriteTag(1, WireFormat.WireType.LengthDelimited);
            w.WriteBytes(rl);
        });

        var traces = ProtobufOtelParser.ParseTraces(request);
        Assert.NotNull(traces);
        Assert.Equal("session-xyz", traces[0].SecureSessionId);
    }

    // ════════════════════════════════════════════════════════════════════
    //  ParseMetrics
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void ParseMetrics_GaugeAsDouble_Parsed()
    {
        // NumberDataPoint: field 1=attributes, field 3=time_unix_nano, field 4=as_double
        var dataPoint = PbMsg(w =>
        {
            w.WriteTag(3, WireFormat.WireType.Fixed64); // time_unix_nano
            w.WriteFixed64(1700000000000000000UL);
            w.WriteTag(4, WireFormat.WireType.Fixed64); // as_double (wire type 1 = 64-bit)
            w.WriteDouble(3.14);
        });

        // Gauge: field 1 = data_points (repeated NumberDataPoint)
        var gauge = PbMsg(w =>
        {
            w.WriteTag(1, WireFormat.WireType.LengthDelimited);
            w.WriteBytes(dataPoint);
        });

        // Metric: field 1=name, field 4=gauge
        var metric = PbMsg(w =>
        {
            w.WriteTag(1, WireFormat.WireType.LengthDelimited);
            w.WriteString("cpu.usage");
            w.WriteTag(4, WireFormat.WireType.LengthDelimited);
            w.WriteBytes(gauge);
        });

        var scopeMetrics = PbMsg(w =>
        {
            w.WriteTag(2, WireFormat.WireType.LengthDelimited);
            w.WriteBytes(metric);
        });

        var resourceMetrics = PbMsg(w =>
        {
            w.WriteTag(1, WireFormat.WireType.LengthDelimited);
            w.WriteBytes(Resource());
            w.WriteTag(2, WireFormat.WireType.LengthDelimited);
            w.WriteBytes(scopeMetrics);
        });

        var request = Pb(w =>
        {
            w.WriteTag(1, WireFormat.WireType.LengthDelimited);
            w.WriteBytes(resourceMetrics);
        });

        var metrics = ProtobufOtelParser.ParseMetrics(request);
        Assert.NotNull(metrics);
        Assert.Single(metrics);
        Assert.Equal("cpu.usage", metrics[0].Name);
        Assert.Equal(3.14, metrics[0].Value, precision: 10);
    }

    [Fact]
    public void ParseMetrics_SumAsInt_Parsed()
    {
        var dataPoint = PbMsg(w =>
        {
            w.WriteTag(3, WireFormat.WireType.Fixed64);
            w.WriteFixed64(1700000000000000000UL);
            w.WriteTag(6, WireFormat.WireType.Varint); // as_int (int64)
            w.WriteInt64(1024);
        });

        var sum = PbMsg(w =>
        {
            w.WriteTag(1, WireFormat.WireType.LengthDelimited);
            w.WriteBytes(dataPoint);
        });

        var metric = PbMsg(w =>
        {
            w.WriteTag(1, WireFormat.WireType.LengthDelimited);
            w.WriteString("memory.bytes");
            w.WriteTag(5, WireFormat.WireType.LengthDelimited); // field 5 = sum
            w.WriteBytes(sum);
        });

        var scopeMetrics = PbMsg(w =>
        {
            w.WriteTag(2, WireFormat.WireType.LengthDelimited);
            w.WriteBytes(metric);
        });

        var resourceMetrics = PbMsg(w =>
        {
            w.WriteTag(1, WireFormat.WireType.LengthDelimited);
            w.WriteBytes(Resource(Kv("highlight.project_id", "5")));
            w.WriteTag(2, WireFormat.WireType.LengthDelimited);
            w.WriteBytes(scopeMetrics);
        });

        var request = Pb(w =>
        {
            w.WriteTag(1, WireFormat.WireType.LengthDelimited);
            w.WriteBytes(resourceMetrics);
        });

        var metrics = ProtobufOtelParser.ParseMetrics(request);
        Assert.NotNull(metrics);
        Assert.Single(metrics);
        Assert.Equal("memory.bytes", metrics[0].Name);
        Assert.Equal(1024.0, metrics[0].Value);
    }

    [Fact]
    public void ParseMetrics_HistogramSum_Parsed()
    {
        // HistogramDataPoint: field 3=time_unix_nano, field 5=sum (double)
        var dataPoint = PbMsg(w =>
        {
            w.WriteTag(3, WireFormat.WireType.Fixed64);
            w.WriteFixed64(1700000000000000000UL);
            w.WriteTag(5, WireFormat.WireType.Fixed64); // sum = double
            w.WriteDouble(99.5);
        });

        var histogram = PbMsg(w =>
        {
            w.WriteTag(1, WireFormat.WireType.LengthDelimited);
            w.WriteBytes(dataPoint);
        });

        var metric = PbMsg(w =>
        {
            w.WriteTag(1, WireFormat.WireType.LengthDelimited);
            w.WriteString("request.duration");
            w.WriteTag(9, WireFormat.WireType.LengthDelimited); // field 9 = histogram
            w.WriteBytes(histogram);
        });

        var scopeMetrics = PbMsg(w =>
        {
            w.WriteTag(2, WireFormat.WireType.LengthDelimited);
            w.WriteBytes(metric);
        });

        var resourceMetrics = PbMsg(w =>
        {
            w.WriteTag(1, WireFormat.WireType.LengthDelimited);
            w.WriteBytes(Resource());
            w.WriteTag(2, WireFormat.WireType.LengthDelimited);
            w.WriteBytes(scopeMetrics);
        });

        var request = Pb(w =>
        {
            w.WriteTag(1, WireFormat.WireType.LengthDelimited);
            w.WriteBytes(resourceMetrics);
        });

        var metrics = ProtobufOtelParser.ParseMetrics(request);
        Assert.NotNull(metrics);
        Assert.Single(metrics);
        Assert.Equal("request.duration", metrics[0].Name);
        Assert.Equal(99.5, metrics[0].Value, precision: 10);
    }

    [Fact]
    public void ParseMetrics_DataPointAttributes_ConvertedToTags()
    {
        var dataPoint = PbMsg(w =>
        {
            w.WriteTag(1, WireFormat.WireType.LengthDelimited); // attributes
            w.WriteBytes(Kv("region", "us-east-1"));
            w.WriteTag(1, WireFormat.WireType.LengthDelimited);
            w.WriteBytes(Kv("env", "prod"));
            w.WriteTag(3, WireFormat.WireType.Fixed64);
            w.WriteFixed64(1700000000000000000UL);
            w.WriteTag(4, WireFormat.WireType.Fixed64);
            w.WriteDouble(1.0);
        });

        var gauge = PbMsg(w =>
        {
            w.WriteTag(1, WireFormat.WireType.LengthDelimited);
            w.WriteBytes(dataPoint);
        });

        var metric = PbMsg(w =>
        {
            w.WriteTag(1, WireFormat.WireType.LengthDelimited);
            w.WriteString("m");
            w.WriteTag(4, WireFormat.WireType.LengthDelimited);
            w.WriteBytes(gauge);
        });

        var request = Pb(w =>
        {
            var rm = PbMsg(rmw =>
            {
                rmw.WriteTag(1, WireFormat.WireType.LengthDelimited);
                rmw.WriteBytes(Resource());
                rmw.WriteTag(2, WireFormat.WireType.LengthDelimited);
                rmw.WriteBytes(PbMsg(sm =>
                {
                    sm.WriteTag(2, WireFormat.WireType.LengthDelimited);
                    sm.WriteBytes(metric);
                }));
            });
            w.WriteTag(1, WireFormat.WireType.LengthDelimited);
            w.WriteBytes(rm);
        });

        var metrics = ProtobufOtelParser.ParseMetrics(request);
        Assert.NotNull(metrics);
        Assert.NotNull(metrics[0].Tags);
        Assert.Contains(metrics[0].Tags!, t => t.Name == "region" && t.Value == "us-east-1");
        Assert.Contains(metrics[0].Tags!, t => t.Name == "env" && t.Value == "prod");
    }

    [Fact]
    public void ParseMetrics_EmptyBytes_ReturnsNull()
    {
        Assert.Null(ProtobufOtelParser.ParseMetrics(Array.Empty<byte>()));
    }

    [Fact]
    public void ParseMetrics_InvalidBytes_ReturnsNull()
    {
        Assert.Null(ProtobufOtelParser.ParseMetrics(new byte[] { 0xFF, 0xFE, 0xFD }));
    }

    [Fact]
    public void ParseMetrics_DataPointMissingValue_Skipped()
    {
        // NumberDataPoint without as_double or as_int — hasValue stays false → skipped
        var dataPoint = PbMsg(w =>
        {
            w.WriteTag(3, WireFormat.WireType.Fixed64);
            w.WriteFixed64(1700000000000000000UL);
            // No value field (field 4 or 6)
        });

        var gauge = PbMsg(w =>
        {
            w.WriteTag(1, WireFormat.WireType.LengthDelimited);
            w.WriteBytes(dataPoint);
        });

        var metric = PbMsg(w =>
        {
            w.WriteTag(1, WireFormat.WireType.LengthDelimited);
            w.WriteString("no-value-metric");
            w.WriteTag(4, WireFormat.WireType.LengthDelimited);
            w.WriteBytes(gauge);
        });

        var request = Pb(w =>
        {
            var rm = PbMsg(rmw =>
            {
                rmw.WriteTag(1, WireFormat.WireType.LengthDelimited);
                rmw.WriteBytes(Resource());
                rmw.WriteTag(2, WireFormat.WireType.LengthDelimited);
                rmw.WriteBytes(PbMsg(sm =>
                {
                    sm.WriteTag(2, WireFormat.WireType.LengthDelimited);
                    sm.WriteBytes(metric);
                }));
            });
            w.WriteTag(1, WireFormat.WireType.LengthDelimited);
            w.WriteBytes(rm);
        });

        Assert.Null(ProtobufOtelParser.ParseMetrics(request));
    }

    // ════════════════════════════════════════════════════════════════════
    //  IsProtobufContentType
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void IsProtobufContentType_XProtobuf_ReturnsTrue()
    {
        var ctx = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        ctx.Request.ContentType = "application/x-protobuf";
        Assert.True(OtelEndpoints.IsProtobufContentType(ctx.Request));
    }

    [Fact]
    public void IsProtobufContentType_Json_ReturnsFalse()
    {
        var ctx = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        ctx.Request.ContentType = "application/json";
        Assert.False(OtelEndpoints.IsProtobufContentType(ctx.Request));
    }

    [Fact]
    public void IsProtobufContentType_NullContentType_ReturnsFalse()
    {
        var ctx = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        // ContentType defaults to null
        Assert.False(OtelEndpoints.IsProtobufContentType(ctx.Request));
    }

    [Fact]
    public void IsProtobufContentType_CaseInsensitive_ReturnsTrue()
    {
        var ctx = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        ctx.Request.ContentType = "APPLICATION/X-PROTOBUF";
        Assert.True(OtelEndpoints.IsProtobufContentType(ctx.Request));
    }

    [Fact]
    public void IsProtobufContentType_WithCharset_ReturnsTrue()
    {
        var ctx = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        ctx.Request.ContentType = "application/x-protobuf; charset=binary";
        Assert.True(OtelEndpoints.IsProtobufContentType(ctx.Request));
    }
}
