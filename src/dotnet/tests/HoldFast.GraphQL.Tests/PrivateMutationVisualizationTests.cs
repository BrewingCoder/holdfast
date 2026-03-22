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
/// Tests for Visualization and Graph CRUD mutations, plus UpdateErrorTags.
/// </summary>
public class PrivateMutationVisualizationTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly HoldFastDbContext _db;
    private readonly AuthorizationService _authz;
    private readonly PrivateMutation _mutation;
    private readonly ClaimsPrincipal _principal;
    private readonly Admin _admin;
    private readonly Workspace _workspace;
    private readonly Project _project;

    public PrivateMutationVisualizationTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<HoldFastDbContext>()
            .UseSqlite(_connection).Options;
        _db = new HoldFastDbContext(options);
        _db.Database.EnsureCreated();
        _mutation = new PrivateMutation();

        _admin = new Admin { Uid = "admin-1", Email = "admin@test.com" };
        _db.Admins.Add(_admin);
        _workspace = new Workspace { Name = "WS" };
        _db.Workspaces.Add(_workspace);
        _db.SaveChanges();

        _db.WorkspaceAdmins.Add(new WorkspaceAdmin
            { AdminId = _admin.Id, WorkspaceId = _workspace.Id, Role = "ADMIN" });
        _project = new Project { Name = "Proj", WorkspaceId = _workspace.Id };
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
        _connection.Close();
        _connection.Dispose();
    }

    // ── UpsertVisualization ─────────────────────────────────────────

    [Fact]
    public async Task UpsertVisualization_CreateNew()
    {
        var vizId = await _mutation.UpsertVisualization(
            _project.Id, "My Viz", null,
            _principal, _authz, _db, CancellationToken.None);

        Assert.True(vizId > 0);
        var viz = await _db.Visualizations.FindAsync(vizId);
        Assert.NotNull(viz);
        Assert.Equal("My Viz", viz!.Name);
        Assert.Equal(_project.Id, viz.ProjectId);
    }

    [Fact]
    public async Task UpsertVisualization_CreateWithNullName_DefaultsToUntitled()
    {
        var vizId = await _mutation.UpsertVisualization(
            _project.Id, null, null,
            _principal, _authz, _db, CancellationToken.None);

        var viz = await _db.Visualizations.FindAsync(vizId);
        Assert.Equal("Untitled", viz!.Name);
    }

    [Fact]
    public async Task UpsertVisualization_UpdateExisting()
    {
        var vizId = await _mutation.UpsertVisualization(
            _project.Id, "Original", null,
            _principal, _authz, _db, CancellationToken.None);

        var updatedId = await _mutation.UpsertVisualization(
            _project.Id, "Updated", vizId,
            _principal, _authz, _db, CancellationToken.None);

        Assert.Equal(vizId, updatedId);
        var viz = await _db.Visualizations.FindAsync(vizId);
        Assert.Equal("Updated", viz!.Name);
    }

    [Fact]
    public async Task UpsertVisualization_UpdateWithNullName_LeavesUnchanged()
    {
        var vizId = await _mutation.UpsertVisualization(
            _project.Id, "Keep Me", null,
            _principal, _authz, _db, CancellationToken.None);

        await _mutation.UpsertVisualization(
            _project.Id, null, vizId,
            _principal, _authz, _db, CancellationToken.None);

        var viz = await _db.Visualizations.FindAsync(vizId);
        Assert.Equal("Keep Me", viz!.Name);
    }

    [Fact]
    public async Task UpsertVisualization_UpdateNonexistent_Throws()
    {
        await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.UpsertVisualization(
                _project.Id, "Name", 99999,
                _principal, _authz, _db, CancellationToken.None));
    }

    [Fact]
    public async Task UpsertVisualization_ProjectIdMismatch_Throws()
    {
        var vizId = await _mutation.UpsertVisualization(
            _project.Id, "Test", null,
            _principal, _authz, _db, CancellationToken.None);

        // Create another project
        var project2 = new Project { Name = "Proj2", WorkspaceId = _workspace.Id };
        _db.Projects.Add(project2);
        await _db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.UpsertVisualization(
                project2.Id, "Name", vizId,
                _principal, _authz, _db, CancellationToken.None));

        Assert.Contains("Project ID does not match", ex.Message);
    }

    [Fact]
    public async Task UpsertVisualization_MultiplePerProject()
    {
        await _mutation.UpsertVisualization(_project.Id, "V1", null, _principal, _authz, _db, CancellationToken.None);
        await _mutation.UpsertVisualization(_project.Id, "V2", null, _principal, _authz, _db, CancellationToken.None);
        await _mutation.UpsertVisualization(_project.Id, "V3", null, _principal, _authz, _db, CancellationToken.None);

        var count = await _db.Visualizations.CountAsync(v => v.ProjectId == _project.Id);
        Assert.Equal(3, count);
    }

    // ── DeleteVisualization ─────────────────────────────────────────

    [Fact]
    public async Task DeleteVisualization_Success()
    {
        var vizId = await _mutation.UpsertVisualization(
            _project.Id, "Delete Me", null,
            _principal, _authz, _db, CancellationToken.None);

        var result = await _mutation.DeleteVisualization(
            vizId, _principal, _authz, _db, CancellationToken.None);

        Assert.True(result);
        Assert.Null(await _db.Visualizations.FindAsync(vizId));
    }

    [Fact]
    public async Task DeleteVisualization_CascadesGraphs()
    {
        var vizId = await _mutation.UpsertVisualization(
            _project.Id, "WithGraphs", null,
            _principal, _authz, _db, CancellationToken.None);

        // Add graphs
        _db.Graphs.Add(new Graph { VisualizationId = vizId, ProjectId = _project.Id, Title = "G1" });
        _db.Graphs.Add(new Graph { VisualizationId = vizId, ProjectId = _project.Id, Title = "G2" });
        await _db.SaveChangesAsync();

        var graphCount = await _db.Graphs.CountAsync(g => g.VisualizationId == vizId);
        Assert.Equal(2, graphCount);

        await _mutation.DeleteVisualization(
            vizId, _principal, _authz, _db, CancellationToken.None);

        Assert.Equal(0, await _db.Graphs.CountAsync(g => g.VisualizationId == vizId));
    }

    [Fact]
    public async Task DeleteVisualization_Nonexistent_Throws()
    {
        await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.DeleteVisualization(
                99999, _principal, _authz, _db, CancellationToken.None));
    }

    // ── UpsertGraph ─────────────────────────────────────────────────

    [Fact]
    public async Task UpsertGraph_CreateNew()
    {
        var vizId = await _mutation.UpsertVisualization(
            _project.Id, "Viz", null,
            _principal, _authz, _db, CancellationToken.None);

        var graph = await _mutation.UpsertGraph(
            vizId, "My Graph", "Errors", "status:error", "browser", "timestamp",
            10, 100, "line", null,
            _principal, _authz, _db, CancellationToken.None);

        Assert.True(graph.Id > 0);
        Assert.Equal("My Graph", graph.Title);
        Assert.Equal("Errors", graph.ProductType);
        Assert.Equal("status:error", graph.Query);
        Assert.Equal("browser", graph.GroupByKey);
        Assert.Equal("timestamp", graph.BucketByKey);
        Assert.Equal(10, graph.BucketCount);
        Assert.Equal(100, graph.Limit);
        Assert.Equal("line", graph.Display);
        Assert.Equal(vizId, graph.VisualizationId);
        Assert.Equal(_project.Id, graph.ProjectId);
    }

    [Fact]
    public async Task UpsertGraph_UpdateExisting()
    {
        var vizId = await _mutation.UpsertVisualization(
            _project.Id, "Viz", null,
            _principal, _authz, _db, CancellationToken.None);

        var created = await _mutation.UpsertGraph(
            vizId, "Original", null, null, null, null, null, null, null, null,
            _principal, _authz, _db, CancellationToken.None);

        var updated = await _mutation.UpsertGraph(
            vizId, "Updated Title", "Sessions", "query:new", "os", "hour",
            5, 50, "bar", created.Id,
            _principal, _authz, _db, CancellationToken.None);

        Assert.Equal(created.Id, updated.Id);
        Assert.Equal("Updated Title", updated.Title);
        Assert.Equal("Sessions", updated.ProductType);
        Assert.Equal("query:new", updated.Query);
        Assert.Equal("os", updated.GroupByKey);
        Assert.Equal("hour", updated.BucketByKey);
        Assert.Equal(5, updated.BucketCount);
        Assert.Equal(50, updated.Limit);
        Assert.Equal("bar", updated.Display);
    }

    [Fact]
    public async Task UpsertGraph_NonexistentVisualization_Throws()
    {
        await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.UpsertGraph(
                99999, "Title", null, null, null, null, null, null, null, null,
                _principal, _authz, _db, CancellationToken.None));
    }

    [Fact]
    public async Task UpsertGraph_NonexistentGraph_Throws()
    {
        var vizId = await _mutation.UpsertVisualization(
            _project.Id, "Viz", null,
            _principal, _authz, _db, CancellationToken.None);

        await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.UpsertGraph(
                vizId, "Title", null, null, null, null, null, null, null, 99999,
                _principal, _authz, _db, CancellationToken.None));
    }

    [Fact]
    public async Task UpsertGraph_NullableFields()
    {
        var vizId = await _mutation.UpsertVisualization(
            _project.Id, "Viz", null,
            _principal, _authz, _db, CancellationToken.None);

        var graph = await _mutation.UpsertGraph(
            vizId, "Minimal", null, null, null, null, null, null, null, null,
            _principal, _authz, _db, CancellationToken.None);

        Assert.Null(graph.ProductType);
        Assert.Null(graph.Query);
        Assert.Null(graph.GroupByKey);
        Assert.Null(graph.BucketByKey);
        Assert.Null(graph.BucketCount);
        Assert.Null(graph.Limit);
        Assert.Null(graph.Display);
    }

    // ── DeleteGraph ─────────────────────────────────────────────────

    [Fact]
    public async Task DeleteGraph_Success()
    {
        var vizId = await _mutation.UpsertVisualization(
            _project.Id, "Viz", null,
            _principal, _authz, _db, CancellationToken.None);

        var graph = await _mutation.UpsertGraph(
            vizId, "ToDelete", null, null, null, null, null, null, null, null,
            _principal, _authz, _db, CancellationToken.None);

        var result = await _mutation.DeleteGraph(
            graph.Id, _principal, _authz, _db, CancellationToken.None);

        Assert.True(result);
        Assert.Null(await _db.Graphs.FindAsync(graph.Id));
    }

    [Fact]
    public async Task DeleteGraph_Nonexistent_Throws()
    {
        await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.DeleteGraph(
                99999, _principal, _authz, _db, CancellationToken.None));
    }

    [Fact]
    public async Task DeleteGraph_DoesNotDeleteVisualization()
    {
        var vizId = await _mutation.UpsertVisualization(
            _project.Id, "Viz", null,
            _principal, _authz, _db, CancellationToken.None);

        var graph = await _mutation.UpsertGraph(
            vizId, "Graph", null, null, null, null, null, null, null, null,
            _principal, _authz, _db, CancellationToken.None);

        await _mutation.DeleteGraph(
            graph.Id, _principal, _authz, _db, CancellationToken.None);

        Assert.NotNull(await _db.Visualizations.FindAsync(vizId));
    }

    // ── UpdateErrorTags (no-op placeholder) ─────────────────────────

    [Fact]
    public async Task UpdateErrorTags_ReturnsTrue()
    {
        var result = await _mutation.UpdateErrorTags(
            _principal, _authz, _db, CancellationToken.None);

        Assert.True(result);
    }
}
