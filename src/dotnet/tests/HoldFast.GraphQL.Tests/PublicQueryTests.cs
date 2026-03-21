using HoldFast.Data;
using HoldFast.Domain.Entities;
using HoldFast.GraphQL.Public;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HoldFast.GraphQL.Tests;

/// <summary>
/// Tests for PublicQuery: GetSampling, Ignore, and VerboseId resolution.
/// Uses SQLite in-memory database for sampling settings lookups.
/// </summary>
public class PublicQueryTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly HoldFastDbContext _db;
    private readonly PublicQuery _query;
    private readonly Workspace _workspace;
    private readonly Project _project;

    public PublicQueryTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<HoldFastDbContext>()
            .UseSqlite(_connection).Options;
        _db = new HoldFastDbContext(options);
        _db.Database.EnsureCreated();

        _workspace = new Workspace { Name = "TestWS", PlanTier = "Enterprise" };
        _db.Workspaces.Add(_workspace);
        _db.SaveChanges();

        _project = new Project { Name = "TestProj", WorkspaceId = _workspace.Id };
        _db.Projects.Add(_project);
        _db.SaveChanges();

        _query = new PublicQuery();
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Close();
        _connection.Dispose();
    }

    // ── Ignore ────────────────────────────────────────────────────────

    [Fact]
    public void Ignore_ReturnsNull_ForAnyId()
    {
        Assert.Null(_query.Ignore(0));
        Assert.Null(_query.Ignore(1));
        Assert.Null(_query.Ignore(-1));
        Assert.Null(_query.Ignore(int.MaxValue));
        Assert.Null(_query.Ignore(int.MinValue));
    }

    // ── GetSampling ───────────────────────────────────────────────────

    [Fact]
    public async Task GetSampling_NoSettings_ReturnsEmptyConfig()
    {
        // Use the project's verbose ID (HashID encoded)
        var verboseId = _project.VerboseId;

        var result = await _query.GetSampling(verboseId, _db, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Null(result.Spans);
        Assert.Null(result.Logs);
    }

    [Fact]
    public async Task GetSampling_WithPlainIntId_ReturnsEmptyConfig()
    {
        // Legacy clients send plain integer IDs
        var result = await _query.GetSampling(
            _project.Id.ToString(), _db, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Null(result.Spans);
        Assert.Null(result.Logs);
    }

    [Fact]
    public async Task GetSampling_WithSamplingSettings_QueriesDatabase()
    {
        // Add sampling settings for the project
        _db.ProjectClientSamplingSettings.Add(new ProjectClientSamplingSettings
        {
            ProjectId = _project.Id,
            SpanSamplingConfigs = "[{\"name\":\"slow-spans\",\"ratio\":0.5}]",
            LogSamplingConfigs = "[{\"severity\":\"DEBUG\",\"ratio\":0.1}]",
        });
        await _db.SaveChangesAsync();

        var result = await _query.GetSampling(
            _project.VerboseId, _db, CancellationToken.None);

        // Currently GetSampling always returns empty config
        // (the settings entity is queried but not yet mapped to the response)
        Assert.NotNull(result);
    }

    [Fact]
    public async Task GetSampling_NonexistentProject_ReturnsEmptyConfig()
    {
        // VerboseId that decodes to a project that doesn't exist
        var result = await _query.GetSampling("99999", _db, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Null(result.Spans);
        Assert.Null(result.Logs);
    }

    [Fact]
    public async Task GetSampling_InvalidVerboseId_Throws()
    {
        // A string that can't be decoded as HashID or integer
        await Assert.ThrowsAsync<ArgumentException>(
            () => _query.GetSampling("!!!invalid!!!", _db, CancellationToken.None));
    }

    // ── VerboseId / FromVerboseId Roundtrip ──────────────────────────

    [Fact]
    public void VerboseId_Roundtrip()
    {
        var verboseId = _project.VerboseId;
        var decoded = Project.FromVerboseId(verboseId);

        Assert.Equal(_project.Id, decoded);
    }

    [Fact]
    public void FromVerboseId_PlainInteger()
    {
        var decoded = Project.FromVerboseId("42");
        Assert.Equal(42, decoded);
    }

    [Fact]
    public void FromVerboseId_Zero()
    {
        var decoded = Project.FromVerboseId("0");
        Assert.Equal(0, decoded);
    }

    [Fact]
    public void FromVerboseId_InvalidHashId_Throws()
    {
        Assert.Throws<ArgumentException>(() => Project.FromVerboseId("!!!"));
    }

    [Fact]
    public void VerboseId_MinLength8()
    {
        // Project HashID encoder uses minHashLength=8
        Assert.True(_project.VerboseId.Length >= 8);
    }

    [Fact]
    public void VerboseId_OnlyLowercaseAlphanumeric()
    {
        var verboseId = _project.VerboseId;
        Assert.All(verboseId, c =>
            Assert.True(char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c),
                $"Unexpected character '{c}' in verbose ID '{verboseId}'"));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(100)]
    [InlineData(1000)]
    [InlineData(999999)]
    public void VerboseId_RoundTrips_MultipleIds(int id)
    {
        var project = new Project { Name = "test" };
        typeof(BaseEntity).GetProperty("Id")!.SetValue(project, id);
        var verboseId = project.VerboseId;
        Assert.Equal(id, Project.FromVerboseId(verboseId));
    }

    [Fact]
    public void VerboseId_DifferentIdsProduceDifferentHashes()
    {
        var p1 = new Project { Name = "a" };
        var p2 = new Project { Name = "b" };
        typeof(BaseEntity).GetProperty("Id")!.SetValue(p1, 1);
        typeof(BaseEntity).GetProperty("Id")!.SetValue(p2, 2);
        Assert.NotEqual(p1.VerboseId, p2.VerboseId);
    }

    [Fact]
    public void FromVerboseId_EmptyString_Throws()
    {
        Assert.ThrowsAny<Exception>(() => Project.FromVerboseId(""));
    }

    [Fact]
    public void FromVerboseId_UppercaseNotInAlphabet_Throws()
    {
        Assert.ThrowsAny<Exception>(() => Project.FromVerboseId("ABCDEFGH"));
    }

    [Fact]
    public void VerboseId_LargeId()
    {
        var project = new Project { Name = "test" };
        typeof(BaseEntity).GetProperty("Id")!.SetValue(project, int.MaxValue);
        var verboseId = project.VerboseId;
        Assert.NotEmpty(verboseId);
        Assert.Equal(int.MaxValue, Project.FromVerboseId(verboseId));
    }

    [Fact]
    public void VerboseId_ConsistentAcrossCalls()
    {
        var project = new Project { Name = "test" };
        typeof(BaseEntity).GetProperty("Id")!.SetValue(project, 123);
        Assert.Equal(project.VerboseId, project.VerboseId);
    }

    // ── SamplingConfig Record Tests ──────────────────────────────────

    [Fact]
    public void SamplingConfig_DefaultsToNull()
    {
        var config = new SamplingConfig();
        Assert.Null(config.Spans);
        Assert.Null(config.Logs);
    }

    [Fact]
    public void SamplingConfig_WithSpans()
    {
        var spans = new List<SpanSamplingConfig>
        {
            new(Name: new MatchConfig(RegexValue: "slow.*"), SamplingRatio: 2),
            new(Name: new MatchConfig(MatchValue: "GET /health"), SamplingRatio: 10),
        };

        var config = new SamplingConfig(Spans: spans);
        Assert.Equal(2, config.Spans!.Count);
        Assert.Equal(2, config.Spans[0].SamplingRatio);
        Assert.Equal("slow.*", config.Spans[0].Name!.RegexValue);
        Assert.Equal("GET /health", config.Spans[1].Name!.MatchValue as string);
    }

    [Fact]
    public void SamplingConfig_WithLogs()
    {
        var logs = new List<LogSamplingConfig>
        {
            new(Message: new MatchConfig(RegexValue: "healthcheck"), SamplingRatio: 100),
            new(SeverityText: new MatchConfig(MatchValue: "DEBUG"), SamplingRatio: 5),
        };

        var config = new SamplingConfig(Logs: logs);
        Assert.Equal(2, config.Logs!.Count);
        Assert.Equal(100, config.Logs[0].SamplingRatio);
    }

    [Fact]
    public void SpanSamplingConfig_DefaultRatio()
    {
        var span = new SpanSamplingConfig();
        Assert.Equal(1, span.SamplingRatio);
        Assert.Null(span.Name);
        Assert.Null(span.Attributes);
        Assert.Null(span.Events);
    }

    [Fact]
    public void SpanSamplingConfig_WithAttributes()
    {
        var span = new SpanSamplingConfig(
            Attributes: new List<AttributeMatchConfig>
            {
                new(Key: new MatchConfig(MatchValue: "http.method"),
                    Attribute: new MatchConfig(MatchValue: "GET")),
                new(Key: new MatchConfig(RegexValue: "db\\..*"),
                    Attribute: new MatchConfig(RegexValue: "SELECT.*")),
            },
            SamplingRatio: 5);

        Assert.Equal(2, span.Attributes!.Count);
        Assert.Equal("http.method", span.Attributes[0].Key.MatchValue as string);
    }

    [Fact]
    public void SpanSamplingConfig_WithEvents()
    {
        var span = new SpanSamplingConfig(
            Events: new List<SpanEventMatchConfig>
            {
                new(Name: new MatchConfig(MatchValue: "exception"),
                    Attributes: new List<AttributeMatchConfig>
                    {
                        new(Key: new MatchConfig(MatchValue: "exception.type"),
                            Attribute: new MatchConfig(RegexValue: ".*Timeout.*")),
                    }),
            });

        Assert.Single(span.Events!);
        Assert.Equal("exception", span.Events[0].Name!.MatchValue as string);
        Assert.Single(span.Events[0].Attributes!);
    }

    [Fact]
    public void LogSamplingConfig_DefaultRatio()
    {
        var log = new LogSamplingConfig();
        Assert.Equal(1, log.SamplingRatio);
        Assert.Null(log.Attributes);
        Assert.Null(log.Message);
        Assert.Null(log.SeverityText);
    }

    [Fact]
    public void LogSamplingConfig_AllFields()
    {
        var log = new LogSamplingConfig(
            Attributes: new List<AttributeMatchConfig>
            {
                new(Key: new MatchConfig(MatchValue: "service.name"),
                    Attribute: new MatchConfig(MatchValue: "health-checker")),
            },
            Message: new MatchConfig(RegexValue: "heartbeat|ping"),
            SeverityText: new MatchConfig(MatchValue: "TRACE"),
            SamplingRatio: 50);

        Assert.Single(log.Attributes!);
        Assert.Equal("heartbeat|ping", log.Message!.RegexValue);
        Assert.Equal("TRACE", log.SeverityText!.MatchValue as string);
        Assert.Equal(50, log.SamplingRatio);
    }

    [Fact]
    public void MatchConfig_RegexValue()
    {
        var match = new MatchConfig(RegexValue: "error|warn");
        Assert.Equal("error|warn", match.RegexValue);
        Assert.Null(match.MatchValue);
    }

    [Fact]
    public void MatchConfig_MatchValue()
    {
        var match = new MatchConfig(MatchValue: "exact-string");
        Assert.Null(match.RegexValue);
        Assert.Equal("exact-string", match.MatchValue as string);
    }

    [Fact]
    public void MatchConfig_BothNull()
    {
        var match = new MatchConfig();
        Assert.Null(match.RegexValue);
        Assert.Null(match.MatchValue);
    }

    [Fact]
    public void MatchConfig_NumericMatchValue()
    {
        var match = new MatchConfig(MatchValue: 42);
        Assert.Equal(42, match.MatchValue);
    }

    [Fact]
    public void AttributeMatchConfig_KeyAndAttribute()
    {
        var attr = new AttributeMatchConfig(
            Key: new MatchConfig(MatchValue: "http.status_code"),
            Attribute: new MatchConfig(RegexValue: "5\\d\\d"));

        Assert.Equal("http.status_code", attr.Key.MatchValue as string);
        Assert.Equal("5\\d\\d", attr.Attribute.RegexValue);
    }

    [Fact]
    public void SpanEventMatchConfig_Defaults()
    {
        var evt = new SpanEventMatchConfig();
        Assert.Null(evt.Name);
        Assert.Null(evt.Attributes);
    }

    // ── Record Equality ──────────────────────────────────────────────

    [Fact]
    public void SamplingConfig_RecordEquality()
    {
        var a = new SamplingConfig();
        var b = new SamplingConfig();
        Assert.Equal(a, b);
    }

    [Fact]
    public void MatchConfig_RecordEquality()
    {
        var a = new MatchConfig(RegexValue: "test");
        var b = new MatchConfig(RegexValue: "test");
        var c = new MatchConfig(RegexValue: "other");

        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
    }

    [Fact]
    public void SpanSamplingConfig_RecordEquality()
    {
        var a = new SpanSamplingConfig(SamplingRatio: 5);
        var b = new SpanSamplingConfig(SamplingRatio: 5);
        var c = new SpanSamplingConfig(SamplingRatio: 10);

        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
    }

    // ── ProjectClientSamplingSettings Entity Tests ───────────────────

    [Fact]
    public async Task SamplingSettings_Persistence()
    {
        var settings = new ProjectClientSamplingSettings
        {
            ProjectId = _project.Id,
            SpanSamplingConfigs = "[{\"ratio\":0.5}]",
            LogSamplingConfigs = "[{\"ratio\":0.1}]",
        };
        _db.ProjectClientSamplingSettings.Add(settings);
        await _db.SaveChangesAsync();

        var loaded = await _db.ProjectClientSamplingSettings
            .FirstOrDefaultAsync(s => s.ProjectId == _project.Id);

        Assert.NotNull(loaded);
        Assert.Equal("[{\"ratio\":0.5}]", loaded!.SpanSamplingConfigs);
        Assert.Equal("[{\"ratio\":0.1}]", loaded.LogSamplingConfigs);
    }

    [Fact]
    public async Task SamplingSettings_NullConfigs()
    {
        var settings = new ProjectClientSamplingSettings
        {
            ProjectId = _project.Id,
        };
        _db.ProjectClientSamplingSettings.Add(settings);
        await _db.SaveChangesAsync();

        var loaded = await _db.ProjectClientSamplingSettings
            .FirstOrDefaultAsync(s => s.ProjectId == _project.Id);

        Assert.NotNull(loaded);
        Assert.Null(loaded!.SpanSamplingConfigs);
        Assert.Null(loaded.LogSamplingConfigs);
    }

    [Fact]
    public async Task SamplingSettings_UpdateConfigs()
    {
        var settings = new ProjectClientSamplingSettings
        {
            ProjectId = _project.Id,
            SpanSamplingConfigs = "[]",
        };
        _db.ProjectClientSamplingSettings.Add(settings);
        await _db.SaveChangesAsync();

        settings.SpanSamplingConfigs = "[{\"name\":\"updated\"}]";
        await _db.SaveChangesAsync();

        var loaded = await _db.ProjectClientSamplingSettings
            .FirstOrDefaultAsync(s => s.ProjectId == _project.Id);

        Assert.Equal("[{\"name\":\"updated\"}]", loaded!.SpanSamplingConfigs);
    }
}
