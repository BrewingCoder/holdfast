using System.Security.Claims;
using System.Text;
using HoldFast.Data;
using HoldFast.Data.ClickHouse;
using HoldFast.Data.ClickHouse.Models;
using HoldFast.Domain.Entities;
using HoldFast.Domain.Enums;
using HoldFast.GraphQL.Private;
using HoldFast.Shared.Auth;
using HoldFast.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HoldFast.GraphQL.Tests;

/// <summary>
/// Comprehensive tests for PrivateQuery analytics, notification channel suggestion,
/// SSO, workspace access request, and integration mapping methods.
/// Uses SQLite in-memory database, hand-rolled fakes.
/// </summary>
public class AnalyticsQueryTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly HoldFastDbContext _db;
    private readonly PrivateQuery _query;
    private readonly Workspace _workspace;
    private readonly Project _project;
    private readonly ClaimsPrincipal _principal;
    private readonly FakeAuthorizationService _authz;

    public AnalyticsQueryTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<HoldFastDbContext>()
            .UseSqlite(_connection).Options;
        _db = new HoldFastDbContext(options);
        _db.Database.EnsureCreated();

        _workspace = new Workspace { Name = "AnalyticsWS", PlanTier = "Enterprise" };
        _db.Workspaces.Add(_workspace);
        _db.SaveChanges();

        _project = new Project { Name = "AnalyticsProj", WorkspaceId = _workspace.Id, Secret = "analytics-key" };
        _db.Projects.Add(_project);
        _db.SaveChanges();

        _query = new PrivateQuery();

        var claims = new[] { new Claim(HoldFastClaimTypes.Uid, "test-uid-analytics") };
        var identity = new ClaimsIdentity(claims, "Test");
        _principal = new ClaimsPrincipal(identity);

        _authz = new FakeAuthorizationService();
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Close();
        _connection.Dispose();
    }

    // ══════════════════════════════════════════════════════════════════
    // GetDailyErrorFrequency
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetDailyErrorFrequency_NoErrors_ReturnsAllZeros()
    {
        var eg = new ErrorGroup { ProjectId = _project.Id, SecureId = "eg-freq-empty" };
        _db.ErrorGroups.Add(eg);
        _db.SaveChanges();

        var result = await _query.GetDailyErrorFrequency(
            _project.Id, "eg-freq-empty", 3, _principal, _authz, _db, CancellationToken.None);

        Assert.Equal(4, result.Count); // dateOffset=3 => days 0..3 = 4 entries
        Assert.All(result, c => Assert.Equal(0, c));
    }

    [Fact]
    public async Task GetDailyErrorFrequency_ErrorGroupNotFound_Throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _query.GetDailyErrorFrequency(
                _project.Id, "nonexistent-eg", 7, _principal, _authz, _db, CancellationToken.None));
    }

    [Fact]
    public async Task GetDailyErrorFrequency_CountsErrorsPerDay()
    {
        var eg = new ErrorGroup { ProjectId = _project.Id, SecureId = "eg-freq-count" };
        _db.ErrorGroups.Add(eg);
        _db.SaveChanges();

        var today = DateTime.UtcNow.Date;
        _db.ErrorObjects.AddRange(
            new ErrorObject { ProjectId = _project.Id, ErrorGroupId = eg.Id, CreatedAt = today, Event = "e1" },
            new ErrorObject { ProjectId = _project.Id, ErrorGroupId = eg.Id, CreatedAt = today, Event = "e2" },
            new ErrorObject { ProjectId = _project.Id, ErrorGroupId = eg.Id, CreatedAt = today.AddDays(-1), Event = "e3" });
        _db.SaveChanges();

        var result = await _query.GetDailyErrorFrequency(
            _project.Id, "eg-freq-count", 2, _principal, _authz, _db, CancellationToken.None);

        // dateOffset=2 => 3 entries: [day-2, day-1, day-0]
        Assert.Equal(3, result.Count);
        Assert.Equal(0, result[0]); // day-2: no errors
        Assert.Equal(1, result[1]); // day-1: 1 error
        Assert.Equal(2, result[2]); // today: 2 errors
    }

    [Fact]
    public async Task GetDailyErrorFrequency_IgnoresErrorsBeforeWindow()
    {
        var eg = new ErrorGroup { ProjectId = _project.Id, SecureId = "eg-freq-window" };
        _db.ErrorGroups.Add(eg);
        _db.SaveChanges();

        var today = DateTime.UtcNow.Date;
        _db.ErrorObjects.Add(new ErrorObject
        {
            ProjectId = _project.Id, ErrorGroupId = eg.Id,
            CreatedAt = today.AddDays(-10), Event = "old"
        });
        _db.SaveChanges();

        var result = await _query.GetDailyErrorFrequency(
            _project.Id, "eg-freq-window", 3, _principal, _authz, _db, CancellationToken.None);

        Assert.All(result, c => Assert.Equal(0, c));
    }

    [Fact]
    public async Task GetDailyErrorFrequency_ZeroOffset_ReturnsSingleDay()
    {
        var eg = new ErrorGroup { ProjectId = _project.Id, SecureId = "eg-freq-zero" };
        _db.ErrorGroups.Add(eg);
        _db.SaveChanges();

        var result = await _query.GetDailyErrorFrequency(
            _project.Id, "eg-freq-zero", 0, _principal, _authz, _db, CancellationToken.None);

        Assert.Single(result);
    }

    // ══════════════════════════════════════════════════════════════════
    // GetErrorGroupTags
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetErrorGroupTags_NoErrors_ReturnsEmpty()
    {
        var eg = new ErrorGroup { ProjectId = _project.Id, SecureId = "eg-tags-empty" };
        _db.ErrorGroups.Add(eg);
        _db.SaveChanges();

        var result = await _query.GetErrorGroupTags(
            "eg-tags-empty", _principal, _authz, _db, CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetErrorGroupTags_ErrorGroupNotFound_Throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _query.GetErrorGroupTags("nonexistent-eg", _principal, _authz, _db, CancellationToken.None));
    }

    [Fact]
    public async Task GetErrorGroupTags_AggregatesBrowserOSEnvironment()
    {
        var eg = new ErrorGroup { ProjectId = _project.Id, SecureId = "eg-tags-agg" };
        _db.ErrorGroups.Add(eg);
        _db.SaveChanges();

        _db.ErrorObjects.AddRange(
            new ErrorObject { ProjectId = _project.Id, ErrorGroupId = eg.Id, Browser = "Chrome", OS = "Windows", Environment = "production", Event = "e1" },
            new ErrorObject { ProjectId = _project.Id, ErrorGroupId = eg.Id, Browser = "Chrome", OS = "macOS", Environment = "production", Event = "e2" },
            new ErrorObject { ProjectId = _project.Id, ErrorGroupId = eg.Id, Browser = "Firefox", OS = "Windows", Environment = "staging", Event = "e3" });
        _db.SaveChanges();

        var result = await _query.GetErrorGroupTags(
            "eg-tags-agg", _principal, _authz, _db, CancellationToken.None);

        Assert.Equal(3, result.Count); // browser, os, environment

        var browserAgg = result.First(r => r.Key == "browser");
        Assert.Equal(2, browserAgg.Buckets.Count); // Chrome, Firefox
        Assert.Equal("Chrome", browserAgg.Buckets[0].Key); // ordered by count desc
        Assert.Equal(2, browserAgg.Buckets[0].DocCount);

        var osAgg = result.First(r => r.Key == "os");
        Assert.Equal(2, osAgg.Buckets.Count);

        var envAgg = result.First(r => r.Key == "environment");
        Assert.Equal(2, envAgg.Buckets.Count);
    }

    [Fact]
    public async Task GetErrorGroupTags_NullFieldsExcluded()
    {
        var eg = new ErrorGroup { ProjectId = _project.Id, SecureId = "eg-tags-null" };
        _db.ErrorGroups.Add(eg);
        _db.SaveChanges();

        _db.ErrorObjects.Add(new ErrorObject
        {
            ProjectId = _project.Id, ErrorGroupId = eg.Id,
            Browser = null, OS = null, Environment = null, Event = "e1"
        });
        _db.SaveChanges();

        var result = await _query.GetErrorGroupTags(
            "eg-tags-null", _principal, _authz, _db, CancellationToken.None);

        Assert.Empty(result); // no non-null fields => no aggregations
    }

    [Fact]
    public async Task GetErrorGroupTags_EmptyStringFieldsExcluded()
    {
        var eg = new ErrorGroup { ProjectId = _project.Id, SecureId = "eg-tags-emptystr" };
        _db.ErrorGroups.Add(eg);
        _db.SaveChanges();

        _db.ErrorObjects.Add(new ErrorObject
        {
            ProjectId = _project.Id, ErrorGroupId = eg.Id,
            Browser = "", OS = "", Environment = "", Event = "e1"
        });
        _db.SaveChanges();

        var result = await _query.GetErrorGroupTags(
            "eg-tags-emptystr", _principal, _authz, _db, CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetErrorGroupTags_PercentageCalculation()
    {
        var eg = new ErrorGroup { ProjectId = _project.Id, SecureId = "eg-tags-pct" };
        _db.ErrorGroups.Add(eg);
        _db.SaveChanges();

        _db.ErrorObjects.AddRange(
            new ErrorObject { ProjectId = _project.Id, ErrorGroupId = eg.Id, Browser = "Chrome", Event = "e1" },
            new ErrorObject { ProjectId = _project.Id, ErrorGroupId = eg.Id, Browser = "Chrome", Event = "e2" },
            new ErrorObject { ProjectId = _project.Id, ErrorGroupId = eg.Id, Browser = "Firefox", Event = "e3" },
            new ErrorObject { ProjectId = _project.Id, ErrorGroupId = eg.Id, Browser = "Firefox", Event = "e4" });
        _db.SaveChanges();

        var result = await _query.GetErrorGroupTags(
            "eg-tags-pct", _principal, _authz, _db, CancellationToken.None);

        var browserAgg = result.First(r => r.Key == "browser");
        // 2 out of 4 total = 50%
        Assert.Equal(50.0, browserAgg.Buckets[0].Percent, 0.01);
    }

    // ══════════════════════════════════════════════════════════════════
    // GetReferrers
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetReferrers_NoFields_ReturnsEmpty()
    {
        var result = await _query.GetReferrers(
            _project.Id, 7, _principal, _authz, _db, CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetReferrers_AggregatesAndOrders()
    {
        _db.Fields.AddRange(
            new Field { ProjectId = _project.Id, Name = "referrer", Value = "google.com", CreatedAt = DateTime.UtcNow },
            new Field { ProjectId = _project.Id, Name = "referrer", Value = "google.com", CreatedAt = DateTime.UtcNow },
            new Field { ProjectId = _project.Id, Name = "referrer", Value = "bing.com", CreatedAt = DateTime.UtcNow });
        _db.SaveChanges();

        var result = await _query.GetReferrers(
            _project.Id, 7, _principal, _authz, _db, CancellationToken.None);

        Assert.Equal(2, result.Count);
        Assert.Equal("google.com", result[0].Host);
        Assert.Equal(2, result[0].Count);
        Assert.Equal("bing.com", result[1].Host);
        Assert.Equal(1, result[1].Count);
    }

    [Fact]
    public async Task GetReferrers_IgnoresNonReferrerFields()
    {
        _db.Fields.Add(new Field
        {
            ProjectId = _project.Id, Name = "user_agent", Value = "Mozilla/5.0",
            CreatedAt = DateTime.UtcNow
        });
        _db.SaveChanges();

        var result = await _query.GetReferrers(
            _project.Id, 7, _principal, _authz, _db, CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetReferrers_IgnoresFieldsOutsideLookback()
    {
        _db.Fields.Add(new Field
        {
            ProjectId = _project.Id, Name = "referrer", Value = "old.com",
            CreatedAt = DateTime.UtcNow.AddDays(-30)
        });
        _db.SaveChanges();

        var result = await _query.GetReferrers(
            _project.Id, 7, _principal, _authz, _db, CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetReferrers_LimitedTo50()
    {
        for (int i = 0; i < 55; i++)
        {
            _db.Fields.Add(new Field
            {
                ProjectId = _project.Id, Name = "referrer", Value = $"host{i}.com",
                CreatedAt = DateTime.UtcNow
            });
        }
        _db.SaveChanges();

        var result = await _query.GetReferrers(
            _project.Id, 7, _principal, _authz, _db, CancellationToken.None);

        Assert.Equal(50, result.Count);
    }

    [Fact]
    public async Task GetReferrers_PercentageCalculation()
    {
        _db.Fields.AddRange(
            new Field { ProjectId = _project.Id, Name = "referrer", Value = "a.com", CreatedAt = DateTime.UtcNow },
            new Field { ProjectId = _project.Id, Name = "referrer", Value = "a.com", CreatedAt = DateTime.UtcNow },
            new Field { ProjectId = _project.Id, Name = "referrer", Value = "a.com", CreatedAt = DateTime.UtcNow },
            new Field { ProjectId = _project.Id, Name = "referrer", Value = "b.com", CreatedAt = DateTime.UtcNow });
        _db.SaveChanges();

        var result = await _query.GetReferrers(
            _project.Id, 7, _principal, _authz, _db, CancellationToken.None);

        Assert.Equal(75.0, result[0].Percent, 0.01);
        Assert.Equal(25.0, result[1].Percent, 0.01);
    }

    // ══════════════════════════════════════════════════════════════════
    // GetTopUsers
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetTopUsers_NoSessions_ReturnsEmpty()
    {
        var result = await _query.GetTopUsers(
            _project.Id, 7, _principal, _authz, _db, CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetTopUsers_ExcludesExcludedSessions()
    {
        _db.Sessions.Add(new Session
        {
            SecureId = "sess-excluded", ProjectId = _project.Id, Identifier = "user1",
            ActiveLength = 100, Excluded = true, CreatedAt = DateTime.UtcNow
        });
        _db.SaveChanges();

        var result = await _query.GetTopUsers(
            _project.Id, 7, _principal, _authz, _db, CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetTopUsers_ExcludesSessionsWithNoIdentifier()
    {
        _db.Sessions.Add(new Session
        {
            SecureId = "sess-noid", ProjectId = _project.Id, Identifier = null,
            ActiveLength = 100, CreatedAt = DateTime.UtcNow
        });
        _db.Sessions.Add(new Session
        {
            SecureId = "sess-emptyid", ProjectId = _project.Id, Identifier = "",
            ActiveLength = 100, CreatedAt = DateTime.UtcNow
        });
        _db.SaveChanges();

        var result = await _query.GetTopUsers(
            _project.Id, 7, _principal, _authz, _db, CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetTopUsers_GroupsByIdentifierAndOrdersByActiveTime()
    {
        _db.Sessions.AddRange(
            new Session { SecureId = "sess-tu1", ProjectId = _project.Id, Identifier = "alice", ActiveLength = 200, CreatedAt = DateTime.UtcNow },
            new Session { SecureId = "sess-tu2", ProjectId = _project.Id, Identifier = "alice", ActiveLength = 300, CreatedAt = DateTime.UtcNow },
            new Session { SecureId = "sess-tu3", ProjectId = _project.Id, Identifier = "bob", ActiveLength = 100, CreatedAt = DateTime.UtcNow });
        _db.SaveChanges();

        var result = await _query.GetTopUsers(
            _project.Id, 7, _principal, _authz, _db, CancellationToken.None);

        Assert.Equal(2, result.Count);
        Assert.Equal("alice", result[0].Identifier);
        Assert.Equal(500, result[0].TotalActiveTime);
        Assert.Equal("bob", result[1].Identifier);
        Assert.Equal(100, result[1].TotalActiveTime);
    }

    [Fact]
    public async Task GetTopUsers_ActiveTimePercentageCalculation()
    {
        _db.Sessions.AddRange(
            new Session { SecureId = "sess-pct1", ProjectId = _project.Id, Identifier = "user1", ActiveLength = 300, CreatedAt = DateTime.UtcNow },
            new Session { SecureId = "sess-pct2", ProjectId = _project.Id, Identifier = "user2", ActiveLength = 100, CreatedAt = DateTime.UtcNow });
        _db.SaveChanges();

        var result = await _query.GetTopUsers(
            _project.Id, 7, _principal, _authz, _db, CancellationToken.None);

        Assert.Equal(75.0, result[0].ActiveTimePercentage, 0.01);
        Assert.Equal(25.0, result[1].ActiveTimePercentage, 0.01);
    }

    [Fact]
    public async Task GetTopUsers_NullActiveLengthTreatedAsZero()
    {
        _db.Sessions.Add(new Session
        {
            SecureId = "sess-nullal", ProjectId = _project.Id, Identifier = "user-null",
            ActiveLength = null, CreatedAt = DateTime.UtcNow
        });
        _db.SaveChanges();

        var result = await _query.GetTopUsers(
            _project.Id, 7, _principal, _authz, _db, CancellationToken.None);

        Assert.Single(result);
        Assert.Equal(0, result[0].TotalActiveTime);
    }

    // ══════════════════════════════════════════════════════════════════
    // GetAverageSessionLength
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetAverageSessionLength_NoSessions_ReturnsZero()
    {
        var result = await _query.GetAverageSessionLength(
            _project.Id, 7, _principal, _authz, _db, CancellationToken.None);

        Assert.Equal(0, result.Length);
    }

    [Fact]
    public async Task GetAverageSessionLength_ExcludesExcludedSessions()
    {
        _db.Sessions.Add(new Session
        {
            SecureId = "sess-asl-ex", ProjectId = _project.Id,
            Length = 500, Excluded = true, CreatedAt = DateTime.UtcNow
        });
        _db.SaveChanges();

        var result = await _query.GetAverageSessionLength(
            _project.Id, 7, _principal, _authz, _db, CancellationToken.None);

        Assert.Equal(0, result.Length);
    }

    [Fact]
    public async Task GetAverageSessionLength_ExcludesNullAndZeroLength()
    {
        _db.Sessions.AddRange(
            new Session { SecureId = "sess-asl-null", ProjectId = _project.Id, Length = null, CreatedAt = DateTime.UtcNow },
            new Session { SecureId = "sess-asl-zero", ProjectId = _project.Id, Length = 0, CreatedAt = DateTime.UtcNow });
        _db.SaveChanges();

        var result = await _query.GetAverageSessionLength(
            _project.Id, 7, _principal, _authz, _db, CancellationToken.None);

        Assert.Equal(0, result.Length);
    }

    [Fact]
    public async Task GetAverageSessionLength_CalculatesCorrectAverage()
    {
        _db.Sessions.AddRange(
            new Session { SecureId = "sess-asl1", ProjectId = _project.Id, Length = 100, CreatedAt = DateTime.UtcNow },
            new Session { SecureId = "sess-asl2", ProjectId = _project.Id, Length = 200, CreatedAt = DateTime.UtcNow },
            new Session { SecureId = "sess-asl3", ProjectId = _project.Id, Length = 300, CreatedAt = DateTime.UtcNow });
        _db.SaveChanges();

        var result = await _query.GetAverageSessionLength(
            _project.Id, 7, _principal, _authz, _db, CancellationToken.None);

        Assert.Equal(200.0, result.Length, 0.01);
    }

    [Fact]
    public async Task GetAverageSessionLength_IgnoresSessionsOutsideLookback()
    {
        _db.Sessions.Add(new Session
        {
            SecureId = "sess-asl-old", ProjectId = _project.Id,
            Length = 9999, CreatedAt = DateTime.UtcNow.AddDays(-30)
        });
        _db.SaveChanges();

        var result = await _query.GetAverageSessionLength(
            _project.Id, 7, _principal, _authz, _db, CancellationToken.None);

        Assert.Equal(0, result.Length);
    }

    // ══════════════════════════════════════════════════════════════════
    // GetNewUsersCount
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetNewUsersCount_NoSessions_ReturnsZero()
    {
        var result = await _query.GetNewUsersCount(
            _project.Id, 7, _principal, _authz, _db, CancellationToken.None);

        Assert.Equal(0, result.Count);
    }

    [Fact]
    public async Task GetNewUsersCount_OnlyCountsFirstTimeEqualsOne()
    {
        _db.Sessions.AddRange(
            new Session { SecureId = "sess-nu1", ProjectId = _project.Id, FirstTime = 1, CreatedAt = DateTime.UtcNow },
            new Session { SecureId = "sess-nu2", ProjectId = _project.Id, FirstTime = 1, CreatedAt = DateTime.UtcNow },
            new Session { SecureId = "sess-nu3", ProjectId = _project.Id, FirstTime = 0, CreatedAt = DateTime.UtcNow },
            new Session { SecureId = "sess-nu4", ProjectId = _project.Id, FirstTime = null, CreatedAt = DateTime.UtcNow });
        _db.SaveChanges();

        var result = await _query.GetNewUsersCount(
            _project.Id, 7, _principal, _authz, _db, CancellationToken.None);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task GetNewUsersCount_ExcludesExcludedSessions()
    {
        _db.Sessions.Add(new Session
        {
            SecureId = "sess-nu-ex", ProjectId = _project.Id,
            FirstTime = 1, Excluded = true, CreatedAt = DateTime.UtcNow
        });
        _db.SaveChanges();

        var result = await _query.GetNewUsersCount(
            _project.Id, 7, _principal, _authz, _db, CancellationToken.None);

        Assert.Equal(0, result.Count);
    }

    [Fact]
    public async Task GetNewUsersCount_IgnoresSessionsOutsideLookback()
    {
        _db.Sessions.Add(new Session
        {
            SecureId = "sess-nu-old", ProjectId = _project.Id,
            FirstTime = 1, CreatedAt = DateTime.UtcNow.AddDays(-30)
        });
        _db.SaveChanges();

        var result = await _query.GetNewUsersCount(
            _project.Id, 7, _principal, _authz, _db, CancellationToken.None);

        Assert.Equal(0, result.Count);
    }

    // ══════════════════════════════════════════════════════════════════
    // GetUserFingerprintCount
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetUserFingerprintCount_NoSessions_ReturnsZero()
    {
        var result = await _query.GetUserFingerprintCount(
            _project.Id, 7, _principal, _authz, _db, CancellationToken.None);

        Assert.Equal(0, result.Count);
    }

    [Fact]
    public async Task GetUserFingerprintCount_CountsDistinctFingerprints()
    {
        _db.Sessions.AddRange(
            new Session { SecureId = "sess-fp1", ProjectId = _project.Id, Fingerprint = "fp-abc", CreatedAt = DateTime.UtcNow },
            new Session { SecureId = "sess-fp2", ProjectId = _project.Id, Fingerprint = "fp-abc", CreatedAt = DateTime.UtcNow },
            new Session { SecureId = "sess-fp3", ProjectId = _project.Id, Fingerprint = "fp-def", CreatedAt = DateTime.UtcNow });
        _db.SaveChanges();

        var result = await _query.GetUserFingerprintCount(
            _project.Id, 7, _principal, _authz, _db, CancellationToken.None);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task GetUserFingerprintCount_ExcludesNullFingerprints()
    {
        _db.Sessions.Add(new Session
        {
            SecureId = "sess-fp-null", ProjectId = _project.Id,
            Fingerprint = null, CreatedAt = DateTime.UtcNow
        });
        _db.SaveChanges();

        var result = await _query.GetUserFingerprintCount(
            _project.Id, 7, _principal, _authz, _db, CancellationToken.None);

        Assert.Equal(0, result.Count);
    }

    [Fact]
    public async Task GetUserFingerprintCount_ExcludesExcludedSessions()
    {
        _db.Sessions.Add(new Session
        {
            SecureId = "sess-fp-ex", ProjectId = _project.Id,
            Fingerprint = "fp-excluded", Excluded = true, CreatedAt = DateTime.UtcNow
        });
        _db.SaveChanges();

        var result = await _query.GetUserFingerprintCount(
            _project.Id, 7, _principal, _authz, _db, CancellationToken.None);

        Assert.Equal(0, result.Count);
    }

    [Fact]
    public async Task GetUserFingerprintCount_IgnoresSessionsOutsideLookback()
    {
        _db.Sessions.Add(new Session
        {
            SecureId = "sess-fp-old", ProjectId = _project.Id,
            Fingerprint = "fp-old", CreatedAt = DateTime.UtcNow.AddDays(-30)
        });
        _db.SaveChanges();

        var result = await _query.GetUserFingerprintCount(
            _project.Id, 7, _principal, _authz, _db, CancellationToken.None);

        Assert.Equal(0, result.Count);
    }

    // ══════════════════════════════════════════════════════════════════
    // GetSlackChannelSuggestion
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetSlackChannelSuggestion_NullSlackChannels_ReturnsEmpty()
    {
        var result = await _query.GetSlackChannelSuggestion(
            _project.Id, _principal, _authz, _db, CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetSlackChannelSuggestion_EmptyString_ReturnsEmpty()
    {
        _workspace.SlackChannels = "";
        _db.SaveChanges();

        var result = await _query.GetSlackChannelSuggestion(
            _project.Id, _principal, _authz, _db, CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetSlackChannelSuggestion_EmptyJsonArray_ReturnsEmpty()
    {
        _workspace.SlackChannels = "[]";
        _db.SaveChanges();

        var result = await _query.GetSlackChannelSuggestion(
            _project.Id, _principal, _authz, _db, CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetSlackChannelSuggestion_WithChannels_ReturnsSanitized()
    {
        _workspace.SlackChannels = "[\"#general\",\"#alerts\",\"#engineering\"]";
        _db.SaveChanges();

        var result = await _query.GetSlackChannelSuggestion(
            _project.Id, _principal, _authz, _db, CancellationToken.None);

        Assert.Equal(3, result.Count);
        Assert.Equal("#general", result[0].WebhookChannel);
        Assert.Null(result[0].WebhookChannelId);
    }

    [Fact]
    public async Task GetSlackChannelSuggestion_ProjectNotFound_Throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _query.GetSlackChannelSuggestion(999999, _principal, _authz, _db, CancellationToken.None));
    }

    // ══════════════════════════════════════════════════════════════════
    // GetDiscordChannelSuggestions
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetDiscordChannelSuggestions_NoGuildId_ReturnsEmpty()
    {
        var result = await _query.GetDiscordChannelSuggestions(
            _project.Id, _principal, _authz, _db, CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetDiscordChannelSuggestions_EmptyGuildId_ReturnsEmpty()
    {
        _workspace.DiscordGuildId = "";
        _db.SaveChanges();

        var result = await _query.GetDiscordChannelSuggestions(
            _project.Id, _principal, _authz, _db, CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetDiscordChannelSuggestions_WithGuildId_ReturnsSingleEntry()
    {
        _workspace.DiscordGuildId = "guild-12345";
        _db.SaveChanges();

        var result = await _query.GetDiscordChannelSuggestions(
            _project.Id, _principal, _authz, _db, CancellationToken.None);

        Assert.Single(result);
        Assert.Equal("guild-12345", result[0].Id);
        Assert.Equal("default", result[0].Name);
    }

    [Fact]
    public async Task GetDiscordChannelSuggestions_ProjectNotFound_Throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _query.GetDiscordChannelSuggestions(999999, _principal, _authz, _db, CancellationToken.None));
    }

    // ══════════════════════════════════════════════════════════════════
    // GetMicrosoftTeamsChannelSuggestions
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetMicrosoftTeamsChannelSuggestions_NoTenantId_ReturnsEmpty()
    {
        var result = await _query.GetMicrosoftTeamsChannelSuggestions(
            _project.Id, _principal, _authz, _db, CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetMicrosoftTeamsChannelSuggestions_EmptyTenantId_ReturnsEmpty()
    {
        _workspace.MicrosoftTeamsTenantId = "";
        _db.SaveChanges();

        var result = await _query.GetMicrosoftTeamsChannelSuggestions(
            _project.Id, _principal, _authz, _db, CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetMicrosoftTeamsChannelSuggestions_WithTenantId_ReturnsSingleEntry()
    {
        _workspace.MicrosoftTeamsTenantId = "tenant-abc";
        _db.SaveChanges();

        var result = await _query.GetMicrosoftTeamsChannelSuggestions(
            _project.Id, _principal, _authz, _db, CancellationToken.None);

        Assert.Single(result);
        Assert.Equal("tenant-abc", result[0].Id);
        Assert.Equal("default", result[0].Name);
    }

    [Fact]
    public async Task GetMicrosoftTeamsChannelSuggestions_ProjectNotFound_Throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _query.GetMicrosoftTeamsChannelSuggestions(999999, _principal, _authz, _db, CancellationToken.None));
    }

    // ══════════════════════════════════════════════════════════════════
    // GetSSOLogin
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetSSOLogin_DomainNotFound_ReturnsNull()
    {
        var result = await _query.GetSSOLogin("nonexistent.com", _db, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetSSOLogin_DomainFound_ReturnsLogin()
    {
        _db.SSOClients.Add(new SSOClient
        {
            WorkspaceId = _workspace.Id, Domain = "acme.com", ClientId = "client-123"
        });
        _db.SaveChanges();

        var result = await _query.GetSSOLogin("acme.com", _db, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("acme.com", result!.Domain);
        Assert.Equal("client-123", result.ClientId);
    }

    [Fact]
    public async Task GetSSOLogin_NullClientId_ReturnsEmptyString()
    {
        _db.SSOClients.Add(new SSOClient
        {
            WorkspaceId = _workspace.Id, Domain = "noclient.com", ClientId = null
        });
        _db.SaveChanges();

        var result = await _query.GetSSOLogin("noclient.com", _db, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("", result!.ClientId);
    }

    [Fact]
    public async Task GetSSOLogin_MultipleDomains_ReturnsCorrectOne()
    {
        _db.SSOClients.AddRange(
            new SSOClient { WorkspaceId = _workspace.Id, Domain = "first.com", ClientId = "c1" },
            new SSOClient { WorkspaceId = _workspace.Id, Domain = "second.com", ClientId = "c2" });
        _db.SaveChanges();

        var result = await _query.GetSSOLogin("second.com", _db, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("second.com", result!.Domain);
        Assert.Equal("c2", result.ClientId);
    }

    // ══════════════════════════════════════════════════════════════════
    // GetWorkspaceAccessRequests
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetWorkspaceAccessRequests_NoRequests_ReturnsEmpty()
    {
        var result = await _query.GetWorkspaceAccessRequests(
            _workspace.Id, _principal, _authz, _db, CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetWorkspaceAccessRequests_ReturnsRequestsOrderedByDateDesc()
    {
        _db.WorkspaceAccessRequests.AddRange(
            new WorkspaceAccessRequest
            {
                AdminId = 1, LastRequestedWorkspace = _workspace.Id,
                CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            },
            new WorkspaceAccessRequest
            {
                AdminId = 2, LastRequestedWorkspace = _workspace.Id,
                CreatedAt = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc)
            },
            new WorkspaceAccessRequest
            {
                AdminId = 3, LastRequestedWorkspace = _workspace.Id,
                CreatedAt = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc)
            });
        _db.SaveChanges();

        var result = await _query.GetWorkspaceAccessRequests(
            _workspace.Id, _principal, _authz, _db, CancellationToken.None);

        Assert.Equal(3, result.Count);
        Assert.Equal(2, result[0].AdminId); // March
        Assert.Equal(3, result[1].AdminId); // February
        Assert.Equal(1, result[2].AdminId); // January
    }

    [Fact]
    public async Task GetWorkspaceAccessRequests_DoesNotReturnRequestsForOtherWorkspaces()
    {
        var otherWorkspace = new Workspace { Name = "OtherWS", PlanTier = "Enterprise" };
        _db.Workspaces.Add(otherWorkspace);
        _db.SaveChanges();

        _db.WorkspaceAccessRequests.Add(new WorkspaceAccessRequest
        {
            AdminId = 99, LastRequestedWorkspace = otherWorkspace.Id,
            CreatedAt = DateTime.UtcNow
        });
        _db.SaveChanges();

        var result = await _query.GetWorkspaceAccessRequests(
            _workspace.Id, _principal, _authz, _db, CancellationToken.None);

        Assert.Empty(result);
    }

    // ══════════════════════════════════════════════════════════════════
    // GetIntegrationProjectMappings
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetIntegrationProjectMappings_NoMappings_ReturnsEmpty()
    {
        var result = await _query.GetIntegrationProjectMappings(
            _workspace.Id, null, _principal, _authz, _db, CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetIntegrationProjectMappings_ReturnsAllForWorkspace()
    {
        _db.IntegrationProjectMappings.AddRange(
            new IntegrationProjectMapping { ProjectId = _project.Id, IntegrationType = "GitHub", ExternalId = "repo1" },
            new IntegrationProjectMapping { ProjectId = _project.Id, IntegrationType = "Jira", ExternalId = "proj1" });
        _db.SaveChanges();

        var result = await _query.GetIntegrationProjectMappings(
            _workspace.Id, null, _principal, _authz, _db, CancellationToken.None);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task GetIntegrationProjectMappings_FilteredByType()
    {
        _db.IntegrationProjectMappings.AddRange(
            new IntegrationProjectMapping { ProjectId = _project.Id, IntegrationType = "GitHub", ExternalId = "repo1" },
            new IntegrationProjectMapping { ProjectId = _project.Id, IntegrationType = "Jira", ExternalId = "proj1" });
        _db.SaveChanges();

        var result = await _query.GetIntegrationProjectMappings(
            _workspace.Id, IntegrationType.GitHub, _principal, _authz, _db, CancellationToken.None);

        Assert.Single(result);
        Assert.Equal("GitHub", result[0].IntegrationType);
    }

    [Fact]
    public async Task GetIntegrationProjectMappings_DoesNotReturnMappingsForOtherWorkspaces()
    {
        var otherWorkspace = new Workspace { Name = "OtherWS2", PlanTier = "Enterprise" };
        _db.Workspaces.Add(otherWorkspace);
        _db.SaveChanges();

        var otherProject = new Project { Name = "OtherProj", WorkspaceId = otherWorkspace.Id };
        _db.Projects.Add(otherProject);
        _db.SaveChanges();

        _db.IntegrationProjectMappings.Add(new IntegrationProjectMapping
        {
            ProjectId = otherProject.Id, IntegrationType = "GitHub", ExternalId = "other-repo"
        });
        _db.SaveChanges();

        var result = await _query.GetIntegrationProjectMappings(
            _workspace.Id, null, _principal, _authz, _db, CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetIntegrationProjectMappings_FilterReturnsEmptyWhenNoMatchingType()
    {
        _db.IntegrationProjectMappings.Add(new IntegrationProjectMapping
        {
            ProjectId = _project.Id, IntegrationType = "GitHub", ExternalId = "repo1"
        });
        _db.SaveChanges();

        var result = await _query.GetIntegrationProjectMappings(
            _workspace.Id, IntegrationType.Jira, _principal, _authz, _db, CancellationToken.None);

        Assert.Empty(result);
    }

    // ══════════════════════════════════════════════════════════════════
    // Fakes / Stubs
    // ══════════════════════════════════════════════════════════════════

    private class FakeAuthorizationService : IAuthorizationService
    {
        private readonly Admin _admin = new() { Uid = "test-uid-analytics", Name = "Test Admin", Email = "test@test.com" };

        public Task<Admin> GetCurrentAdminAsync(string uid, CancellationToken ct = default)
            => Task.FromResult(_admin);

        public Task<Workspace> IsAdminInWorkspaceAsync(int adminId, int workspaceId, CancellationToken ct = default)
            => Task.FromResult(new Workspace { Name = "Test" });

        public Task<Workspace> IsAdminInWorkspaceFullAccessAsync(int adminId, int workspaceId, CancellationToken ct = default)
            => Task.FromResult(new Workspace { Name = "Test" });

        public Task<Project> IsAdminInProjectAsync(int adminId, int projectId, CancellationToken ct = default)
            => Task.FromResult(new Project { Name = "Test" });

        public Task<(string Role, List<int>? ProjectIds)?> GetAdminRoleAsync(int adminId, int workspaceId, CancellationToken ct = default)
            => Task.FromResult<(string Role, List<int>? ProjectIds)?>(("ADMIN", null));

        public Task ValidateAdminRoleAsync(int adminId, int workspaceId, CancellationToken ct = default)
            => Task.CompletedTask;
    }
}
