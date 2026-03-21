using System.Security.Claims;
using HoldFast.Data;
using HoldFast.Domain.Entities;
using HoldFast.GraphQL.Private;
using HoldFast.Shared.Auth;
using HotChocolate;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HoldFast.Shared.Tests;

/// <summary>
/// Tests for Visualization and Graph CRUD mutations/queries.
/// </summary>
public class VisualizationGraphTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly HoldFastDbContext _db;
    private readonly PrivateMutation _mutation;
    private readonly PrivateQuery _query;
    private readonly AuthorizationService _authz;
    private readonly ClaimsPrincipal _principal;
    private readonly Admin _admin;
    private readonly Project _project;

    private static ClaimsPrincipal MakePrincipal(string uid) =>
        new(new ClaimsIdentity(new[]
        {
            new Claim(HoldFastClaimTypes.Uid, uid),
            new Claim(HoldFastClaimTypes.Email, $"{uid}@test.com"),
        }, "Test"));

    public VisualizationGraphTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<HoldFastDbContext>()
            .UseSqlite(_connection).Options;
        _db = new HoldFastDbContext(options);
        _db.Database.EnsureCreated();

        _admin = new Admin { Uid = "viz-uid", Name = "Viz Admin", Email = "viz@test.com" };
        _db.Admins.Add(_admin);
        var workspace = new Workspace { Name = "WS", PlanTier = "Enterprise" };
        _db.Workspaces.Add(workspace);
        _db.SaveChanges();

        _db.WorkspaceAdmins.Add(new WorkspaceAdmin { AdminId = _admin.Id, WorkspaceId = workspace.Id, Role = "ADMIN" });
        _project = new Project { Name = "Proj", WorkspaceId = workspace.Id };
        _db.Projects.Add(_project);
        _db.SaveChanges();

        _authz = new AuthorizationService(_db);
        _principal = MakePrincipal("viz-uid");
        _mutation = new PrivateMutation();
        _query = new PrivateQuery();
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Close();
        _connection.Dispose();
    }

    // ── Visualization CRUD ───────────────────────────────────────────

    [Fact]
    public async Task UpsertVisualization_Create_ReturnsNewId()
    {
        var id = await _mutation.UpsertVisualization(
            _project.Id, "My Dashboard", null, _principal, _authz, _db, CancellationToken.None);

        Assert.True(id > 0);
        var viz = await _db.Visualizations.FindAsync(id);
        Assert.NotNull(viz);
        Assert.Equal("My Dashboard", viz!.Name);
        Assert.Equal(_project.Id, viz.ProjectId);
    }

    [Fact]
    public async Task UpsertVisualization_Update_ChangesName()
    {
        var id = await _mutation.UpsertVisualization(
            _project.Id, "Old", null, _principal, _authz, _db, CancellationToken.None);

        await _mutation.UpsertVisualization(
            _project.Id, "New", id, _principal, _authz, _db, CancellationToken.None);

        var viz = await _db.Visualizations.FindAsync(id);
        Assert.Equal("New", viz!.Name);
    }

    [Fact]
    public async Task UpsertVisualization_WrongProject_Throws()
    {
        var id = await _mutation.UpsertVisualization(
            _project.Id, "Owned", null, _principal, _authz, _db, CancellationToken.None);

        var otherProject = new Project { Name = "Other", WorkspaceId = _project.WorkspaceId };
        _db.Projects.Add(otherProject);
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.UpsertVisualization(otherProject.Id, "Hack", id, _principal, _authz, _db, CancellationToken.None));
    }

    [Fact]
    public async Task UpsertVisualization_NullName_DefaultsToUntitled()
    {
        var id = await _mutation.UpsertVisualization(
            _project.Id, null, null, _principal, _authz, _db, CancellationToken.None);

        var viz = await _db.Visualizations.FindAsync(id);
        Assert.Equal("Untitled", viz!.Name);
    }

    [Fact]
    public async Task DeleteVisualization_RemovesVizAndGraphs()
    {
        var id = await _mutation.UpsertVisualization(
            _project.Id, "Delete Me", null, _principal, _authz, _db, CancellationToken.None);

        // Add a graph to the visualization
        _db.Graphs.Add(new Graph { ProjectId = _project.Id, VisualizationId = id, Title = "Graph1" });
        await _db.SaveChangesAsync();

        var result = await _mutation.DeleteVisualization(id, _principal, _authz, _db, CancellationToken.None);
        Assert.True(result);
        Assert.Empty(await _db.Visualizations.ToListAsync());
        Assert.Empty(await _db.Graphs.ToListAsync());
    }

    [Fact]
    public async Task DeleteVisualization_NotFound_Throws()
    {
        await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.DeleteVisualization(9999, _principal, _authz, _db, CancellationToken.None));
    }

    [Fact]
    public async Task GetVisualizations_ReturnsWithGraphs()
    {
        var viz = new Visualization { ProjectId = _project.Id, Name = "V1" };
        _db.Visualizations.Add(viz);
        await _db.SaveChangesAsync();

        _db.Graphs.Add(new Graph { ProjectId = _project.Id, VisualizationId = viz.Id, Title = "G1" });
        await _db.SaveChangesAsync();

        var results = await _query.GetVisualizations(_project.Id, _principal, _authz, _db, CancellationToken.None);
        Assert.Single(results);
        Assert.Single(results[0].Graphs);
    }

    [Fact]
    public async Task GetVisualization_ById_Exists()
    {
        var viz = new Visualization { ProjectId = _project.Id, Name = "FindMe" };
        _db.Visualizations.Add(viz);
        await _db.SaveChangesAsync();

        var result = await _query.GetVisualization(viz.Id, _principal, _authz, _db, CancellationToken.None);
        Assert.NotNull(result);
        Assert.Equal("FindMe", result!.Name);
    }

    [Fact]
    public async Task GetVisualization_NotFound_ReturnsNull()
    {
        var result = await _query.GetVisualization(9999, _principal, _authz, _db, CancellationToken.None);
        Assert.Null(result);
    }

    // ── Graph CRUD ───────────────────────────────────────────────────

    [Fact]
    public async Task UpsertGraph_Create_ReturnsGraph()
    {
        var viz = new Visualization { ProjectId = _project.Id, Name = "V" };
        _db.Visualizations.Add(viz);
        await _db.SaveChangesAsync();

        var graph = await _mutation.UpsertGraph(
            viz.Id, "My Graph", "LOGS", "error AND level:ERROR", null, null, null, null, "Line", null,
            _principal, _authz, _db, CancellationToken.None);

        Assert.True(graph.Id > 0);
        Assert.Equal("My Graph", graph.Title);
        Assert.Equal("LOGS", graph.ProductType);
        Assert.Equal(_project.Id, graph.ProjectId);
    }

    [Fact]
    public async Task UpsertGraph_Update_ChangesFields()
    {
        var viz = new Visualization { ProjectId = _project.Id, Name = "V" };
        _db.Visualizations.Add(viz);
        await _db.SaveChangesAsync();

        var graph = await _mutation.UpsertGraph(
            viz.Id, "Old Title", null, null, null, null, null, null, null, null,
            _principal, _authz, _db, CancellationToken.None);

        var updated = await _mutation.UpsertGraph(
            viz.Id, "New Title", "TRACES", "updated query", "service", "Timestamp", 20, 100, "Bar", graph.Id,
            _principal, _authz, _db, CancellationToken.None);

        Assert.Equal(graph.Id, updated.Id);
        Assert.Equal("New Title", updated.Title);
        Assert.Equal("TRACES", updated.ProductType);
        Assert.Equal("service", updated.GroupByKey);
        Assert.Equal(20, updated.BucketCount);
    }

    [Fact]
    public async Task UpsertGraph_VizNotFound_Throws()
    {
        await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.UpsertGraph(9999, "X", null, null, null, null, null, null, null, null,
                _principal, _authz, _db, CancellationToken.None));
    }

    [Fact]
    public async Task DeleteGraph_RemovesGraph()
    {
        var viz = new Visualization { ProjectId = _project.Id, Name = "V" };
        _db.Visualizations.Add(viz);
        await _db.SaveChangesAsync();

        var graph = new Graph { ProjectId = _project.Id, VisualizationId = viz.Id, Title = "G" };
        _db.Graphs.Add(graph);
        await _db.SaveChangesAsync();

        var result = await _mutation.DeleteGraph(graph.Id, _principal, _authz, _db, CancellationToken.None);
        Assert.True(result);
        Assert.Empty(await _db.Graphs.ToListAsync());
    }

    [Fact]
    public async Task DeleteGraph_NotFound_Throws()
    {
        await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.DeleteGraph(9999, _principal, _authz, _db, CancellationToken.None));
    }

    [Fact]
    public async Task GetGraph_ById_Exists()
    {
        var viz = new Visualization { ProjectId = _project.Id, Name = "V" };
        _db.Visualizations.Add(viz);
        await _db.SaveChangesAsync();

        var graph = new Graph { ProjectId = _project.Id, VisualizationId = viz.Id, Title = "Find" };
        _db.Graphs.Add(graph);
        await _db.SaveChangesAsync();

        var result = await _query.GetGraph(graph.Id, _principal, _authz, _db, CancellationToken.None);
        Assert.NotNull(result);
        Assert.Equal("Find", result!.Title);
    }

    [Fact]
    public async Task GetGraph_NotFound_ReturnsNull()
    {
        var result = await _query.GetGraph(9999, _principal, _authz, _db, CancellationToken.None);
        Assert.Null(result);
    }
}
