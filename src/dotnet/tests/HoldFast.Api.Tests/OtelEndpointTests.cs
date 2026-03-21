using System.IO.Compression;
using System.Text;
using System.Text.Json;
using HoldFast.Api;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace HoldFast.Api.Tests;

/// <summary>
/// Comprehensive tests for OtelEndpoints parsing and helper methods.
/// Tests internal methods exposed via InternalsVisibleTo.
/// </summary>
public class OtelEndpointTests
{
    // ── Helper: Convert JSON string to UTF-8 byte[] ────────────────────

    private static byte[] Json(string json) => Encoding.UTF8.GetBytes(json);

    private static JsonElement ParseElement(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    // ════════════════════════════════════════════════════════════════════
    //  ParseOtelLogs
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void ParseOtelLogs_ValidRequest_ReturnsLogs()
    {
        var body = Json("""
        {
          "resourceLogs": [{
            "resource": {
              "attributes": [
                { "key": "service.name", "value": { "stringValue": "my-svc" } },
                { "key": "service.version", "value": { "stringValue": "1.0.0" } },
                { "key": "highlight.project_id", "value": { "stringValue": "42" } },
                { "key": "deployment.environment", "value": { "stringValue": "staging" } }
              ]
            },
            "scopeLogs": [{
              "logRecords": [{
                "timeUnixNano": "1700000000000000000",
                "severityText": "ERROR",
                "severityNumber": 17,
                "body": { "stringValue": "something broke" },
                "traceId": "abc123",
                "spanId": "def456",
                "attributes": [
                  { "key": "highlight.session_id", "value": { "stringValue": "sess-1" } }
                ]
              }]
            }]
          }]
        }
        """);

        var logs = OtelEndpoints.ParseOtelLogs(body);

        Assert.NotNull(logs);
        Assert.Single(logs);
        var log = logs[0];
        Assert.Equal(42, log.ProjectId);
        Assert.Equal("my-svc", log.ServiceName);
        Assert.Equal("1.0.0", log.ServiceVersion);
        Assert.Equal("staging", log.Environment);
        Assert.Equal("ERROR", log.SeverityText);
        Assert.Equal(17, log.SeverityNumber);
        Assert.Equal("something broke", log.Body);
        Assert.Equal("abc123", log.TraceId);
        Assert.Equal("def456", log.SpanId);
        Assert.Equal("sess-1", log.SecureSessionId);
        Assert.Equal("otel", log.Source);
    }

    [Fact]
    public void ParseOtelLogs_MultipleResourceLogs_ReturnsAll()
    {
        var body = Json("""
        {
          "resourceLogs": [
            {
              "resource": { "attributes": [{ "key": "service.name", "value": { "stringValue": "svc-a" } }] },
              "scopeLogs": [{ "logRecords": [{ "body": { "stringValue": "log-a" } }] }]
            },
            {
              "resource": { "attributes": [{ "key": "service.name", "value": { "stringValue": "svc-b" } }] },
              "scopeLogs": [{ "logRecords": [{ "body": { "stringValue": "log-b" } }] }]
            }
          ]
        }
        """);

        var logs = OtelEndpoints.ParseOtelLogs(body);

        Assert.NotNull(logs);
        Assert.Equal(2, logs.Count);
        Assert.Equal("svc-a", logs[0].ServiceName);
        Assert.Equal("svc-b", logs[1].ServiceName);
    }

    [Fact]
    public void ParseOtelLogs_MissingResourceLogs_ReturnsNull()
    {
        var body = Json("""{ "somethingElse": [] }""");
        var logs = OtelEndpoints.ParseOtelLogs(body);
        Assert.Null(logs);
    }

    [Fact]
    public void ParseOtelLogs_EmptyResourceLogs_ReturnsEmptyList()
    {
        var body = Json("""{ "resourceLogs": [] }""");
        var logs = OtelEndpoints.ParseOtelLogs(body);
        Assert.NotNull(logs);
        Assert.Empty(logs);
    }

    [Fact]
    public void ParseOtelLogs_EmptyLogRecords_ReturnsEmptyList()
    {
        var body = Json("""
        {
          "resourceLogs": [{
            "resource": { "attributes": [] },
            "scopeLogs": [{ "logRecords": [] }]
          }]
        }
        """);

        var logs = OtelEndpoints.ParseOtelLogs(body);
        Assert.NotNull(logs);
        Assert.Empty(logs);
    }

    [Fact]
    public void ParseOtelLogs_MissingScopeLogs_ReturnsEmptyList()
    {
        var body = Json("""
        {
          "resourceLogs": [{
            "resource": { "attributes": [] }
          }]
        }
        """);

        var logs = OtelEndpoints.ParseOtelLogs(body);
        Assert.NotNull(logs);
        Assert.Empty(logs);
    }

    [Fact]
    public void ParseOtelLogs_MissingLogRecordsProperty_SkipsScope()
    {
        var body = Json("""
        {
          "resourceLogs": [{
            "resource": { "attributes": [] },
            "scopeLogs": [{ "noLogRecords": true }]
          }]
        }
        """);

        var logs = OtelEndpoints.ParseOtelLogs(body);
        Assert.NotNull(logs);
        Assert.Empty(logs);
    }

    [Fact]
    public void ParseOtelLogs_MissingOptionalFields_DefaultsApplied()
    {
        var body = Json("""
        {
          "resourceLogs": [{
            "resource": { "attributes": [] },
            "scopeLogs": [{
              "logRecords": [{}]
            }]
          }]
        }
        """);

        var logs = OtelEndpoints.ParseOtelLogs(body);
        Assert.NotNull(logs);
        Assert.Single(logs);
        var log = logs[0];
        Assert.Equal(0, log.ProjectId);
        Assert.Equal("", log.TraceId);
        Assert.Equal("", log.SpanId);
        Assert.Equal("", log.SecureSessionId);
        Assert.Equal("INFO", log.SeverityText);
        Assert.Equal(0, log.SeverityNumber);
        Assert.Equal("", log.Body);
        Assert.Equal("", log.ServiceName);
        Assert.Equal("", log.ServiceVersion);
        Assert.Equal("", log.Environment);
    }

    [Fact]
    public void ParseOtelLogs_MalformedJson_ReturnsNull()
    {
        var body = Json("{ not valid json !!!");
        var logs = OtelEndpoints.ParseOtelLogs(body);
        Assert.Null(logs);
    }

    [Fact]
    public void ParseOtelLogs_EmptyByteArray_ReturnsNull()
    {
        var logs = OtelEndpoints.ParseOtelLogs(Array.Empty<byte>());
        Assert.Null(logs);
    }

    [Fact]
    public void ParseOtelLogs_BodyAsNestedObject_ReturnsJsonString()
    {
        var body = Json("""
        {
          "resourceLogs": [{
            "resource": { "attributes": [] },
            "scopeLogs": [{
              "logRecords": [{
                "body": { "kvlistValue": { "values": [{ "key": "foo", "value": { "stringValue": "bar" } }] } }
              }]
            }]
          }]
        }
        """);

        var logs = OtelEndpoints.ParseOtelLogs(body);
        Assert.NotNull(logs);
        Assert.Single(logs);
        // Body should be the raw JSON string of the nested object
        Assert.Contains("kvlistValue", logs[0].Body);
    }

    [Fact]
    public void ParseOtelLogs_HighlightProjectIdAlternateKey_Parsed()
    {
        var body = Json("""
        {
          "resourceLogs": [{
            "resource": {
              "attributes": [
                { "key": "highlight_project_id", "value": { "stringValue": "99" } }
              ]
            },
            "scopeLogs": [{ "logRecords": [{ "body": { "stringValue": "test" } }] }]
          }]
        }
        """);

        var logs = OtelEndpoints.ParseOtelLogs(body);
        Assert.NotNull(logs);
        Assert.Equal(99, logs[0].ProjectId);
    }

    [Fact]
    public void ParseOtelLogs_HighlightEnvironmentKey_Parsed()
    {
        var body = Json("""
        {
          "resourceLogs": [{
            "resource": {
              "attributes": [
                { "key": "highlight.environment", "value": { "stringValue": "prod" } }
              ]
            },
            "scopeLogs": [{ "logRecords": [{ "body": { "stringValue": "test" } }] }]
          }]
        }
        """);

        var logs = OtelEndpoints.ParseOtelLogs(body);
        Assert.NotNull(logs);
        Assert.Equal("prod", logs[0].Environment);
    }

    [Fact]
    public void ParseOtelLogs_MultipleScopeLogs_AllParsed()
    {
        var body = Json("""
        {
          "resourceLogs": [{
            "resource": { "attributes": [] },
            "scopeLogs": [
              { "logRecords": [{ "body": { "stringValue": "log1" } }] },
              { "logRecords": [{ "body": { "stringValue": "log2" } }] }
            ]
          }]
        }
        """);

        var logs = OtelEndpoints.ParseOtelLogs(body);
        Assert.NotNull(logs);
        Assert.Equal(2, logs.Count);
        Assert.Equal("log1", logs[0].Body);
        Assert.Equal("log2", logs[1].Body);
    }

    // ════════════════════════════════════════════════════════════════════
    //  ParseOtelTraces
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void ParseOtelTraces_ValidRequest_ReturnsTraces()
    {
        var body = Json("""
        {
          "resourceSpans": [{
            "resource": {
              "attributes": [
                { "key": "service.name", "value": { "stringValue": "api" } },
                { "key": "highlight.project_id", "value": { "stringValue": "7" } },
                { "key": "deployment.environment", "value": { "stringValue": "prod" } }
              ]
            },
            "scopeSpans": [{
              "spans": [{
                "traceId": "t1",
                "spanId": "s1",
                "parentSpanId": "ps1",
                "name": "GET /health",
                "kind": 2,
                "startTimeUnixNano": "1700000000000000000",
                "endTimeUnixNano": "1700000001000000000",
                "status": { "code": 1, "message": "all good" },
                "attributes": [
                  { "key": "http.method", "value": { "stringValue": "GET" } }
                ]
              }]
            }]
          }]
        }
        """);

        var traces = OtelEndpoints.ParseOtelTraces(body);

        Assert.NotNull(traces);
        Assert.Single(traces);
        var t = traces[0];
        Assert.Equal(7, t.ProjectId);
        Assert.Equal("api", t.ServiceName);
        Assert.Equal("prod", t.Environment);
        Assert.Equal("t1", t.TraceId);
        Assert.Equal("s1", t.SpanId);
        Assert.Equal("ps1", t.ParentSpanId);
        Assert.Equal("GET /health", t.SpanName);
        Assert.Equal("SERVER", t.SpanKind);
        Assert.Equal("OK", t.StatusCode);
        Assert.Equal("all good", t.StatusMessage);
        Assert.False(t.HasErrors);
        // Duration: 1 second = 1_000_000 microseconds
        Assert.Equal(1_000_000, t.Duration);
    }

    [Fact]
    public void ParseOtelTraces_MissingResourceSpans_ReturnsNull()
    {
        var body = Json("""{ "other": 1 }""");
        Assert.Null(OtelEndpoints.ParseOtelTraces(body));
    }

    [Fact]
    public void ParseOtelTraces_EmptyResourceSpans_ReturnsEmpty()
    {
        var body = Json("""{ "resourceSpans": [] }""");
        var traces = OtelEndpoints.ParseOtelTraces(body);
        Assert.NotNull(traces);
        Assert.Empty(traces);
    }

    [Fact]
    public void ParseOtelTraces_MalformedJson_ReturnsNull()
    {
        Assert.Null(OtelEndpoints.ParseOtelTraces(Json("not json")));
    }

    [Fact]
    public void ParseOtelTraces_EmptyBody_ReturnsNull()
    {
        Assert.Null(OtelEndpoints.ParseOtelTraces(Array.Empty<byte>()));
    }

    [Fact]
    public void ParseOtelTraces_StatusError_HasErrorsTrue()
    {
        var body = Json("""
        {
          "resourceSpans": [{
            "resource": { "attributes": [] },
            "scopeSpans": [{
              "spans": [{
                "name": "fail",
                "startTimeUnixNano": "1700000000000000000",
                "endTimeUnixNano": "1700000000500000000",
                "status": { "code": 2, "message": "boom" }
              }]
            }]
          }]
        }
        """);

        var traces = OtelEndpoints.ParseOtelTraces(body);
        Assert.NotNull(traces);
        var t = traces[0];
        Assert.Equal("ERROR", t.StatusCode);
        Assert.Equal("boom", t.StatusMessage);
        Assert.True(t.HasErrors);
    }

    [Fact]
    public void ParseOtelTraces_MissingStatus_DefaultsToUnset()
    {
        var body = Json("""
        {
          "resourceSpans": [{
            "resource": { "attributes": [] },
            "scopeSpans": [{
              "spans": [{
                "name": "noop",
                "startTimeUnixNano": "1700000000000000000",
                "endTimeUnixNano": "1700000000000000000"
              }]
            }]
          }]
        }
        """);

        var traces = OtelEndpoints.ParseOtelTraces(body);
        Assert.NotNull(traces);
        Assert.Equal("UNSET", traces[0].StatusCode);
        Assert.False(traces[0].HasErrors);
    }

    [Fact]
    public void ParseOtelTraces_MissingParentSpanId_DefaultsToEmpty()
    {
        var body = Json("""
        {
          "resourceSpans": [{
            "resource": { "attributes": [] },
            "scopeSpans": [{
              "spans": [{
                "name": "root",
                "startTimeUnixNano": "1700000000000000000",
                "endTimeUnixNano": "1700000000000000000"
              }]
            }]
          }]
        }
        """);

        var traces = OtelEndpoints.ParseOtelTraces(body);
        Assert.NotNull(traces);
        Assert.Equal("", traces[0].ParentSpanId);
    }

    [Fact]
    public void ParseOtelTraces_MissingScopeSpans_SkipsResource()
    {
        var body = Json("""
        {
          "resourceSpans": [{
            "resource": { "attributes": [] }
          }]
        }
        """);

        var traces = OtelEndpoints.ParseOtelTraces(body);
        Assert.NotNull(traces);
        Assert.Empty(traces);
    }

    [Fact]
    public void ParseOtelTraces_MissingSpansProperty_SkipsScope()
    {
        var body = Json("""
        {
          "resourceSpans": [{
            "resource": { "attributes": [] },
            "scopeSpans": [{ "noSpans": true }]
          }]
        }
        """);

        var traces = OtelEndpoints.ParseOtelTraces(body);
        Assert.NotNull(traces);
        Assert.Empty(traces);
    }

    [Fact]
    public void ParseOtelTraces_SessionIdFromAttributes()
    {
        var body = Json("""
        {
          "resourceSpans": [{
            "resource": { "attributes": [] },
            "scopeSpans": [{
              "spans": [{
                "name": "x",
                "startTimeUnixNano": "1700000000000000000",
                "endTimeUnixNano": "1700000000000000000",
                "attributes": [
                  { "key": "highlight.session_id", "value": { "stringValue": "my-sess" } }
                ]
              }]
            }]
          }]
        }
        """);

        var traces = OtelEndpoints.ParseOtelTraces(body);
        Assert.NotNull(traces);
        Assert.Equal("my-sess", traces[0].SecureSessionId);
    }

    [Fact]
    public void ParseOtelTraces_DurationCalculation_SubMicrosecond()
    {
        // 500 nanoseconds = 0 microseconds (truncated)
        var body = Json("""
        {
          "resourceSpans": [{
            "resource": { "attributes": [] },
            "scopeSpans": [{
              "spans": [{
                "name": "tiny",
                "startTimeUnixNano": "1700000000000000000",
                "endTimeUnixNano": "1700000000000000500"
              }]
            }]
          }]
        }
        """);

        var traces = OtelEndpoints.ParseOtelTraces(body);
        Assert.NotNull(traces);
        // 500 nanos rounds to 0 microseconds via TotalMicroseconds cast
        Assert.True(traces[0].Duration >= 0);
    }

    // ════════════════════════════════════════════════════════════════════
    //  ParseOtelMetrics
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void ParseOtelMetrics_SumWithAsDouble_Parsed()
    {
        var body = Json("""
        {
          "resourceMetrics": [{
            "resource": { "attributes": [] },
            "scopeMetrics": [{
              "metrics": [{
                "name": "http.request.count",
                "sum": {
                  "dataPoints": [{
                    "asDouble": 42.5,
                    "timeUnixNano": "1700000000000000000",
                    "attributes": [
                      { "key": "method", "value": { "stringValue": "GET" } }
                    ]
                  }]
                }
              }]
            }]
          }]
        }
        """);

        var metrics = OtelEndpoints.ParseOtelMetrics(body);
        Assert.NotNull(metrics);
        Assert.Single(metrics);
        Assert.Equal("http.request.count", metrics[0].Name);
        Assert.Equal(42.5, metrics[0].Value);
        Assert.NotNull(metrics[0].Tags);
        Assert.Contains(metrics[0].Tags!, t => t.Name == "method" && t.Value == "GET");
    }

    [Fact]
    public void ParseOtelMetrics_GaugeWithAsInt_Parsed()
    {
        var body = Json("""
        {
          "resourceMetrics": [{
            "resource": { "attributes": [] },
            "scopeMetrics": [{
              "metrics": [{
                "name": "system.memory.usage",
                "gauge": {
                  "dataPoints": [{
                    "asInt": 1024,
                    "timeUnixNano": "1700000000000000000"
                  }]
                }
              }]
            }]
          }]
        }
        """);

        var metrics = OtelEndpoints.ParseOtelMetrics(body);
        Assert.NotNull(metrics);
        Assert.Single(metrics);
        Assert.Equal("system.memory.usage", metrics[0].Name);
        Assert.Equal(1024.0, metrics[0].Value);
    }

    [Fact]
    public void ParseOtelMetrics_Histogram_Parsed()
    {
        var body = Json("""
        {
          "resourceMetrics": [{
            "resource": { "attributes": [] },
            "scopeMetrics": [{
              "metrics": [{
                "name": "http.duration",
                "histogram": {
                  "dataPoints": [{
                    "sum": 123.45,
                    "timeUnixNano": "1700000000000000000"
                  }]
                }
              }]
            }]
          }]
        }
        """);

        var metrics = OtelEndpoints.ParseOtelMetrics(body);
        Assert.NotNull(metrics);
        Assert.Single(metrics);
        Assert.Equal("http.duration", metrics[0].Name);
        Assert.Equal(123.45, metrics[0].Value);
    }

    [Fact]
    public void ParseOtelMetrics_MultipleDataPoints_AllParsed()
    {
        var body = Json("""
        {
          "resourceMetrics": [{
            "resource": { "attributes": [] },
            "scopeMetrics": [{
              "metrics": [{
                "name": "cpu",
                "gauge": {
                  "dataPoints": [
                    { "asDouble": 0.5, "timeUnixNano": "1700000000000000000" },
                    { "asDouble": 0.7, "timeUnixNano": "1700000001000000000" }
                  ]
                }
              }]
            }]
          }]
        }
        """);

        var metrics = OtelEndpoints.ParseOtelMetrics(body);
        Assert.NotNull(metrics);
        Assert.Equal(2, metrics.Count);
        Assert.Equal(0.5, metrics[0].Value);
        Assert.Equal(0.7, metrics[1].Value);
    }

    [Fact]
    public void ParseOtelMetrics_MissingName_Skipped()
    {
        var body = Json("""
        {
          "resourceMetrics": [{
            "resource": { "attributes": [] },
            "scopeMetrics": [{
              "metrics": [{
                "gauge": { "dataPoints": [{ "asDouble": 1.0, "timeUnixNano": "1700000000000000000" }] }
              }]
            }]
          }]
        }
        """);

        var metrics = OtelEndpoints.ParseOtelMetrics(body);
        Assert.NotNull(metrics);
        Assert.Empty(metrics);
    }

    [Fact]
    public void ParseOtelMetrics_EmptyName_Skipped()
    {
        var body = Json("""
        {
          "resourceMetrics": [{
            "resource": { "attributes": [] },
            "scopeMetrics": [{
              "metrics": [{
                "name": "",
                "gauge": { "dataPoints": [{ "asDouble": 1.0, "timeUnixNano": "1700000000000000000" }] }
              }]
            }]
          }]
        }
        """);

        var metrics = OtelEndpoints.ParseOtelMetrics(body);
        Assert.NotNull(metrics);
        Assert.Empty(metrics);
    }

    [Fact]
    public void ParseOtelMetrics_EmptyDataPoints_NoMetrics()
    {
        var body = Json("""
        {
          "resourceMetrics": [{
            "resource": { "attributes": [] },
            "scopeMetrics": [{
              "metrics": [{
                "name": "empty",
                "sum": { "dataPoints": [] }
              }]
            }]
          }]
        }
        """);

        var metrics = OtelEndpoints.ParseOtelMetrics(body);
        Assert.NotNull(metrics);
        Assert.Empty(metrics);
    }

    [Fact]
    public void ParseOtelMetrics_MissingResourceMetrics_ReturnsNull()
    {
        var body = Json("""{ "other": 1 }""");
        Assert.Null(OtelEndpoints.ParseOtelMetrics(body));
    }

    [Fact]
    public void ParseOtelMetrics_MalformedJson_ReturnsNull()
    {
        Assert.Null(OtelEndpoints.ParseOtelMetrics(Json("{bad")));
    }

    [Fact]
    public void ParseOtelMetrics_EmptyBody_ReturnsNull()
    {
        Assert.Null(OtelEndpoints.ParseOtelMetrics(Array.Empty<byte>()));
    }

    [Fact]
    public void ParseOtelMetrics_MissingScopeMetrics_SkipsResource()
    {
        var body = Json("""
        {
          "resourceMetrics": [{
            "resource": { "attributes": [] }
          }]
        }
        """);

        var metrics = OtelEndpoints.ParseOtelMetrics(body);
        Assert.NotNull(metrics);
        Assert.Empty(metrics);
    }

    [Fact]
    public void ParseOtelMetrics_TagsSplitCorrectly()
    {
        var body = Json("""
        {
          "resourceMetrics": [{
            "resource": { "attributes": [] },
            "scopeMetrics": [{
              "metrics": [{
                "name": "m",
                "sum": {
                  "dataPoints": [{
                    "asDouble": 1.0,
                    "timeUnixNano": "1700000000000000000",
                    "attributes": [
                      { "key": "env", "value": { "stringValue": "prod" } },
                      { "key": "region", "value": { "stringValue": "us-east-1" } }
                    ]
                  }]
                }
              }]
            }]
          }]
        }
        """);

        var metrics = OtelEndpoints.ParseOtelMetrics(body);
        Assert.NotNull(metrics);
        Assert.NotNull(metrics[0].Tags);
        Assert.Equal(2, metrics[0].Tags!.Count);
        Assert.Contains(metrics[0].Tags!, t => t.Name == "env" && t.Value == "prod");
        Assert.Contains(metrics[0].Tags!, t => t.Name == "region" && t.Value == "us-east-1");
    }

    [Fact]
    public void ParseOtelMetrics_SessionIdFromAttributes()
    {
        var body = Json("""
        {
          "resourceMetrics": [{
            "resource": { "attributes": [] },
            "scopeMetrics": [{
              "metrics": [{
                "name": "m",
                "sum": {
                  "dataPoints": [{
                    "asDouble": 1.0,
                    "timeUnixNano": "1700000000000000000",
                    "attributes": [
                      { "key": "highlight.session_id", "value": { "stringValue": "sess-99" } }
                    ]
                  }]
                }
              }]
            }]
          }]
        }
        """);

        var metrics = OtelEndpoints.ParseOtelMetrics(body);
        Assert.NotNull(metrics);
        Assert.Equal("sess-99", metrics[0].SessionSecureId);
    }

    [Fact]
    public void ParseOtelMetrics_NoSessionId_EmptyString()
    {
        var body = Json("""
        {
          "resourceMetrics": [{
            "resource": { "attributes": [] },
            "scopeMetrics": [{
              "metrics": [{
                "name": "m",
                "sum": { "dataPoints": [{ "asDouble": 1.0, "timeUnixNano": "1700000000000000000" }] }
              }]
            }]
          }]
        }
        """);

        var metrics = OtelEndpoints.ParseOtelMetrics(body);
        Assert.NotNull(metrics);
        Assert.Equal("", metrics[0].SessionSecureId);
    }

    // ════════════════════════════════════════════════════════════════════
    //  ParseTimestamp
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void ParseTimestamp_NanosecondsAsString_Parsed()
    {
        var element = ParseElement("""{ "timeUnixNano": "1700000000000000000" }""");
        var result = OtelEndpoints.ParseTimestamp(element, "timeUnixNano");
        var expected = new DateTime(2023, 11, 14, 22, 13, 20, DateTimeKind.Utc);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ParseTimestamp_NanosecondsAsNumber_Parsed()
    {
        var element = ParseElement("""{ "timeUnixNano": 1700000000000000000 }""");
        var result = OtelEndpoints.ParseTimestamp(element, "timeUnixNano");
        var expected = new DateTime(2023, 11, 14, 22, 13, 20, DateTimeKind.Utc);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ParseTimestamp_MissingField_DefaultsToUtcNow()
    {
        var element = ParseElement("""{ "other": 1 }""");
        var before = DateTime.UtcNow;
        var result = OtelEndpoints.ParseTimestamp(element, "timeUnixNano");
        var after = DateTime.UtcNow;
        Assert.InRange(result, before.AddSeconds(-1), after.AddSeconds(1));
    }

    [Fact]
    public void ParseTimestamp_FallbackToSecondField()
    {
        var element = ParseElement("""{ "observedTimeUnixNano": "1700000000000000000" }""");
        var result = OtelEndpoints.ParseTimestamp(element, "timeUnixNano", "observedTimeUnixNano");
        var expected = new DateTime(2023, 11, 14, 22, 13, 20, DateTimeKind.Utc);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ParseTimestamp_FirstFieldPreferredOverSecond()
    {
        // timeUnixNano = 2023-11-14, observedTimeUnixNano = different
        var element = ParseElement("""
        {
          "timeUnixNano": "1700000000000000000",
          "observedTimeUnixNano": "1600000000000000000"
        }
        """);
        var result = OtelEndpoints.ParseTimestamp(element, "timeUnixNano", "observedTimeUnixNano");
        var expected = new DateTime(2023, 11, 14, 22, 13, 20, DateTimeKind.Utc);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ParseTimestamp_ZeroNanos_ReturnsEpoch()
    {
        var element = ParseElement("""{ "timeUnixNano": "0" }""");
        var result = OtelEndpoints.ParseTimestamp(element, "timeUnixNano");
        Assert.Equal(DateTimeOffset.UnixEpoch.UtcDateTime, result);
    }

    [Fact]
    public void ParseTimestamp_InvalidStringValue_FallsThrough()
    {
        var element = ParseElement("""{ "timeUnixNano": "not-a-number" }""");
        var before = DateTime.UtcNow;
        var result = OtelEndpoints.ParseTimestamp(element, "timeUnixNano");
        var after = DateTime.UtcNow;
        Assert.InRange(result, before.AddSeconds(-1), after.AddSeconds(1));
    }

    // ════════════════════════════════════════════════════════════════════
    //  ParseSpanKind
    // ════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(1, "INTERNAL")]
    [InlineData(2, "SERVER")]
    [InlineData(3, "CLIENT")]
    [InlineData(4, "PRODUCER")]
    [InlineData(5, "CONSUMER")]
    public void ParseSpanKind_NumericValues_MappedCorrectly(int numeric, string expected)
    {
        var element = ParseElement($"{{ \"kind\": {numeric} }}");
        var kind = element.GetProperty("kind");
        Assert.Equal(expected, OtelEndpoints.ParseSpanKind(kind));
    }

    [Fact]
    public void ParseSpanKind_UnknownNumeric_DefaultsToInternal()
    {
        var element = ParseElement("""{ "kind": 99 }""");
        Assert.Equal("INTERNAL", OtelEndpoints.ParseSpanKind(element.GetProperty("kind")));
    }

    [Fact]
    public void ParseSpanKind_Zero_DefaultsToInternal()
    {
        var element = ParseElement("""{ "kind": 0 }""");
        Assert.Equal("INTERNAL", OtelEndpoints.ParseSpanKind(element.GetProperty("kind")));
    }

    [Theory]
    [InlineData("SPAN_KIND_SERVER", "SERVER")]
    [InlineData("SPAN_KIND_CLIENT", "CLIENT")]
    [InlineData("SPAN_KIND_PRODUCER", "PRODUCER")]
    [InlineData("SPAN_KIND_CONSUMER", "CONSUMER")]
    [InlineData("SPAN_KIND_INTERNAL", "INTERNAL")]
    public void ParseSpanKind_StringValues_PrefixStripped(string input, string expected)
    {
        var element = ParseElement($"{{ \"kind\": \"{input}\" }}");
        Assert.Equal(expected, OtelEndpoints.ParseSpanKind(element.GetProperty("kind")));
    }

    [Fact]
    public void ParseSpanKind_PlainStringWithoutPrefix_ReturnedAsIs()
    {
        var element = ParseElement("""{ "kind": "SERVER" }""");
        Assert.Equal("SERVER", OtelEndpoints.ParseSpanKind(element.GetProperty("kind")));
    }

    [Fact]
    public void ParseSpanKind_EmptyString_ReturnsEmpty()
    {
        var element = ParseElement("""{ "kind": "" }""");
        Assert.Equal("", OtelEndpoints.ParseSpanKind(element.GetProperty("kind")));
    }

    // ════════════════════════════════════════════════════════════════════
    //  ParseStatusCode
    // ════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(0, "UNSET")]
    [InlineData(1, "OK")]
    [InlineData(2, "ERROR")]
    public void ParseStatusCode_NumericValues_MappedCorrectly(int numeric, string expected)
    {
        var element = ParseElement($"{{ \"code\": {numeric} }}");
        Assert.Equal(expected, OtelEndpoints.ParseStatusCode(element.GetProperty("code")));
    }

    [Fact]
    public void ParseStatusCode_UnknownNumeric_DefaultsToUnset()
    {
        var element = ParseElement("""{ "code": 99 }""");
        Assert.Equal("UNSET", OtelEndpoints.ParseStatusCode(element.GetProperty("code")));
    }

    [Fact]
    public void ParseStatusCode_NegativeNumeric_DefaultsToUnset()
    {
        var element = ParseElement("""{ "code": -1 }""");
        Assert.Equal("UNSET", OtelEndpoints.ParseStatusCode(element.GetProperty("code")));
    }

    [Theory]
    [InlineData("OK", "OK")]
    [InlineData("ERROR", "ERROR")]
    [InlineData("UNSET", "UNSET")]
    [InlineData("STATUS_CODE_OK", "STATUS_CODE_OK")]
    public void ParseStatusCode_StringValues_ReturnedAsIs(string input, string expected)
    {
        var element = ParseElement($"{{ \"code\": \"{input}\" }}");
        Assert.Equal(expected, OtelEndpoints.ParseStatusCode(element.GetProperty("code")));
    }

    // ════════════════════════════════════════════════════════════════════
    //  ExtractAttributes
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void ExtractAttributes_StringValue_Extracted()
    {
        var element = ParseElement("""
        {
          "attributes": [
            { "key": "k1", "value": { "stringValue": "v1" } }
          ]
        }
        """);
        var attrs = OtelEndpoints.ExtractAttributes(element);
        Assert.Single(attrs);
        Assert.Equal("v1", attrs["k1"]);
    }

    [Fact]
    public void ExtractAttributes_IntValue_Extracted()
    {
        var element = ParseElement("""
        {
          "attributes": [
            { "key": "count", "value": { "intValue": 42 } }
          ]
        }
        """);
        var attrs = OtelEndpoints.ExtractAttributes(element);
        Assert.Equal("42", attrs["count"]);
    }

    [Fact]
    public void ExtractAttributes_DoubleValue_Extracted()
    {
        var element = ParseElement("""
        {
          "attributes": [
            { "key": "rate", "value": { "doubleValue": 3.14 } }
          ]
        }
        """);
        var attrs = OtelEndpoints.ExtractAttributes(element);
        Assert.Contains("rate", attrs.Keys);
        Assert.Contains("3.14", attrs["rate"]);
    }

    [Fact]
    public void ExtractAttributes_BoolValue_Extracted()
    {
        var element = ParseElement("""
        {
          "attributes": [
            { "key": "active", "value": { "boolValue": true } }
          ]
        }
        """);
        var attrs = OtelEndpoints.ExtractAttributes(element);
        Assert.Equal("True", attrs["active"]);
    }

    [Fact]
    public void ExtractAttributes_MissingAttributes_ReturnsEmptyDict()
    {
        var element = ParseElement("""{ "other": 1 }""");
        var attrs = OtelEndpoints.ExtractAttributes(element);
        Assert.Empty(attrs);
    }

    [Fact]
    public void ExtractAttributes_EmptyArray_ReturnsEmptyDict()
    {
        var element = ParseElement("""{ "attributes": [] }""");
        var attrs = OtelEndpoints.ExtractAttributes(element);
        Assert.Empty(attrs);
    }

    [Fact]
    public void ExtractAttributes_DuplicateKeys_LastWins()
    {
        var element = ParseElement("""
        {
          "attributes": [
            { "key": "k", "value": { "stringValue": "first" } },
            { "key": "k", "value": { "stringValue": "second" } }
          ]
        }
        """);
        var attrs = OtelEndpoints.ExtractAttributes(element);
        Assert.Single(attrs);
        Assert.Equal("second", attrs["k"]);
    }

    [Fact]
    public void ExtractAttributes_MissingKey_Skipped()
    {
        var element = ParseElement("""
        {
          "attributes": [
            { "value": { "stringValue": "orphan" } },
            { "key": "real", "value": { "stringValue": "yes" } }
          ]
        }
        """);
        var attrs = OtelEndpoints.ExtractAttributes(element);
        Assert.Single(attrs);
        Assert.Equal("yes", attrs["real"]);
    }

    [Fact]
    public void ExtractAttributes_MissingValueProperty_Skipped()
    {
        var element = ParseElement("""
        {
          "attributes": [
            { "key": "novalue" },
            { "key": "has", "value": { "stringValue": "yes" } }
          ]
        }
        """);
        var attrs = OtelEndpoints.ExtractAttributes(element);
        Assert.Single(attrs);
        Assert.Equal("yes", attrs["has"]);
    }

    [Fact]
    public void ExtractAttributes_MultipleTypes_AllExtracted()
    {
        var element = ParseElement("""
        {
          "attributes": [
            { "key": "s", "value": { "stringValue": "hello" } },
            { "key": "i", "value": { "intValue": 10 } },
            { "key": "d", "value": { "doubleValue": 1.5 } },
            { "key": "b", "value": { "boolValue": false } }
          ]
        }
        """);
        var attrs = OtelEndpoints.ExtractAttributes(element);
        Assert.Equal(4, attrs.Count);
        Assert.Equal("hello", attrs["s"]);
        Assert.Equal("10", attrs["i"]);
        Assert.Equal("False", attrs["b"]);
    }

    // ════════════════════════════════════════════════════════════════════
    //  ExtractAttributeValue
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void ExtractAttributeValue_NoValueProperty_ReturnsNull()
    {
        var element = ParseElement("""{ "key": "k" }""");
        Assert.Null(OtelEndpoints.ExtractAttributeValue(element));
    }

    [Fact]
    public void ExtractAttributeValue_UnknownValueType_ReturnsFallbackJson()
    {
        var element = ParseElement("""
        { "key": "k", "value": { "arrayValue": { "values": [] } } }
        """);
        var result = OtelEndpoints.ExtractAttributeValue(element);
        Assert.NotNull(result);
        Assert.Contains("arrayValue", result);
    }

    // ════════════════════════════════════════════════════════════════════
    //  ExtractResourceAttributes
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void ExtractResourceAttributes_AllKnownKeys_Extracted()
    {
        var element = ParseElement("""
        {
          "resource": {
            "attributes": [
              { "key": "service.name", "value": { "stringValue": "my-svc" } },
              { "key": "service.version", "value": { "stringValue": "2.0" } },
              { "key": "highlight.project_id", "value": { "stringValue": "10" } },
              { "key": "deployment.environment", "value": { "stringValue": "staging" } }
            ]
          }
        }
        """);

        var (sn, sv, pid, env) = OtelEndpoints.ExtractResourceAttributes(element);
        Assert.Equal("my-svc", sn);
        Assert.Equal("2.0", sv);
        Assert.Equal(10, pid);
        Assert.Equal("staging", env);
    }

    [Fact]
    public void ExtractResourceAttributes_AlternateProjectIdKey()
    {
        var element = ParseElement("""
        {
          "resource": {
            "attributes": [
              { "key": "highlight_project_id", "value": { "stringValue": "55" } }
            ]
          }
        }
        """);

        var (_, _, pid, _) = OtelEndpoints.ExtractResourceAttributes(element);
        Assert.Equal(55, pid);
    }

    [Fact]
    public void ExtractResourceAttributes_AlternateEnvironmentKey()
    {
        var element = ParseElement("""
        {
          "resource": {
            "attributes": [
              { "key": "highlight.environment", "value": { "stringValue": "dev" } }
            ]
          }
        }
        """);

        var (_, _, _, env) = OtelEndpoints.ExtractResourceAttributes(element);
        Assert.Equal("dev", env);
    }

    [Fact]
    public void ExtractResourceAttributes_MissingResource_ReturnsDefaults()
    {
        var element = ParseElement("""{ "other": 1 }""");
        var (sn, sv, pid, env) = OtelEndpoints.ExtractResourceAttributes(element);
        Assert.Null(sn);
        Assert.Null(sv);
        Assert.Equal(0, pid);
        Assert.Null(env);
    }

    [Fact]
    public void ExtractResourceAttributes_MissingAttributes_ReturnsDefaults()
    {
        var element = ParseElement("""{ "resource": {} }""");
        var (sn, sv, pid, env) = OtelEndpoints.ExtractResourceAttributes(element);
        Assert.Null(sn);
        Assert.Null(sv);
        Assert.Equal(0, pid);
        Assert.Null(env);
    }

    [Fact]
    public void ExtractResourceAttributes_EmptyAttributes_ReturnsDefaults()
    {
        var element = ParseElement("""{ "resource": { "attributes": [] } }""");
        var (sn, sv, pid, env) = OtelEndpoints.ExtractResourceAttributes(element);
        Assert.Null(sn);
        Assert.Null(sv);
        Assert.Equal(0, pid);
        Assert.Null(env);
    }

    [Fact]
    public void ExtractResourceAttributes_NonNumericProjectId_DefaultsToZero()
    {
        var element = ParseElement("""
        {
          "resource": {
            "attributes": [
              { "key": "highlight.project_id", "value": { "stringValue": "not-a-number" } }
            ]
          }
        }
        """);

        var (_, _, pid, _) = OtelEndpoints.ExtractResourceAttributes(element);
        Assert.Equal(0, pid);
    }

    [Fact]
    public void ExtractResourceAttributes_UnknownKeys_Ignored()
    {
        var element = ParseElement("""
        {
          "resource": {
            "attributes": [
              { "key": "telemetry.sdk.name", "value": { "stringValue": "opentelemetry" } },
              { "key": "service.name", "value": { "stringValue": "svc" } }
            ]
          }
        }
        """);

        var (sn, sv, pid, env) = OtelEndpoints.ExtractResourceAttributes(element);
        Assert.Equal("svc", sn);
        Assert.Null(sv);
        Assert.Equal(0, pid);
        Assert.Null(env);
    }

    // ════════════════════════════════════════════════════════════════════
    //  ExtractBody
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void ExtractBody_StringValue_Extracted()
    {
        var element = ParseElement("""{ "body": { "stringValue": "hello world" } }""");
        Assert.Equal("hello world", OtelEndpoints.ExtractBody(element));
    }

    [Fact]
    public void ExtractBody_MissingBody_ReturnsEmpty()
    {
        var element = ParseElement("""{ "other": 1 }""");
        Assert.Equal("", OtelEndpoints.ExtractBody(element));
    }

    [Fact]
    public void ExtractBody_NestedObject_ReturnsJson()
    {
        var element = ParseElement("""{ "body": { "mapValue": { "fields": {} } } }""");
        var body = OtelEndpoints.ExtractBody(element);
        Assert.Contains("mapValue", body);
    }

    [Fact]
    public void ExtractBody_NullStringValue_ReturnsEmpty()
    {
        // A stringValue that is JSON null
        var element = ParseElement("""{ "body": { "stringValue": null } }""");
        Assert.Equal("", OtelEndpoints.ExtractBody(element));
    }

    // ════════════════════════════════════════════════════════════════════
    //  ReadBodyAsync
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ReadBodyAsync_IdentityEncoding_ReadsRaw()
    {
        var expected = "hello world"u8.ToArray();
        var ctx = new DefaultHttpContext();
        ctx.Request.Body = new MemoryStream(expected);
        // No Content-Encoding header

        var result = await OtelEndpoints.ReadBodyAsync(ctx.Request);
        Assert.Equal(expected, result);
    }

    [Fact]
    public async Task ReadBodyAsync_GzipEncoding_Decompresses()
    {
        var original = "compressed payload"u8.ToArray();

        using var compressed = new MemoryStream();
        await using (var gzip = new GZipStream(compressed, CompressionMode.Compress, leaveOpen: true))
        {
            await gzip.WriteAsync(original);
        }
        compressed.Position = 0;

        var ctx = new DefaultHttpContext();
        ctx.Request.Body = compressed;
        ctx.Request.Headers.ContentEncoding = "gzip";

        var result = await OtelEndpoints.ReadBodyAsync(ctx.Request);
        Assert.Equal(original, result);
    }

    [Fact]
    public async Task ReadBodyAsync_GzipUpperCase_Decompresses()
    {
        var original = "test"u8.ToArray();

        using var compressed = new MemoryStream();
        await using (var gzip = new GZipStream(compressed, CompressionMode.Compress, leaveOpen: true))
        {
            await gzip.WriteAsync(original);
        }
        compressed.Position = 0;

        var ctx = new DefaultHttpContext();
        ctx.Request.Body = compressed;
        ctx.Request.Headers.ContentEncoding = "GZIP";

        var result = await OtelEndpoints.ReadBodyAsync(ctx.Request);
        Assert.Equal(original, result);
    }

    [Fact]
    public async Task ReadBodyAsync_EmptyBody_ReturnsEmptyArray()
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Body = new MemoryStream(Array.Empty<byte>());

        var result = await OtelEndpoints.ReadBodyAsync(ctx.Request);
        Assert.Empty(result);
    }

    [Fact]
    public async Task ReadBodyAsync_UnknownEncoding_ReadsRaw()
    {
        var expected = "raw data"u8.ToArray();
        var ctx = new DefaultHttpContext();
        ctx.Request.Body = new MemoryStream(expected);
        ctx.Request.Headers.ContentEncoding = "deflate"; // unsupported, falls to default

        var result = await OtelEndpoints.ReadBodyAsync(ctx.Request);
        Assert.Equal(expected, result);
    }

    [Fact]
    public async Task ReadBodyAsync_SnappyEncoding_ThrowsOnInvalidSnappy()
    {
        // Snappy is now a supported encoding, but non-snappy data will fail decoding
        var ctx = new DefaultHttpContext();
        ctx.Request.Body = new MemoryStream("not snappy data"u8.ToArray());
        ctx.Request.Headers.ContentEncoding = "snappy";

        await Assert.ThrowsAsync<IOException>(() => OtelEndpoints.ReadBodyAsync(ctx.Request));
    }

    [Fact]
    public async Task ReadBodyAsync_LargePayload_HandledCorrectly()
    {
        var large = new byte[1024 * 1024]; // 1 MB
        Random.Shared.NextBytes(large);

        var ctx = new DefaultHttpContext();
        ctx.Request.Body = new MemoryStream(large);

        var result = await OtelEndpoints.ReadBodyAsync(ctx.Request);
        Assert.Equal(large.Length, result.Length);
        Assert.Equal(large, result);
    }

    // ════════════════════════════════════════════════════════════════════
    //  ExtractMetricDataPoints
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void ExtractMetricDataPoints_SumField_Extracted()
    {
        var element = ParseElement("""
        {
          "sum": {
            "dataPoints": [
              { "asDouble": 10.5, "timeUnixNano": "1700000000000000000" }
            ]
          }
        }
        """);
        var points = OtelEndpoints.ExtractMetricDataPoints(element);
        Assert.Single(points);
        Assert.Equal(10.5, points[0].Value);
    }

    [Fact]
    public void ExtractMetricDataPoints_GaugeField_Extracted()
    {
        var element = ParseElement("""
        {
          "gauge": {
            "dataPoints": [
              { "asInt": 77, "timeUnixNano": "1700000000000000000" }
            ]
          }
        }
        """);
        var points = OtelEndpoints.ExtractMetricDataPoints(element);
        Assert.Single(points);
        Assert.Equal(77.0, points[0].Value);
    }

    [Fact]
    public void ExtractMetricDataPoints_HistogramSum_Extracted()
    {
        var element = ParseElement("""
        {
          "histogram": {
            "dataPoints": [
              { "sum": 99.9, "timeUnixNano": "1700000000000000000" }
            ]
          }
        }
        """);
        var points = OtelEndpoints.ExtractMetricDataPoints(element);
        Assert.Single(points);
        Assert.Equal(99.9, points[0].Value);
    }

    [Fact]
    public void ExtractMetricDataPoints_NoKnownField_ReturnsEmpty()
    {
        var element = ParseElement("""{ "exponentialHistogram": {} }""");
        var points = OtelEndpoints.ExtractMetricDataPoints(element);
        Assert.Empty(points);
    }

    [Fact]
    public void ExtractMetricDataPoints_NoDataPoints_ReturnsEmpty()
    {
        var element = ParseElement("""{ "sum": {} }""");
        var points = OtelEndpoints.ExtractMetricDataPoints(element);
        Assert.Empty(points);
    }

    [Fact]
    public void ExtractMetricDataPoints_EmptyDataPoints_ReturnsEmpty()
    {
        var element = ParseElement("""{ "sum": { "dataPoints": [] } }""");
        var points = OtelEndpoints.ExtractMetricDataPoints(element);
        Assert.Empty(points);
    }

    [Fact]
    public void ExtractMetricDataPoints_DataPointAttributes_Parsed()
    {
        var element = ParseElement("""
        {
          "gauge": {
            "dataPoints": [{
              "asDouble": 1.0,
              "timeUnixNano": "1700000000000000000",
              "attributes": [
                { "key": "host", "value": { "stringValue": "web01" } }
              ]
            }]
          }
        }
        """);
        var points = OtelEndpoints.ExtractMetricDataPoints(element);
        Assert.Single(points);
        Assert.NotNull(points[0].Tags);
        Assert.Contains("host:web01", points[0].Tags!);
    }

    [Fact]
    public void ExtractMetricDataPoints_DataPointWithNoValue_DefaultsToZero()
    {
        var element = ParseElement("""
        {
          "gauge": {
            "dataPoints": [{ "timeUnixNano": "1700000000000000000" }]
          }
        }
        """);
        var points = OtelEndpoints.ExtractMetricDataPoints(element);
        Assert.Single(points);
        Assert.Equal(0.0, points[0].Value);
    }

    [Fact]
    public void ExtractMetricDataPoints_SessionIdExtracted()
    {
        var element = ParseElement("""
        {
          "sum": {
            "dataPoints": [{
              "asDouble": 1.0,
              "timeUnixNano": "1700000000000000000",
              "attributes": [
                { "key": "highlight.session_id", "value": { "stringValue": "sid-1" } }
              ]
            }]
          }
        }
        """);
        var points = OtelEndpoints.ExtractMetricDataPoints(element);
        Assert.Equal("sid-1", points[0].SessionId);
    }

    // ════════════════════════════════════════════════════════════════════
    //  Edge cases: full pipeline round-trips
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void ParseOtelLogs_ArrayRoot_ReturnsNull()
    {
        // An array root is not a valid ExportLogsServiceRequest — should return null, not throw
        var body = Json("""[{"body": "test"}]""");
        Assert.Null(OtelEndpoints.ParseOtelLogs(body));
    }

    [Fact]
    public void ParseOtelTraces_ArrayRoot_ReturnsNull()
    {
        var body = Json("""[{"name": "span1"}]""");
        Assert.Null(OtelEndpoints.ParseOtelTraces(body));
    }

    [Fact]
    public void ParseOtelMetrics_ArrayRoot_ReturnsNull()
    {
        var body = Json("""[{"name": "m1", "value": 1}]""");
        Assert.Null(OtelEndpoints.ParseOtelMetrics(body));
    }

    [Fact]
    public void ParseOtelLogs_EmptyObject_ReturnsNull()
    {
        var body = Json("{}");
        Assert.Null(OtelEndpoints.ParseOtelLogs(body));
    }

    [Fact]
    public void ParseOtelTraces_EmptyObject_ReturnsNull()
    {
        var body = Json("{}");
        Assert.Null(OtelEndpoints.ParseOtelTraces(body));
    }

    [Fact]
    public void ParseOtelMetrics_EmptyObject_ReturnsNull()
    {
        var body = Json("{}");
        Assert.Null(OtelEndpoints.ParseOtelMetrics(body));
    }

    [Fact]
    public void ParseOtelTraces_MultipleSpansInOneScope_AllParsed()
    {
        var body = Json("""
        {
          "resourceSpans": [{
            "resource": { "attributes": [] },
            "scopeSpans": [{
              "spans": [
                { "name": "s1", "startTimeUnixNano": "1700000000000000000", "endTimeUnixNano": "1700000000000000000" },
                { "name": "s2", "startTimeUnixNano": "1700000000000000000", "endTimeUnixNano": "1700000000000000000" },
                { "name": "s3", "startTimeUnixNano": "1700000000000000000", "endTimeUnixNano": "1700000000000000000" }
              ]
            }]
          }]
        }
        """);

        var traces = OtelEndpoints.ParseOtelTraces(body);
        Assert.NotNull(traces);
        Assert.Equal(3, traces.Count);
    }

    [Fact]
    public void ParseOtelTraces_SpanKindAsString_InTrace()
    {
        var body = Json("""
        {
          "resourceSpans": [{
            "resource": { "attributes": [] },
            "scopeSpans": [{
              "spans": [{
                "name": "rpc",
                "kind": "SPAN_KIND_CLIENT",
                "startTimeUnixNano": "1700000000000000000",
                "endTimeUnixNano": "1700000000000000000"
              }]
            }]
          }]
        }
        """);

        var traces = OtelEndpoints.ParseOtelTraces(body);
        Assert.NotNull(traces);
        Assert.Equal("CLIENT", traces[0].SpanKind);
    }

    [Fact]
    public void ParseOtelTraces_MissingKind_DefaultsToInternal()
    {
        var body = Json("""
        {
          "resourceSpans": [{
            "resource": { "attributes": [] },
            "scopeSpans": [{
              "spans": [{
                "name": "nk",
                "startTimeUnixNano": "1700000000000000000",
                "endTimeUnixNano": "1700000000000000000"
              }]
            }]
          }]
        }
        """);

        var traces = OtelEndpoints.ParseOtelTraces(body);
        Assert.NotNull(traces);
        Assert.Equal("INTERNAL", traces[0].SpanKind);
    }

    [Fact]
    public void ParseOtelTraces_StatusCodeAsString_ReturnsString()
    {
        var body = Json("""
        {
          "resourceSpans": [{
            "resource": { "attributes": [] },
            "scopeSpans": [{
              "spans": [{
                "name": "x",
                "startTimeUnixNano": "1700000000000000000",
                "endTimeUnixNano": "1700000000000000000",
                "status": { "code": "ERROR" }
              }]
            }]
          }]
        }
        """);

        var traces = OtelEndpoints.ParseOtelTraces(body);
        Assert.NotNull(traces);
        Assert.Equal("ERROR", traces[0].StatusCode);
        Assert.True(traces[0].HasErrors);
    }

    [Fact]
    public void ParseOtelMetrics_MultipleMetricsInOneScope()
    {
        var body = Json("""
        {
          "resourceMetrics": [{
            "resource": { "attributes": [] },
            "scopeMetrics": [{
              "metrics": [
                { "name": "m1", "gauge": { "dataPoints": [{ "asDouble": 1, "timeUnixNano": "1700000000000000000" }] } },
                { "name": "m2", "gauge": { "dataPoints": [{ "asDouble": 2, "timeUnixNano": "1700000000000000000" }] } }
              ]
            }]
          }]
        }
        """);

        var metrics = OtelEndpoints.ParseOtelMetrics(body);
        Assert.NotNull(metrics);
        Assert.Equal(2, metrics.Count);
        Assert.Equal("m1", metrics[0].Name);
        Assert.Equal("m2", metrics[1].Name);
    }

    [Fact]
    public void ParseOtelLogs_TimestampFromObservedTimeUnixNano()
    {
        var body = Json("""
        {
          "resourceLogs": [{
            "resource": { "attributes": [] },
            "scopeLogs": [{
              "logRecords": [{
                "observedTimeUnixNano": "1700000000000000000",
                "body": { "stringValue": "test" }
              }]
            }]
          }]
        }
        """);

        var logs = OtelEndpoints.ParseOtelLogs(body);
        Assert.NotNull(logs);
        var expected = new DateTime(2023, 11, 14, 22, 13, 20, DateTimeKind.Utc);
        Assert.Equal(expected, logs[0].Timestamp);
    }

    [Fact]
    public void ParseOtelLogs_LogAttributes_Preserved()
    {
        var body = Json("""
        {
          "resourceLogs": [{
            "resource": { "attributes": [] },
            "scopeLogs": [{
              "logRecords": [{
                "body": { "stringValue": "test" },
                "attributes": [
                  { "key": "custom.key1", "value": { "stringValue": "val1" } },
                  { "key": "custom.key2", "value": { "intValue": 100 } }
                ]
              }]
            }]
          }]
        }
        """);

        var logs = OtelEndpoints.ParseOtelLogs(body);
        Assert.NotNull(logs);
        Assert.NotNull(logs[0].LogAttributes);
        Assert.Equal("val1", logs[0].LogAttributes!["custom.key1"]);
        Assert.Equal("100", logs[0].LogAttributes!["custom.key2"]);
    }

    [Fact]
    public void ParseOtelTraces_TraceAttributes_Preserved()
    {
        var body = Json("""
        {
          "resourceSpans": [{
            "resource": { "attributes": [] },
            "scopeSpans": [{
              "spans": [{
                "name": "x",
                "startTimeUnixNano": "1700000000000000000",
                "endTimeUnixNano": "1700000000000000000",
                "attributes": [
                  { "key": "http.url", "value": { "stringValue": "https://example.com" } },
                  { "key": "http.status_code", "value": { "intValue": 200 } }
                ]
              }]
            }]
          }]
        }
        """);

        var traces = OtelEndpoints.ParseOtelTraces(body);
        Assert.NotNull(traces);
        Assert.NotNull(traces[0].TraceAttributes);
        Assert.Equal("https://example.com", traces[0].TraceAttributes!["http.url"]);
        Assert.Equal("200", traces[0].TraceAttributes!["http.status_code"]);
    }
}
