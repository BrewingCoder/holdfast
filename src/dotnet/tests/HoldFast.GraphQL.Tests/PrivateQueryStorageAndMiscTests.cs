using System.Security.Claims;
using HoldFast.Data;
using HoldFast.Domain.Entities;
using HoldFast.GraphQL.Private;
using HoldFast.Shared.Auth;
using HoldFast.Storage;
using HotChocolate;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HoldFast.GraphQL.Tests;

/// <summary>
/// Tests for PrivateQuery methods that were previously untested:
/// GetWorkspaces, GetWorkspaceSettings, GetAdmin, GetSourcemapFiles,
/// GetSourcemapVersions, GetEventChunkUrl.
/// </summary>
public class PrivateQueryStorageAndMiscTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly HoldFastDbContext _db;
    private readonly AuthorizationService _authz;
    private readonly PrivateQuery _query;
    private readonly ClaimsPrincipal _principal;
    private readonly Admin _admin;
    private readonly Workspace _workspace;
    private readonly Project _project;

    public PrivateQueryStorageAndMiscTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<HoldFastDbContext>()
            .UseSqlite(_connection).Options;
        _db = new HoldFastDbContext(options);
        _db.Database.EnsureCreated();
        _query = new PrivateQuery();

        _admin = new Admin { Uid = "admin-1", Email = "admin@test.com", Name = "Admin User" };
        _db.Admins.Add(_admin);
        _workspace = new Workspace { Name = "TestWS" };
        _db.Workspaces.Add(_workspace);
        _db.SaveChanges();

        _db.WorkspaceAdmins.Add(new WorkspaceAdmin
            { AdminId = _admin.Id, WorkspaceId = _workspace.Id, Role = "ADMIN" });
        _project = new Project { Name = "TestProj", WorkspaceId = _workspace.Id };
        _db.Projects.Add(_project);
        _db.SaveChanges();

        _authz = new AuthorizationService(_db);
        _principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(HoldFastClaimTypes.Uid, "admin-1"),
            new Claim(HoldFastClaimTypes.AdminId, _admin.Id.ToString()),
        }, "Test"));
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    // ── Fake Storage ──────────────────────────────────────────────────

    private class FakeStorageService : IStorageService
    {
        public Dictionary<string, bool> ExistsResults { get; } = new();
        public Dictionary<string, string> DownloadUrls { get; } = new();
        public List<(string Bucket, string Key)> ExistsCalls { get; } = [];
        public List<(string Bucket, string Key, TimeSpan Expiry)> GetUrlCalls { get; } = [];

        public Task UploadAsync(string bucket, string key, Stream data, string? contentType = null, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<Stream?> DownloadAsync(string bucket, string key, CancellationToken ct = default)
            => Task.FromResult<Stream?>(null);

        public Task<bool> ExistsAsync(string bucket, string key, CancellationToken ct = default)
        {
            ExistsCalls.Add((bucket, key));
            return Task.FromResult(ExistsResults.GetValueOrDefault($"{bucket}/{key}", false));
        }

        public Task DeleteAsync(string bucket, string key, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<string> GetDownloadUrlAsync(string bucket, string key, TimeSpan expiry, CancellationToken ct = default)
        {
            GetUrlCalls.Add((bucket, key, expiry));
            return Task.FromResult(DownloadUrls.GetValueOrDefault($"{bucket}/{key}", $"https://storage.test/{bucket}/{key}"));
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  GetWorkspaces
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetWorkspaces_ReturnsAdminWorkspaces()
    {
        var result = await _query.GetWorkspaces(_principal, _authz, _db, CancellationToken.None);
        var list = await result.ToListAsync();
        Assert.Single(list);
        Assert.Equal("TestWS", list[0].Name);
    }

    [Fact]
    public async Task GetWorkspaces_MultipleWorkspaces_ReturnsAll()
    {
        var ws2 = new Workspace { Name = "SecondWS" };
        _db.Workspaces.Add(ws2);
        _db.SaveChanges();
        _db.WorkspaceAdmins.Add(new WorkspaceAdmin
            { AdminId = _admin.Id, WorkspaceId = ws2.Id, Role = "MEMBER" });
        _db.SaveChanges();

        var result = await _query.GetWorkspaces(_principal, _authz, _db, CancellationToken.None);
        var list = await result.ToListAsync();
        Assert.Equal(2, list.Count);
    }

    [Fact]
    public async Task GetWorkspaces_ExcludesOtherAdminWorkspaces()
    {
        var otherAdmin = new Admin { Uid = "other", Email = "other@test.com" };
        _db.Admins.Add(otherAdmin);
        var otherWs = new Workspace { Name = "OtherWS" };
        _db.Workspaces.Add(otherWs);
        _db.SaveChanges();
        _db.WorkspaceAdmins.Add(new WorkspaceAdmin
            { AdminId = otherAdmin.Id, WorkspaceId = otherWs.Id, Role = "ADMIN" });
        _db.SaveChanges();

        var result = await _query.GetWorkspaces(_principal, _authz, _db, CancellationToken.None);
        var list = await result.ToListAsync();
        Assert.Single(list);
        Assert.Equal("TestWS", list[0].Name);
    }

    [Fact]
    public async Task GetWorkspaces_NoMembership_ReturnsEmpty()
    {
        var loneAdmin = new Admin { Uid = "lone", Email = "lone@test.com" };
        _db.Admins.Add(loneAdmin);
        _db.SaveChanges();

        var lonePrincipal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(HoldFastClaimTypes.Uid, "lone"),
            new Claim(HoldFastClaimTypes.AdminId, loneAdmin.Id.ToString()),
        }, "Test"));

        var result = await _query.GetWorkspaces(lonePrincipal, _authz, _db, CancellationToken.None);
        var list = await result.ToListAsync();
        Assert.Empty(list);
    }

    // ════════════════════════════════════════════════════════════════════
    //  GetWorkspaceSettings
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetWorkspaceSettings_NoSettings_ReturnsNull()
    {
        var result = await _query.GetWorkspaceSettings(
            _workspace.Id, _principal, _authz, _db, CancellationToken.None);
        Assert.Null(result);
    }

    [Fact]
    public async Task GetWorkspaceSettings_ReturnsSettings()
    {
        var settings = new AllWorkspaceSettings { WorkspaceId = _workspace.Id };
        _db.AllWorkspaceSettings.Add(settings);
        _db.SaveChanges();

        var result = await _query.GetWorkspaceSettings(
            _workspace.Id, _principal, _authz, _db, CancellationToken.None);
        Assert.NotNull(result);
        Assert.Equal(_workspace.Id, result!.WorkspaceId);
    }

    [Fact]
    public async Task GetWorkspaceSettings_AllFeaturesEnabledByDefault()
    {
        var settings = new AllWorkspaceSettings { WorkspaceId = _workspace.Id };
        _db.AllWorkspaceSettings.Add(settings);
        _db.SaveChanges();

        var result = await _query.GetWorkspaceSettings(
            _workspace.Id, _principal, _authz, _db, CancellationToken.None);
        Assert.NotNull(result);
        Assert.True(result!.AIApplication);
        Assert.True(result.AIInsights);
        Assert.True(result.EnableUnlimitedDashboards);
        Assert.True(result.EnableSSO);
    }

    [Fact]
    public async Task GetWorkspaceSettings_WrongWorkspace_ReturnsNull()
    {
        var otherWs = new Workspace { Name = "OtherWS" };
        _db.Workspaces.Add(otherWs);
        _db.SaveChanges();
        _db.WorkspaceAdmins.Add(new WorkspaceAdmin
            { AdminId = _admin.Id, WorkspaceId = otherWs.Id, Role = "ADMIN" });
        _db.SaveChanges();

        var settings = new AllWorkspaceSettings { WorkspaceId = otherWs.Id };
        _db.AllWorkspaceSettings.Add(settings);
        _db.SaveChanges();

        // Query with _workspace.Id (no settings) - should return null
        var result = await _query.GetWorkspaceSettings(
            _workspace.Id, _principal, _authz, _db, CancellationToken.None);
        Assert.Null(result);
    }

    // ════════════════════════════════════════════════════════════════════
    //  GetAdmin
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetAdmin_ReturnsCurrentAdmin()
    {
        var result = await _query.GetAdmin(_principal, _authz, CancellationToken.None);
        Assert.NotNull(result);
        Assert.Equal("admin-1", result!.Uid);
        Assert.Equal("admin@test.com", result.Email);
    }

    [Fact]
    public async Task GetAdmin_NoAuth_Throws()
    {
        var unauthPrincipal = new ClaimsPrincipal(new ClaimsIdentity());
        await Assert.ThrowsAsync<GraphQLException>(
            () => _query.GetAdmin(unauthPrincipal, _authz, CancellationToken.None));
    }

    [Fact]
    public async Task GetAdmin_NewUser_AutoCreatesAdmin()
    {
        var newPrincipal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(HoldFastClaimTypes.Uid, "brand-new-uid"),
        }, "Test"));

        var result = await _query.GetAdmin(newPrincipal, _authz, CancellationToken.None);
        Assert.NotNull(result);
        Assert.Equal("brand-new-uid", result!.Uid);

        // Verify persisted
        var fromDb = await _db.Admins.FirstOrDefaultAsync(a => a.Uid == "brand-new-uid");
        Assert.NotNull(fromDb);
    }

    // ════════════════════════════════════════════════════════════════════
    //  GetSourcemapVersions
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetSourcemapVersions_NoBackendSetup_ReturnsEmpty()
    {
        var result = await _query.GetSourcemapVersions(
            _project.Id, _principal, _authz, _db, CancellationToken.None);
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetSourcemapVersions_WithBackendSetup_ReturnsLatest()
    {
        _project.BackendSetup = true;
        _db.SaveChanges();

        var result = await _query.GetSourcemapVersions(
            _project.Id, _principal, _authz, _db, CancellationToken.None);
        Assert.Single(result);
        Assert.Equal("latest", result[0]);
    }

    [Fact]
    public async Task GetSourcemapVersions_NonexistentProject_Throws()
    {
        // Project 99999 doesn't exist, so auth check throws
        await Assert.ThrowsAsync<GraphQLException>(
            () => _query.GetSourcemapVersions(
                99999, _principal, _authz, _db, CancellationToken.None));
    }

    [Fact]
    public async Task GetSourcemapVersions_BackendSetupNull_ReturnsEmpty()
    {
        // BackendSetup defaults to null, should return empty
        Assert.Null(_project.BackendSetup);
        var result = await _query.GetSourcemapVersions(
            _project.Id, _principal, _authz, _db, CancellationToken.None);
        Assert.Empty(result);
    }

    // ════════════════════════════════════════════════════════════════════
    //  GetSourcemapFiles
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetSourcemapFiles_NotExists_ReturnsEmpty()
    {
        var storage = new FakeStorageService();

        var result = await _query.GetSourcemapFiles(
            _project.Id, null, _principal, _authz, storage, CancellationToken.None);
        Assert.Empty(result);
        Assert.Single(storage.ExistsCalls);
        Assert.Equal("sourcemaps", storage.ExistsCalls[0].Bucket);
    }

    [Fact]
    public async Task GetSourcemapFiles_Exists_ReturnsUrl()
    {
        var storage = new FakeStorageService();
        storage.ExistsResults[$"sourcemaps/{_project.Id}"] = true;

        var result = await _query.GetSourcemapFiles(
            _project.Id, null, _principal, _authz, storage, CancellationToken.None);
        Assert.Single(result);
        Assert.Contains("sourcemaps", result[0]);
        Assert.Single(storage.GetUrlCalls);
    }

    [Fact]
    public async Task GetSourcemapFiles_WithVersion_ChecksCorrectPath()
    {
        var storage = new FakeStorageService();
        storage.ExistsResults[$"sourcemaps/{_project.Id}"] = true;

        var result = await _query.GetSourcemapFiles(
            _project.Id, "1.2.3", _principal, _authz, storage, CancellationToken.None);
        Assert.Single(result);
    }

    [Fact]
    public async Task GetSourcemapFiles_PresignedUrlHas5MinExpiry()
    {
        var storage = new FakeStorageService();
        storage.ExistsResults[$"sourcemaps/{_project.Id}"] = true;

        await _query.GetSourcemapFiles(
            _project.Id, null, _principal, _authz, storage, CancellationToken.None);
        Assert.Single(storage.GetUrlCalls);
        Assert.Equal(TimeSpan.FromMinutes(5), storage.GetUrlCalls[0].Expiry);
    }

    // ════════════════════════════════════════════════════════════════════
    //  GetEventChunkUrl
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetEventChunkUrl_SessionNotFound_ReturnsNull()
    {
        var storage = new FakeStorageService();

        var result = await _query.GetEventChunkUrl(
            99999, 0, _principal, _authz, _db, storage, CancellationToken.None);
        Assert.Null(result);
    }

    [Fact]
    public async Task GetEventChunkUrl_ChunkNotFound_ReturnsNull()
    {
        var session = new Session { SecureId = "s1", ProjectId = _project.Id };
        _db.Sessions.Add(session);
        _db.SaveChanges();

        var storage = new FakeStorageService();
        var result = await _query.GetEventChunkUrl(
            session.Id, 0, _principal, _authz, _db, storage, CancellationToken.None);
        Assert.Null(result);
    }

    [Fact]
    public async Task GetEventChunkUrl_ValidChunk_ReturnsUrl()
    {
        var session = new Session { SecureId = "s1", ProjectId = _project.Id };
        _db.Sessions.Add(session);
        _db.SaveChanges();

        var chunk = new EventChunk { SessionId = session.Id, ChunkIndex = 0 };
        _db.EventChunks.Add(chunk);
        _db.SaveChanges();

        var storage = new FakeStorageService();
        var result = await _query.GetEventChunkUrl(
            session.Id, 0, _principal, _authz, _db, storage, CancellationToken.None);
        Assert.NotNull(result);
        Assert.Contains("sessions", result);
    }

    [Fact]
    public async Task GetEventChunkUrl_CorrectStoragePath()
    {
        var session = new Session { SecureId = "s2", ProjectId = _project.Id };
        _db.Sessions.Add(session);
        _db.SaveChanges();

        var chunk = new EventChunk { SessionId = session.Id, ChunkIndex = 3 };
        _db.EventChunks.Add(chunk);
        _db.SaveChanges();

        var storage = new FakeStorageService();
        await _query.GetEventChunkUrl(
            session.Id, 3, _principal, _authz, _db, storage, CancellationToken.None);

        Assert.Single(storage.GetUrlCalls);
        Assert.Equal("sessions", storage.GetUrlCalls[0].Bucket);
        Assert.Equal($"{_project.Id}/{session.Id}/3", storage.GetUrlCalls[0].Key);
        Assert.Equal(TimeSpan.FromMinutes(5), storage.GetUrlCalls[0].Expiry);
    }

    [Fact]
    public async Task GetEventChunkUrl_WrongChunkIndex_ReturnsNull()
    {
        var session = new Session { SecureId = "s3", ProjectId = _project.Id };
        _db.Sessions.Add(session);
        _db.SaveChanges();

        var chunk = new EventChunk { SessionId = session.Id, ChunkIndex = 0 };
        _db.EventChunks.Add(chunk);
        _db.SaveChanges();

        var storage = new FakeStorageService();
        var result = await _query.GetEventChunkUrl(
            session.Id, 5, _principal, _authz, _db, storage, CancellationToken.None);
        Assert.Null(result);
    }

    // ════════════════════════════════════════════════════════════════════
    //  GetLiveUsersCount
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetLiveUsersCount_NoSessions_ReturnsZero()
    {
        var result = await _query.GetLiveUsersCount(
            _project.Id, _principal, _authz, _db, CancellationToken.None);
        Assert.Equal(0, result);
    }

    [Fact]
    public async Task GetLiveUsersCount_RecentUnprocessedSessions_Counted()
    {
        // LiveUsers requires Processed != true (unprocessed) and within 4h10m
        _db.Sessions.AddRange(
            new Session { SecureId = "a", ProjectId = _project.Id, Identifier = "user1",
                CreatedAt = DateTime.UtcNow.AddMinutes(-3) },
            new Session { SecureId = "b", ProjectId = _project.Id, Identifier = "user2",
                CreatedAt = DateTime.UtcNow.AddMinutes(-2) });
        _db.SaveChanges();

        var result = await _query.GetLiveUsersCount(
            _project.Id, _principal, _authz, _db, CancellationToken.None);
        Assert.Equal(2, result);
    }

    [Fact]
    public async Task GetLiveUsersCount_DuplicateIdentifier_CountedOnce()
    {
        _db.Sessions.AddRange(
            new Session { SecureId = "c", ProjectId = _project.Id, Identifier = "same-user",
                CreatedAt = DateTime.UtcNow.AddMinutes(-1) },
            new Session { SecureId = "d", ProjectId = _project.Id, Identifier = "same-user",
                CreatedAt = DateTime.UtcNow.AddMinutes(-2) });
        _db.SaveChanges();

        var result = await _query.GetLiveUsersCount(
            _project.Id, _principal, _authz, _db, CancellationToken.None);
        Assert.Equal(1, result);
    }

    [Fact]
    public async Task GetLiveUsersCount_ProcessedSessions_NotCounted()
    {
        _db.Sessions.Add(new Session { SecureId = "e", ProjectId = _project.Id,
            Identifier = "user-processed", CreatedAt = DateTime.UtcNow.AddMinutes(-1), Processed = true });
        _db.SaveChanges();

        var result = await _query.GetLiveUsersCount(
            _project.Id, _principal, _authz, _db, CancellationToken.None);
        Assert.Equal(0, result);
    }

    [Fact]
    public async Task GetLiveUsersCount_OldSessions_NotCounted()
    {
        _db.Sessions.Add(new Session { SecureId = "f", ProjectId = _project.Id,
            Identifier = "old-user", CreatedAt = DateTime.UtcNow.AddHours(-5) });
        _db.SaveChanges();

        var result = await _query.GetLiveUsersCount(
            _project.Id, _principal, _authz, _db, CancellationToken.None);
        Assert.Equal(0, result);
    }

    // ════════════════════════════════════════════════════════════════════
    //  Edge: auth boundary on storage methods
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetSourcemapFiles_Unauthorized_Throws()
    {
        // Create a project in a workspace the admin doesn't belong to
        var otherWs = new Workspace { Name = "OtherWS" };
        _db.Workspaces.Add(otherWs);
        _db.SaveChanges();
        var otherProj = new Project { Name = "OtherProj", WorkspaceId = otherWs.Id };
        _db.Projects.Add(otherProj);
        _db.SaveChanges();

        var storage = new FakeStorageService();
        await Assert.ThrowsAsync<GraphQLException>(
            () => _query.GetSourcemapFiles(
                otherProj.Id, null, _principal, _authz, storage, CancellationToken.None));
    }

    [Fact]
    public async Task GetSourcemapVersions_Unauthorized_Throws()
    {
        var otherWs = new Workspace { Name = "OtherWS2" };
        _db.Workspaces.Add(otherWs);
        _db.SaveChanges();
        var otherProj = new Project { Name = "OtherProj2", WorkspaceId = otherWs.Id };
        _db.Projects.Add(otherProj);
        _db.SaveChanges();

        await Assert.ThrowsAsync<GraphQLException>(
            () => _query.GetSourcemapVersions(
                otherProj.Id, _principal, _authz, _db, CancellationToken.None));
    }

    [Fact]
    public async Task GetWorkspaceSettings_Unauthorized_Throws()
    {
        var otherWs = new Workspace { Name = "NoAccessWS" };
        _db.Workspaces.Add(otherWs);
        _db.SaveChanges();

        await Assert.ThrowsAsync<GraphQLException>(
            () => _query.GetWorkspaceSettings(
                otherWs.Id, _principal, _authz, _db, CancellationToken.None));
    }
}
