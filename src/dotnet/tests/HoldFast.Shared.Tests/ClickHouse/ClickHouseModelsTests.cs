using HoldFast.Data.ClickHouse;
using HoldFast.Data.ClickHouse.Models;
using HoldFast.Domain.Enums;

namespace HoldFast.Shared.Tests.ClickHouse;

public class ClickHouseModelsTests
{
    // ── LogRow ───────────────────────────────────────────────────────

    [Fact]
    public void LogRow_DefaultValues()
    {
        var row = new LogRow();
        Assert.Equal(string.Empty, row.TraceId);
        Assert.Equal(string.Empty, row.SpanId);
        Assert.Equal(string.Empty, row.SecureSessionId);
        Assert.Equal(string.Empty, row.UUID);
        Assert.Equal(string.Empty, row.SeverityText);
        Assert.Equal(string.Empty, row.ServiceName);
        Assert.Equal(string.Empty, row.ServiceVersion);
        Assert.Equal(string.Empty, row.Body);
        Assert.Equal(string.Empty, row.Environment);
        Assert.NotNull(row.LogAttributes);
        Assert.Empty(row.LogAttributes);
    }

    [Fact]
    public void LogRow_Cursor_IsBase64Encoded()
    {
        var row = new LogRow
        {
            Timestamp = new DateTime(2026, 3, 20, 12, 0, 0, DateTimeKind.Utc),
            UUID = "abc123",
        };
        // Should be valid base64
        var bytes = Convert.FromBase64String(row.Cursor);
        Assert.NotEmpty(bytes);
        // Should decode to timestamp,uuid
        var (ts, uuid) = CursorHelper.Decode(row.Cursor);
        Assert.Equal("abc123", uuid);
        Assert.Equal(new DateTime(2026, 3, 20, 12, 0, 0, DateTimeKind.Utc), ts);
    }

    [Fact]
    public void LogRow_Cursor_RoundTrips()
    {
        var row = new LogRow
        {
            Timestamp = new DateTime(2026, 1, 15, 10, 30, 0, DateTimeKind.Utc),
            UUID = "xyz",
        };
        var (ts, uuid) = CursorHelper.Decode(row.Cursor);
        Assert.Equal(row.Timestamp, ts);
        Assert.Equal(row.UUID, uuid);
    }

    [Fact]
    public void LogRow_AllPropertiesSettable()
    {
        var attrs = new Dictionary<string, string> { ["key"] = "value" };
        var row = new LogRow
        {
            Timestamp = DateTime.UtcNow,
            ProjectId = 42,
            TraceId = "trace-1",
            SpanId = "span-1",
            SecureSessionId = "session-abc",
            UUID = "uuid-1",
            TraceFlags = 1,
            SeverityText = "ERROR",
            SeverityNumber = 17,
            Source = LogSource.Backend,
            ServiceName = "api",
            ServiceVersion = "1.0.0",
            Body = "Something went wrong",
            LogAttributes = attrs,
            Environment = "production",
        };

        Assert.Equal(42, row.ProjectId);
        Assert.Equal("trace-1", row.TraceId);
        Assert.Equal(LogSource.Backend, row.Source);
        Assert.Equal("api", row.ServiceName);
        Assert.Single(row.LogAttributes);
    }

    [Fact]
    public void LogRow_SeverityNumber_MatchesOpenTelemetry()
    {
        // OTEL severity numbers: TRACE=1-4, DEBUG=5-8, INFO=9-12, WARN=13-16, ERROR=17-20, FATAL=21-24
        Assert.Equal(1, (int)LogLevel.Trace);
        Assert.Equal(17, (int)LogLevel.Error);
    }

    // ── TraceRow ─────────────────────────────────────────────────────

