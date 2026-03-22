using System.Security.Claims;
using HoldFast.Data;
using HoldFast.Domain.Entities;
using HoldFast.GraphQL.Private;
using HoldFast.Shared.Auth;
using HotChocolate;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace HoldFast.GraphQL.Tests;

/// <summary>
/// Tests for PrivateMutation alert and metric monitor CRUD:
/// SaveAlert, UpdateAlertDisabled, DeleteAlert,
/// CreateMetricMonitor, UpdateMetricMonitor, DeleteMetricMonitor,
/// UpsertDashboard, DeleteDashboard,
/// CreateSavedSegment, EditSavedSegment, DeleteSavedSegment.
/// </summary>
public class PrivateMutationAlertTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly HoldFastDbContext _db;
    private readonly AuthorizationService _authz;
    private readonly PrivateMutation _mutation;
    private readonly Admin _admin;
    private readonly Workspace _workspace;
    private readonly Project _project;

    public PrivateMutationAlertTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<HoldFastDbContext>()
            .UseSqlite(_connection).Options;
        _db = new HoldFastDbContext(options);
        _db.Database.EnsureCreated();

        _admin = new Admin { Uid = "alert-admin", Email = "alert@test.com" };
        _db.Admins.Add(_admin);
        _workspace = new Workspace { Name = "AlertWS" };
        _db.Workspaces.Add(_workspace);
        _db.SaveChanges();

        _db.WorkspaceAdmins.Add(new WorkspaceAdmin
        {
            AdminId = _admin.Id, WorkspaceId = _workspace.Id, Role = "ADMIN",
        });
        _project = new Project { Name = "AlertProj", WorkspaceId = _workspace.Id };
        _db.Projects.Add(_project);
        _db.SaveChanges();

        _authz = new AuthorizationService(_db);
        _mutation = new PrivateMutation();
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Close();
        _connection.Dispose();
    }

    private static ClaimsPrincipal MakePrincipal(string uid) =>
        new(new ClaimsIdentity(new[]
        {
            new Claim(HoldFastClaimTypes.Uid, uid),
            new Claim(HoldFastClaimTypes.Email, "test@example.com"),
        }, "Test"));

    private static ClaimsPrincipal AnonymousPrincipal => new(new ClaimsIdentity());

    // ── SaveAlert ───────────────────────────────────────────────────

    [Fact]
    public async Task SaveAlert_CreatesNewAlert()
    {
        var result = await _mutation.SaveAlert(
            _project.Id, "CPU Alert", "METRICS_ALERT", "COUNT",
            "cpu > 80", belowThreshold: null, aboveThreshold: 80.0,
            thresholdWindow: 300, disabled: false,
            MakePrincipal("alert-admin"), _authz, _db, CancellationToken.None);

        Assert.Equal("CPU Alert", result.Name);
        Assert.Equal("METRICS_ALERT", result.ProductType);
        Assert.Equal("COUNT", result.FunctionType);
        Assert.Equal(80.0, result.AboveThreshold);
        Assert.Equal(300, result.ThresholdWindow);
        Assert.False(result.Disabled);
    }

    [Fact]
    public async Task SaveAlert_WithBelowThreshold()
    {
        var result = await _mutation.SaveAlert(
            _project.Id, "Low Memory", "METRICS_ALERT", null,
            null, belowThreshold: 10.0, aboveThreshold: null,
            thresholdWindow: null, disabled: false,
            MakePrincipal("alert-admin"), _authz, _db, CancellationToken.None);

        Assert.Equal(10.0, result.BelowThreshold);
        Assert.Null(result.AboveThreshold);
    }

    [Fact]
    public async Task SaveAlert_Disabled()
    {
        var result = await _mutation.SaveAlert(
            _project.Id, "Disabled Alert", "ERRORS_ALERT", null,
            null, null, null, null, disabled: true,
            MakePrincipal("alert-admin"), _authz, _db, CancellationToken.None);

        Assert.True(result.Disabled);
    }

    [Fact]
    public async Task SaveAlert_Unauthenticated_Throws()
    {
        await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.SaveAlert(
                _project.Id, "Alert", "ERRORS_ALERT", null, null, null, null, null, false,
                AnonymousPrincipal, _authz, _db, CancellationToken.None));
    }

    [Fact]
    public async Task SaveAlert_MultipleAlerts_SameProject()
    {
        var a1 = await _mutation.SaveAlert(
            _project.Id, "A1", "ERRORS_ALERT", null, null, null, null, null, false,
            MakePrincipal("alert-admin"), _authz, _db, CancellationToken.None);
        var a2 = await _mutation.SaveAlert(
            _project.Id, "A2", "LOGS_ALERT", null, null, null, null, null, false,
            MakePrincipal("alert-admin"), _authz, _db, CancellationToken.None);

        Assert.NotEqual(a1.Id, a2.Id);
        var count = await _db.Alerts.CountAsync(a => a.ProjectId == _project.Id);
        Assert.Equal(2, count);
    }

    // ── UpdateAlertDisabled ─────────────────────────────────────────

    [Fact]
    public async Task UpdateAlertDisabled_TogglesDisabled()
    {
        var alert = new Alert { ProjectId = _project.Id, Name = "Toggle", ProductType = "ERRORS_ALERT", Disabled = false };
        _db.Alerts.Add(alert);
        _db.SaveChanges();

        var result = await _mutation.UpdateAlertDisabled(
            alert.Id, true,
            MakePrincipal("alert-admin"), _authz, _db, CancellationToken.None);

        Assert.True(result);
        await _db.Entry(alert).ReloadAsync();
        Assert.True(alert.Disabled);
    }

    [Fact]
    public async Task UpdateAlertDisabled_EnablesDisabledAlert()
    {
        var alert = new Alert { ProjectId = _project.Id, Name = "Dis", ProductType = "ERRORS_ALERT", Disabled = true };
        _db.Alerts.Add(alert);
        _db.SaveChanges();

        await _mutation.UpdateAlertDisabled(
            alert.Id, false,
            MakePrincipal("alert-admin"), _authz, _db, CancellationToken.None);

        await _db.Entry(alert).ReloadAsync();
        Assert.False(alert.Disabled);
    }

    [Fact]
    public async Task UpdateAlertDisabled_NonexistentAlert_Throws()
    {
        await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.UpdateAlertDisabled(
                99999, true,
                MakePrincipal("alert-admin"), _authz, _db, CancellationToken.None));
    }

    // ── DeleteAlert ─────────────────────────────────────────────────

    [Fact]
    public async Task DeleteAlert_RemovesAlert()
    {
        var alert = new Alert { ProjectId = _project.Id, Name = "ToRemove", ProductType = "ERRORS_ALERT" };
        _db.Alerts.Add(alert);
        _db.SaveChanges();

        var result = await _mutation.DeleteAlert(
            alert.Id, MakePrincipal("alert-admin"), _authz, _db, CancellationToken.None);

        Assert.True(result);
        Assert.Null(await _db.Alerts.FindAsync(alert.Id));
    }

    [Fact]
    public async Task DeleteAlert_NonexistentAlert_Throws()
    {
        await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.DeleteAlert(
                99999, MakePrincipal("alert-admin"), _authz, _db, CancellationToken.None));
    }

    [Fact]
    public async Task DeleteAlert_Unauthenticated_Throws()
    {
        var alert = new Alert { ProjectId = _project.Id, Name = "Auth", ProductType = "ERRORS_ALERT" };
        _db.Alerts.Add(alert);
        _db.SaveChanges();

        await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.DeleteAlert(
                alert.Id, AnonymousPrincipal, _authz, _db, CancellationToken.None));
    }

    // ── CreateMetricMonitor ─────────────────────────────────────────

    [Fact]
    public async Task CreateMetricMonitor_WithAllFields()
    {
        var result = await _mutation.CreateMetricMonitor(
            _project.Id, "CPU Monitor", "cpu.usage", "AVG", 90.0,
            MakePrincipal("alert-admin"), _authz, _db, CancellationToken.None);

        Assert.Equal("CPU Monitor", result.Name);
        Assert.Equal("cpu.usage", result.MetricToMonitor);
        Assert.Equal("AVG", result.Aggregator);
        Assert.Equal(90.0, result.Threshold);
        Assert.Equal(_project.Id, result.ProjectId);
    }

    [Fact]
    public async Task CreateMetricMonitor_NullOptionalFields()
    {
        var result = await _mutation.CreateMetricMonitor(
            _project.Id, "Basic Monitor", "memory", null, null,
            MakePrincipal("alert-admin"), _authz, _db, CancellationToken.None);

        Assert.Null(result.Aggregator);
        Assert.Null(result.Threshold);
    }

    [Fact]
    public async Task CreateMetricMonitor_Unauthenticated_Throws()
    {
        await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.CreateMetricMonitor(
                _project.Id, "Mon", "metric", null, null,
                AnonymousPrincipal, _authz, _db, CancellationToken.None));
    }

    // ── UpdateMetricMonitor ─────────────────────────────────────────

    [Fact]
    public async Task UpdateMetricMonitor_UpdatesAllFields()
    {
        var monitor = new MetricMonitor
        {
            ProjectId = _project.Id, Name = "Original", MetricToMonitor = "cpu",
            Aggregator = "AVG", Threshold = 50.0, Disabled = false,
        };
        _db.MetricMonitors.Add(monitor);
        _db.SaveChanges();

        var result = await _mutation.UpdateMetricMonitor(
            monitor.Id, "Renamed", "P99", 95.0, true,
            MakePrincipal("alert-admin"), _authz, _db, CancellationToken.None);

        Assert.Equal("Renamed", result.Name);
        Assert.Equal("P99", result.Aggregator);
        Assert.Equal(95.0, result.Threshold);
        Assert.True(result.Disabled);
    }

    [Fact]
    public async Task UpdateMetricMonitor_NullFields_NoChange()
    {
        var monitor = new MetricMonitor
        {
            ProjectId = _project.Id, Name = "Stable", MetricToMonitor = "mem",
            Aggregator = "SUM", Threshold = 75.0,
        };
        _db.MetricMonitors.Add(monitor);
        _db.SaveChanges();

        var result = await _mutation.UpdateMetricMonitor(
            monitor.Id, null, null, null, null,
            MakePrincipal("alert-admin"), _authz, _db, CancellationToken.None);

        Assert.Equal("Stable", result.Name);
        Assert.Equal("SUM", result.Aggregator);
        Assert.Equal(75.0, result.Threshold);
    }

    [Fact]
    public async Task UpdateMetricMonitor_NonexistentMonitor_Throws()
    {
        await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.UpdateMetricMonitor(
                99999, "Name", null, null, null,
                MakePrincipal("alert-admin"), _authz, _db, CancellationToken.None));
    }

    // ── DeleteMetricMonitor ─────────────────────────────────────────

    [Fact]
    public async Task DeleteMetricMonitor_RemovesMonitor()
    {
        var monitor = new MetricMonitor
        {
            ProjectId = _project.Id, Name = "ToDelete", MetricToMonitor = "disk",
        };
        _db.MetricMonitors.Add(monitor);
        _db.SaveChanges();

        var result = await _mutation.DeleteMetricMonitor(
            monitor.Id, MakePrincipal("alert-admin"), _authz, _db, CancellationToken.None);

        Assert.True(result);
        Assert.Null(await _db.MetricMonitors.FindAsync(monitor.Id));
    }

    [Fact]
    public async Task DeleteMetricMonitor_NonexistentMonitor_Throws()
    {
        await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.DeleteMetricMonitor(
                99999, MakePrincipal("alert-admin"), _authz, _db, CancellationToken.None));
    }

    // ── UpsertDashboard ─────────────────────────────────────────────

    [Fact]
    public async Task UpsertDashboard_CreatesNew()
    {
        var result = await _mutation.UpsertDashboard(
            _project.Id, "My Dashboard", null,
            MakePrincipal("alert-admin"), _authz, _db, CancellationToken.None);

        Assert.Equal("My Dashboard", result.Name);
        Assert.Equal(_project.Id, result.ProjectId);
        Assert.True(result.Id > 0);
    }

    [Fact]
    public async Task UpsertDashboard_UpdatesExisting()
    {
        var dash = new Dashboard { ProjectId = _project.Id, Name = "Original" };
        _db.Dashboards.Add(dash);
        _db.SaveChanges();

        var result = await _mutation.UpsertDashboard(
            _project.Id, "Updated Dashboard", dash.Id,
            MakePrincipal("alert-admin"), _authz, _db, CancellationToken.None);

        Assert.Equal(dash.Id, result.Id);
        Assert.Equal("Updated Dashboard", result.Name);
    }

    [Fact]
    public async Task UpsertDashboard_NonexistentDashboardId_Throws()
    {
        await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.UpsertDashboard(
                _project.Id, "Name", 99999,
                MakePrincipal("alert-admin"), _authz, _db, CancellationToken.None));
    }

    // ── DeleteDashboard ─────────────────────────────────────────────

    [Fact]
    public async Task DeleteDashboard_RemovesDashboard()
    {
        var dash = new Dashboard { ProjectId = _project.Id, Name = "ToRemove" };
        _db.Dashboards.Add(dash);
        _db.SaveChanges();

        var result = await _mutation.DeleteDashboard(
            dash.Id, MakePrincipal("alert-admin"), _authz, _db, CancellationToken.None);

        Assert.True(result);
        Assert.Null(await _db.Dashboards.FindAsync(dash.Id));
    }

    [Fact]
    public async Task DeleteDashboard_NonexistentDashboard_Throws()
    {
        await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.DeleteDashboard(
                99999, MakePrincipal("alert-admin"), _authz, _db, CancellationToken.None));
    }

    // ── CreateSavedSegment ──────────────────────────────────────────

    [Fact]
    public async Task CreateSavedSegment_CreatesSuccessfully()
    {
        var result = await _mutation.CreateSavedSegment(
            _project.Id, "Active Users", "Session", "{\"filters\":[]}",
            MakePrincipal("alert-admin"), _authz, _db, CancellationToken.None);

        Assert.Equal("Active Users", result.Name);
        Assert.Equal("Session", result.EntityType);
        Assert.Equal("{\"filters\":[]}", result.Params);
    }

    [Fact]
    public async Task CreateSavedSegment_NullParams()
    {
        var result = await _mutation.CreateSavedSegment(
            _project.Id, "No Params", "Error", null,
            MakePrincipal("alert-admin"), _authz, _db, CancellationToken.None);

        Assert.Null(result.Params);
    }

    // ── EditSavedSegment ────────────────────────────────────────────

    [Fact]
    public async Task EditSavedSegment_UpdatesName()
    {
        var segment = new SavedSegment
        {
            ProjectId = _project.Id, Name = "Old", EntityType = "Session",
        };
        _db.SavedSegments.Add(segment);
        _db.SaveChanges();

        var result = await _mutation.EditSavedSegment(
            segment.Id, "New Name", null,
            MakePrincipal("alert-admin"), _authz, _db, CancellationToken.None);

        Assert.Equal("New Name", result.Name);
    }

    [Fact]
    public async Task EditSavedSegment_NonexistentSegment_Throws()
    {
        await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.EditSavedSegment(
                99999, "Name", null,
                MakePrincipal("alert-admin"), _authz, _db, CancellationToken.None));
    }

    // ── DeleteSavedSegment ──────────────────────────────────────────

    [Fact]
    public async Task DeleteSavedSegment_RemovesSegment()
    {
        var segment = new SavedSegment
        {
            ProjectId = _project.Id, Name = "ToDelete", EntityType = "Log",
        };
        _db.SavedSegments.Add(segment);
        _db.SaveChanges();

        var result = await _mutation.DeleteSavedSegment(
            segment.Id, MakePrincipal("alert-admin"), _authz, _db, CancellationToken.None);

        Assert.True(result);
        Assert.Null(await _db.SavedSegments.FindAsync(segment.Id));
    }

    [Fact]
    public async Task DeleteSavedSegment_NonexistentSegment_Throws()
    {
        await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.DeleteSavedSegment(
                99999, MakePrincipal("alert-admin"), _authz, _db, CancellationToken.None));
    }
}
