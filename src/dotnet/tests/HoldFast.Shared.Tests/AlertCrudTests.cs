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
/// Tests for legacy alert CRUD mutations (ErrorAlert, SessionAlert, LogAlert, MetricMonitor disable toggle).
/// </summary>
public class AlertCrudTests : IDisposable
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

    public AlertCrudTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<HoldFastDbContext>()
            .UseSqlite(_connection).Options;
        _db = new HoldFastDbContext(options);
        _db.Database.EnsureCreated();

        _admin = new Admin { Uid = "alert-uid", Name = "Alert Admin", Email = "alert@test.com" };
        _db.Admins.Add(_admin);
        var workspace = new Workspace { Name = "WS", PlanTier = "Enterprise" };
        _db.Workspaces.Add(workspace);
        _db.SaveChanges();

        _db.WorkspaceAdmins.Add(new WorkspaceAdmin { AdminId = _admin.Id, WorkspaceId = workspace.Id, Role = "ADMIN" });
        _project = new Project { Name = "Proj", WorkspaceId = workspace.Id };
        _db.Projects.Add(_project);
        _db.SaveChanges();

        _authz = new AuthorizationService(_db);
        _principal = MakePrincipal("alert-uid");
        _mutation = new PrivateMutation();
        _query = new PrivateQuery();
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Close();
        _connection.Dispose();
    }

    // ── Error Alert Tests ────────────────────────────────────────────

    [Fact]
    public async Task UpdateErrorAlert_UpdatesFields()
    {
        var alert = new ErrorAlert { ProjectId = _project.Id, Name = "Old", CountThreshold = 1, Frequency = 60 };
        _db.ErrorAlerts.Add(alert);
        await _db.SaveChangesAsync();

        var result = await _mutation.UpdateErrorAlert(
            _project.Id, alert.Id, "New Name", 5, 300, "error query", null, 120,
            _principal, _authz, _db, CancellationToken.None);

        Assert.Equal("New Name", result.Name);
        Assert.Equal(5, result.CountThreshold);
        Assert.Equal(300, result.ThresholdWindow);
        Assert.Equal("error query", result.Query);
        Assert.Equal(120, result.Frequency);
        Assert.Equal(_admin.Id, result.LastAdminToEditId);
    }

    [Fact]
    public async Task UpdateErrorAlert_NotFound_Throws()
    {
        await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.UpdateErrorAlert(_project.Id, 9999, "x", null, null, null, null, null,
                _principal, _authz, _db, CancellationToken.None));
    }

    [Fact]
    public async Task UpdateErrorAlert_PartialUpdate_OnlyChangesProvidedFields()
    {
        var alert = new ErrorAlert { ProjectId = _project.Id, Name = "Keep", CountThreshold = 10, Frequency = 30 };
        _db.ErrorAlerts.Add(alert);
        await _db.SaveChangesAsync();

        var result = await _mutation.UpdateErrorAlert(
            _project.Id, alert.Id, null, null, null, null, true, null,
            _principal, _authz, _db, CancellationToken.None);

        Assert.Equal("Keep", result.Name);
        Assert.Equal(10, result.CountThreshold);
        Assert.True(result.Disabled);
    }

    [Fact]
    public async Task DeleteErrorAlert_RemovesAlert()
    {
        var alert = new ErrorAlert { ProjectId = _project.Id, Name = "Doomed" };
        _db.ErrorAlerts.Add(alert);
        await _db.SaveChangesAsync();

        var result = await _mutation.DeleteErrorAlert(_project.Id, alert.Id, _principal, _authz, _db, CancellationToken.None);

        Assert.Equal("Doomed", result.Name);
        Assert.Empty(await _db.ErrorAlerts.ToListAsync());
    }

    [Fact]
    public async Task DeleteErrorAlert_WrongProject_Throws()
    {
        var alert = new ErrorAlert { ProjectId = _project.Id, Name = "A" };
        _db.ErrorAlerts.Add(alert);
        await _db.SaveChangesAsync();

        var otherProject = new Project { Name = "Other", WorkspaceId = _project.WorkspaceId };
        _db.Projects.Add(otherProject);
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.DeleteErrorAlert(otherProject.Id, alert.Id, _principal, _authz, _db, CancellationToken.None));
    }

    [Fact]
    public async Task UpdateErrorAlertIsDisabled_TogglesDisabled()
    {
        var alert = new ErrorAlert { ProjectId = _project.Id, Name = "Toggle", Disabled = false };
        _db.ErrorAlerts.Add(alert);
        await _db.SaveChangesAsync();

        var result = await _mutation.UpdateErrorAlertIsDisabled(
            alert.Id, _project.Id, true, _principal, _authz, _db, CancellationToken.None);
        Assert.True(result.Disabled);

        result = await _mutation.UpdateErrorAlertIsDisabled(
            alert.Id, _project.Id, false, _principal, _authz, _db, CancellationToken.None);
        Assert.False(result.Disabled);
    }

    [Fact]
    public async Task GetErrorAlerts_ReturnsProjectAlerts()
    {
        _db.ErrorAlerts.AddRange(
            new ErrorAlert { ProjectId = _project.Id, Name = "A" },
            new ErrorAlert { ProjectId = _project.Id, Name = "B" });
        await _db.SaveChangesAsync();

        var results = await _query.GetErrorAlerts(_project.Id, _principal, _authz, _db, CancellationToken.None);
        Assert.Equal(2, results.Count);
    }

    // ── Session Alert Tests ──────────────────────────────────────────

    [Fact]
    public async Task UpdateSessionAlert_UpdatesFields()
    {
        var alert = new SessionAlert { ProjectId = _project.Id, Name = "Old", Type = "NEW_SESSION" };
        _db.SessionAlerts.Add(alert);
        await _db.SaveChangesAsync();

        var result = await _mutation.UpdateSessionAlert(
            alert.Id, _project.Id, "Updated", 3, 600, "session query", false,
            _principal, _authz, _db, CancellationToken.None);

        Assert.Equal("Updated", result.Name);
        Assert.Equal(3, result.CountThreshold);
        Assert.Equal(600, result.ThresholdWindow);
    }

    [Fact]
    public async Task DeleteSessionAlert_RemovesAlert()
    {
        var alert = new SessionAlert { ProjectId = _project.Id, Name = "Remove Me" };
        _db.SessionAlerts.Add(alert);
        await _db.SaveChangesAsync();

        await _mutation.DeleteSessionAlert(_project.Id, alert.Id, _principal, _authz, _db, CancellationToken.None);
        Assert.Empty(await _db.SessionAlerts.ToListAsync());
    }

    [Fact]
    public async Task UpdateSessionAlertIsDisabled_TogglesState()
    {
        var alert = new SessionAlert { ProjectId = _project.Id, Name = "Toggle" };
        _db.SessionAlerts.Add(alert);
        await _db.SaveChangesAsync();

        var result = await _mutation.UpdateSessionAlertIsDisabled(
            alert.Id, _project.Id, true, _principal, _authz, _db, CancellationToken.None);
        Assert.True(result.Disabled);
    }

    [Fact]
    public async Task GetSessionAlerts_FiltersById()
    {
        _db.SessionAlerts.AddRange(
            new SessionAlert { ProjectId = _project.Id, Name = "A", Type = "NEW_SESSION" },
            new SessionAlert { ProjectId = _project.Id, Name = "B", Type = "RAGE_CLICK" });
        await _db.SaveChangesAsync();

        var all = await _query.GetSessionAlerts(_project.Id, null, _principal, _authz, _db, CancellationToken.None);
        Assert.Equal(2, all.Count);

        var filtered = await _query.GetSessionAlerts(_project.Id, "RAGE_CLICK", _principal, _authz, _db, CancellationToken.None);
        Assert.Single(filtered);
        Assert.Equal("B", filtered[0].Name);
    }

    // ── Log Alert Tests ──────────────────────────────────────────────

    [Fact]
    public async Task UpdateLogAlert_UpdatesFields()
    {
        var alert = new LogAlert { ProjectId = _project.Id, Name = "Log Old" };
        _db.LogAlerts.Add(alert);
        await _db.SaveChangesAsync();

        var result = await _mutation.UpdateLogAlert(
            alert.Id, _project.Id, "Log New", 10, 900, 5, "log query", null,
            _principal, _authz, _db, CancellationToken.None);

        Assert.Equal("Log New", result.Name);
        Assert.Equal(10, result.CountThreshold);
        Assert.Equal(900, result.ThresholdWindow);
        Assert.Equal(5, result.BelowThreshold);
        Assert.Equal("log query", result.Query);
    }

    [Fact]
    public async Task DeleteLogAlert_RemovesAlert()
    {
        var alert = new LogAlert { ProjectId = _project.Id, Name = "Bye Log" };
        _db.LogAlerts.Add(alert);
        await _db.SaveChangesAsync();

        await _mutation.DeleteLogAlert(_project.Id, alert.Id, _principal, _authz, _db, CancellationToken.None);
        Assert.Empty(await _db.LogAlerts.ToListAsync());
    }

    [Fact]
    public async Task UpdateLogAlertIsDisabled_TogglesState()
    {
        var alert = new LogAlert { ProjectId = _project.Id, Name = "Toggle Log" };
        _db.LogAlerts.Add(alert);
        await _db.SaveChangesAsync();

        var result = await _mutation.UpdateLogAlertIsDisabled(
            alert.Id, _project.Id, true, _principal, _authz, _db, CancellationToken.None);
        Assert.True(result.Disabled);
    }

    [Fact]
    public async Task GetLogAlerts_ReturnsProjectAlerts()
    {
        _db.LogAlerts.AddRange(
            new LogAlert { ProjectId = _project.Id, Name = "LA" },
            new LogAlert { ProjectId = _project.Id, Name = "LB" });
        await _db.SaveChangesAsync();

        var results = await _query.GetLogAlerts(_project.Id, _principal, _authz, _db, CancellationToken.None);
        Assert.Equal(2, results.Count);
    }

    [Fact]
    public async Task GetLogAlert_ReturnsById()
    {
        var alert = new LogAlert { ProjectId = _project.Id, Name = "FindMe" };
        _db.LogAlerts.Add(alert);
        await _db.SaveChangesAsync();

        var result = await _query.GetLogAlert(alert.Id, _project.Id, _principal, _authz, _db, CancellationToken.None);
        Assert.NotNull(result);
        Assert.Equal("FindMe", result!.Name);
    }

    [Fact]
    public async Task GetLogAlert_WrongProject_Throws()
    {
        var alert = new LogAlert { ProjectId = _project.Id, Name = "Wrong" };
        _db.LogAlerts.Add(alert);
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<GraphQLException>(() =>
            _query.GetLogAlert(alert.Id, 9999, _principal, _authz, _db, CancellationToken.None));
    }

    // ── Metric Monitor Disable Toggle ────────────────────────────────

    [Fact]
    public async Task UpdateMetricMonitorIsDisabled_TogglesState()
    {
        var monitor = new MetricMonitor { ProjectId = _project.Id, Name = "MM", Disabled = false };
        _db.MetricMonitors.Add(monitor);
        await _db.SaveChangesAsync();

        var result = await _mutation.UpdateMetricMonitorIsDisabled(
            monitor.Id, _project.Id, true, _principal, _authz, _db, CancellationToken.None);
        Assert.True(result.Disabled);

        result = await _mutation.UpdateMetricMonitorIsDisabled(
            monitor.Id, _project.Id, false, _principal, _authz, _db, CancellationToken.None);
        Assert.False(result.Disabled);
    }

    [Fact]
    public async Task UpdateMetricMonitorIsDisabled_NotFound_Throws()
    {
        await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.UpdateMetricMonitorIsDisabled(9999, _project.Id, true,
                _principal, _authz, _db, CancellationToken.None));
    }
}