    [Fact]
    public void TraceRow_DefaultValues()
    {
        var row = new TraceRow();
        Assert.Equal(string.Empty, row.TraceId);
        Assert.Equal(string.Empty, row.SpanId);
        Assert.Equal(string.Empty, row.ParentSpanId);
        Assert.Equal(string.Empty, row.SpanName);
        Assert.Equal(string.Empty, row.StatusCode);
        Assert.Equal(string.Empty, row.StatusMessage);
        Assert.Equal(0L, row.Duration);
        Assert.False(row.HasErrors);
        Assert.Empty(row.Events);
        Assert.Empty(row.Links);
        Assert.Empty(row.TraceAttributes);
    }

    [Fact]
    public void TraceRow_Cursor_IsBase64Encoded()
    {
        var row = new TraceRow
        {
            Timestamp = new DateTime(2026, 6, 1, 8, 0, 0, DateTimeKind.Utc),
            UUID = "trace-uuid",
        };
        var (ts, uuid) = CursorHelper.Decode(row.Cursor);
        Assert.Equal("trace-uuid", uuid);
        Assert.Equal(new DateTime(2026, 6, 1, 8, 0, 0, DateTimeKind.Utc), ts);
    }

    [Fact]
    public void TraceRow_Duration_InNanoseconds()
    {
        var row = new TraceRow { Duration = 1_500_000_000 }; // 1.5 seconds
        Assert.Equal(1_500_000_000L, row.Duration);
    }

    [Fact]
    public void TraceRow_WithEvents()
    {
        var row = new TraceRow
        {
            Events =
            [
                new TraceEvent
                {
                    Timestamp = DateTime.UtcNow,
                    Name = "exception",
                    Attributes = new() { ["exception.message"] = "NullRef" },
                },
            ],
        };
        Assert.Single(row.Events);
        Assert.Equal("exception", row.Events[0].Name);
    }

    [Fact]
    public void TraceRow_WithLinks()
    {
        var row = new TraceRow
        {
            Links =
            [
                new TraceLink
                {
                    TraceId = "linked-trace",
                    SpanId = "linked-span",
                },
            ],
        };
        Assert.Single(row.Links);
        Assert.Equal("linked-trace", row.Links[0].TraceId);
    }

    [Fact]
    public void TraceRow_SpanKinds()
    {
        foreach (var kind in Enum.GetValues<SpanKind>())
        {
            var row = new TraceRow { SpanKind = kind };
            Assert.Equal(kind, row.SpanKind);
        }
    }

    // ── TraceEvent ──────────────────────────────────────────────────

    [Fact]
    public void TraceEvent_Defaults()
    {
        var evt = new TraceEvent();
        Assert.Equal(string.Empty, evt.Name);
        Assert.Empty(evt.Attributes);
    }

    [Fact]
    public void TraceEvent_MultipleAttributes()
    {
        var evt = new TraceEvent
        {
            Attributes = new()
            {
                ["exception.type"] = "System.NullReferenceException",
                ["exception.message"] = "Object reference not set",
                ["exception.stacktrace"] = "at Foo.Bar()",
            },
        };
        Assert.Equal(3, evt.Attributes.Count);
    }

    // ── TraceLink ───────────────────────────────────────────────────

    [Fact]
    public void TraceLink_Defaults()
    {
        var link = new TraceLink();
        Assert.Equal(string.Empty, link.TraceId);
        Assert.Equal(string.Empty, link.SpanId);
        Assert.Equal(string.Empty, link.TraceState);
        Assert.Empty(link.Attributes);
    }

    // ── MetricsBucket ───────────────────────────────────────────────

    [Fact]
    public void MetricsBucket_AllProperties()
    {
        var bucket = new MetricsBucket
        {
            BucketStart = DateTime.UtcNow.AddMinutes(-5),
            BucketEnd = DateTime.UtcNow,
            Group = "api-service",
            Value = 42.5,
            Count = 100,
        };
        Assert.Equal("api-service", bucket.Group);
        Assert.Equal(42.5, bucket.Value);
        Assert.Equal(100, bucket.Count);
    }

    [Fact]
    public void MetricsBuckets_DefaultValues()
    {
        var result = new MetricsBuckets();
        Assert.Empty(result.Buckets);
        Assert.Equal(0, result.TotalCount);
        Assert.Null(result.SampleFactor);
    }

