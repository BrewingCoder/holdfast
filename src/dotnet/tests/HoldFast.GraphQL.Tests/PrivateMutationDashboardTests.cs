using System.Security.Claims;
using HoldFast.Data;
using HoldFast.Domain.Entities;
using HoldFast.GraphQL.Private;
using HoldFast.Shared.Auth;
using HotChocolate;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HoldFast.GraphQL.Tests;

/// <summary>
/// Tests for Dashboard and SavedSegment CRUD mutations.
/// </summary>
public class PrivateMutationDashboardTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly HoldFastDbContext _db;
    private readonly AuthorizationService _authz;
    private readonly PrivateMutation _mutation;
    private readonly ClaimsPrincipal _principal;

    public PrivateMutationDashboardTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<HoldFastDbContext>()
            .UseSqlite(_connection).Options;
        _db = new HoldFastDbContext(options);
        _db.Database.EnsureCreated();
        _mutation = new PrivateMutation();

        // Seed admin, workspace, project
        var admin = new Admin { Uid = "admin-1", Email = "admin@test.com" };
        _db.Admins.Add(admin);
        var workspace = new Workspace { Name = "WS" };
        _db.Workspaces.Add(workspace);
        _db.SaveChanges();

        _db.WorkspaceAdmins.Add(new WorkspaceAdmin { AdminId = admin.Id, WorkspaceId = workspace.Id, Role = "ADMIN" });
        var project = new Project { Name = "Proj", WorkspaceId = workspace.Id };
        _db.Projects.Add(project);
        _db.SaveChanges();

        _authz = new AuthorizationService(_db);
        _principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(HoldFastClaimTypes.Uid, "admin-1"),
            new Claim(HoldFastClaimTypes.AdminId, admin.Id.ToString()),
        }, "Test"));
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Close();
        _connection.Dispose();
    }

    private int ProjectId => _db.Projects.First().Id;

    // ── Dashboard CRUD ────────────────────────────────────────────────

    [Fact]
    public async Task UpsertDashboard_CreateNew()
    {
        var dashboard = await _mutation.UpsertDashboard(
            ProjectId, "My Dashboard", null, _principal, _authz, _db, CancellationToken.None);

        Assert.NotNull(dashboard);
        Assert.Equal("My Dashboard", dashboard.Name);
        Assert.Equal(ProjectId, dashboard.ProjectId);
        Assert.True(dashboard.Id > 0);
    }

    [Fact]
    public async Task UpsertDashboard_UpdateExisting()
    {
        var created = await _mutation.UpsertDashboard(
            ProjectId, "Original", null, _principal, _authz, _db, CancellationToken.None);

        var updated = await _mutation.UpsertDashboard(
            ProjectId, "Updated", created.Id, _principal, _authz, _db, CancellationToken.None);

        Assert.Equal(created.Id, updated.Id);
        Assert.Equal("Updated", updated.Name);
    }

    [Fact]
    public async Task UpsertDashboard_UpdateNonexistent_Throws()
    {
        await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.UpsertDashboard(
                ProjectId, "Name", 99999, _principal, _authz, _db, CancellationToken.None));
    }

    [Fact]
    public async Task DeleteDashboard_Success()
    {
        var dashboard = await _mutation.UpsertDashboard(
            ProjectId, "ToDelete", null, _principal, _authz, _db, CancellationToken.None);

        var result = await _mutation.DeleteDashboard(
            dashboard.Id, _principal, _authz, _db, CancellationToken.None);

        Assert.True(result);
        Assert.Null(await _db.Dashboards.FindAsync(dashboard.Id));
    }

    [Fact]
    public async Task DeleteDashboard_Nonexistent_Throws()
    {
        await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.DeleteDashboard(99999, _principal, _authz, _db, CancellationToken.None));
    }

    [Fact]
    public async Task UpsertDashboard_EmptyName()
    {
        var dashboard = await _mutation.UpsertDashboard(
            ProjectId, "", null, _principal, _authz, _db, CancellationToken.None);
        Assert.Equal("", dashboard.Name);
    }

    [Fact]
    public async Task UpsertDashboard_MultipleDashboardsSameProject()
    {
        await _mutation.UpsertDashboard(ProjectId, "Dashboard 1", null, _principal, _authz, _db, CancellationToken.None);
        await _mutation.UpsertDashboard(ProjectId, "Dashboard 2", null, _principal, _authz, _db, CancellationToken.None);
        await _mutation.UpsertDashboard(ProjectId, "Dashboard 3", null, _principal, _authz, _db, CancellationToken.None);

        var count = await _db.Dashboards.CountAsync(d => d.ProjectId == ProjectId);
        Assert.Equal(3, count);
    }

    // ── Saved Segment CRUD ────────────────────────────────────────────

    [Fact]
    public async Task CreateSavedSegment_Success()
    {
        var segment = await _mutation.CreateSavedSegment(
            ProjectId, "Active Users", "Session", "{\"filter\":\"active\"}", _principal, _authz, _db, CancellationToken.None);

        Assert.NotNull(segment);
        Assert.Equal("Active Users", segment.Name);
        Assert.Equal("Session", segment.EntityType);
        Assert.Equal("{\"filter\":\"active\"}", segment.Params);
    }

    [Fact]
    public async Task CreateSavedSegment_NullParams()
    {
        var segment = await _mutation.CreateSavedSegment(
            ProjectId, "All Errors", "Error", null, _principal, _authz, _db, CancellationToken.None);

        Assert.Null(segment.Params);
    }

    [Fact]
    public async Task EditSavedSegment_UpdateName()
    {
        var segment = await _mutation.CreateSavedSegment(
            ProjectId, "Old Name", "Session", null, _principal, _authz, _db, CancellationToken.None);

        var edited = await _mutation.EditSavedSegment(
            segment.Id, "New Name", null, _principal, _authz, _db, CancellationToken.None);

        Assert.Equal("New Name", edited.Name);
    }

    [Fact]
    public async Task EditSavedSegment_UpdateParams()
    {
        var segment = await _mutation.CreateSavedSegment(
            ProjectId, "Test", "Session", "{\"old\":true}", _principal, _authz, _db, CancellationToken.None);

        var edited = await _mutation.EditSavedSegment(
            segment.Id, null, "{\"new\":true}", _principal, _authz, _db, CancellationToken.None);

        Assert.Equal("Test", edited.Name); // Name unchanged
        Assert.Equal("{\"new\":true}", edited.Params);
    }

    [Fact]
    public async Task EditSavedSegment_NullsLeaveUnchanged()
    {
        var segment = await _mutation.CreateSavedSegment(
            ProjectId, "Keep", "Session", "{\"keep\":true}", _principal, _authz, _db, CancellationToken.None);

        var edited = await _mutation.EditSavedSegment(
            segment.Id, null, null, _principal, _authz, _db, CancellationToken.None);

        Assert.Equal("Keep", edited.Name);
        Assert.Equal("{\"keep\":true}", edited.Params);
    }

    [Fact]
    public async Task EditSavedSegment_Nonexistent_Throws()
    {
        await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.EditSavedSegment(99999, "Name", null, _principal, _authz, _db, CancellationToken.None));
    }

    [Fact]
    public async Task DeleteSavedSegment_Success()
    {
        var segment = await _mutation.CreateSavedSegment(
            ProjectId, "ToDelete", "Error", null, _principal, _authz, _db, CancellationToken.None);

        var result = await _mutation.DeleteSavedSegment(
            segment.Id, _principal, _authz, _db, CancellationToken.None);

        Assert.True(result);
        Assert.Null(await _db.SavedSegments.FindAsync(segment.Id));
    }

    [Fact]
    public async Task DeleteSavedSegment_Nonexistent_Throws()
    {
        await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.DeleteSavedSegment(99999, _principal, _authz, _db, CancellationToken.None));
    }

    [Fact]
    public async Task CreateSavedSegment_DifferentEntityTypes()
    {
        var s1 = await _mutation.CreateSavedSegment(ProjectId, "S1", "Session", null, _principal, _authz, _db, CancellationToken.None);
        var s2 = await _mutation.CreateSavedSegment(ProjectId, "S2", "Error", null, _principal, _authz, _db, CancellationToken.None);
        var s3 = await _mutation.CreateSavedSegment(ProjectId, "S3", "Log", null, _principal, _authz, _db, CancellationToken.None);

        Assert.Equal("Session", s1.EntityType);
        Assert.Equal("Error", s2.EntityType);
        Assert.Equal("Log", s3.EntityType);
    }
}