    [Fact]
    public void MetricsBuckets_WithData()
    {
        var result = new MetricsBuckets
        {
            Buckets = [new MetricsBucket { Value = 1 }, new MetricsBucket { Value = 2 }],
            TotalCount = 1000,
            SampleFactor = 0.1,
        };
        Assert.Equal(2, result.Buckets.Count);
        Assert.Equal(1000, result.TotalCount);
        Assert.Equal(0.1, result.SampleFactor);
    }

    // ── HistogramBucket ─────────────────────────────────────────────

    [Fact]
    public void HistogramBucket_Properties()
    {
        var bucket = new HistogramBucket
        {
            BucketStart = DateTime.UtcNow.AddHours(-1),
            BucketEnd = DateTime.UtcNow,
            Count = 42,
            Group = "frontend",
        };
        Assert.Equal(42, bucket.Count);
        Assert.Equal("frontend", bucket.Group);
    }

    // ── QueryInput ──────────────────────────────────────────────────

    [Fact]
    public void QueryInput_Defaults()
    {
        var input = new QueryInput();
        Assert.Null(input.Query); // Query is nullable; defaults to null when no search filter provided
        Assert.Equal(default, input.DateRange.StartDate);
        Assert.Equal(default, input.DateRange.EndDate);
    }

    [Fact]
    public void QueryInput_WithDateRange()
    {
        var start = DateTime.UtcNow.AddDays(-7);
        var end = DateTime.UtcNow;
        var input = new QueryInput
        {
            Query = "service_name:api AND level:error",
            DateRange = new DateRangeRequiredInput { StartDate = start, EndDate = end },
        };
        Assert.True(input.DateRange.EndDate > input.DateRange.StartDate);
    }

    // ── ClickHousePagination ────────────────────────────────────────

    [Fact]
    public void Pagination_Defaults()
    {
        var p = new ClickHousePagination();
        Assert.Null(p.After);
        Assert.Null(p.Before);
        Assert.Null(p.At);
        Assert.Equal("DESC", p.Direction);
        Assert.Equal(50, p.Limit);
    }

    [Fact]
    public void Pagination_CustomValues()
    {
        var p = new ClickHousePagination
        {
            After = "cursor-abc",
            Direction = "ASC",
            Limit = 100,
        };
        Assert.Equal("cursor-abc", p.After);
        Assert.Equal("ASC", p.Direction);
        Assert.Equal(100, p.Limit);
    }

    // ── LogConnection ───────────────────────────────────────────────

    [Fact]
    public void LogConnection_Defaults()
    {
        var conn = new LogConnection();
        Assert.Empty(conn.Edges);
        Assert.NotNull(conn.PageInfo);
    }

    [Fact]
    public void LogConnection_WithEdges()
    {
        var conn = new LogConnection
        {
            Edges =
            [
                new LogEdge
                {
                    Node = new LogRow { Body = "log line 1" },
                    Cursor = "c1",
                },
                new LogEdge
                {
                    Node = new LogRow { Body = "log line 2" },
                    Cursor = "c2",
                },
            ],
            PageInfo = new PageInfo
            {
                HasNextPage = true,
                HasPreviousPage = false,
                StartCursor = "c1",
                EndCursor = "c2",
            },
        };
        Assert.Equal(2, conn.Edges.Count);
        Assert.True(conn.PageInfo.HasNextPage);
        Assert.Equal("c1", conn.PageInfo.StartCursor);
    }

    // ── TraceConnection ─────────────────────────────────────────────

    [Fact]
    public void TraceConnection_Defaults()
    {
        var conn = new TraceConnection();
        Assert.Empty(conn.Edges);
        Assert.NotNull(conn.PageInfo);
    }

    // ── PageInfo ────────────────────────────────────────────────────

    [Fact]
    public void PageInfo_Defaults()
    {
        var pi = new PageInfo();
        Assert.False(pi.HasNextPage);
        Assert.False(pi.HasPreviousPage);
        Assert.Null(pi.StartCursor);
        Assert.Null(pi.EndCursor);
    }
}
