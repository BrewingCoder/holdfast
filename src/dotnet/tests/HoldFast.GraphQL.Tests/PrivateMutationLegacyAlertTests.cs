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
/// Tests for legacy alert CRUD: ErrorAlert, SessionAlert, LogAlert.
/// </summary>
public class PrivateMutationLegacyAlertTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly HoldFastDbContext _db;
    private readonly AuthorizationService _authz;
    private readonly PrivateMutation _mutation;
    private readonly ClaimsPrincipal _principal;
    private readonly Admin _admin;
    private readonly Workspace _workspace;
    private readonly Project _project;

    public PrivateMutationLegacyAlertTests()
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

    // ── ErrorAlert CRUD ───────────────────────────────────────────────

    [Fact]
    public async Task UpdateErrorAlert_UpdateName()
    {
        var alert = new ErrorAlert { ProjectId = _project.Id, Name = "Old" };
        _db.ErrorAlerts.Add(alert);
        await _db.SaveChangesAsync();

        var updated = await _mutation.UpdateErrorAlert(
            _project.Id, alert.Id, "New Name", null, null, null, null, null,
            _principal, _authz, _db, CancellationToken.None);

        Assert.Equal("New Name", updated.Name);
    }

    [Fact]
    public async Task UpdateErrorAlert_UpdateThreshold()
    {
        var alert = new ErrorAlert { ProjectId = _project.Id, Name = "Test", CountThreshold = 1, ThresholdWindow = 30 };
        _db.ErrorAlerts.Add(alert);
        await _db.SaveChangesAsync();

        var updated = await _mutation.UpdateErrorAlert(
            _project.Id, alert.Id, null, 10, 60, null, null, null,
            _principal, _authz, _db, CancellationToken.None);

        Assert.Equal(10, updated.CountThreshold);
        Assert.Equal(60, updated.ThresholdWindow);
    }

    [Fact]
    public async Task UpdateErrorAlert_DisableAlert()
    {
        var alert = new ErrorAlert { ProjectId = _project.Id, Name = "Active" };
        _db.ErrorAlerts.Add(alert);
        await _db.SaveChangesAsync();

        var updated = await _mutation.UpdateErrorAlert(
            _project.Id, alert.Id, null, null, null, null, true, null,
            _principal, _authz, _db, CancellationToken.None);

        Assert.True(updated.Disabled);
    }

    [Fact]
    public async Task UpdateErrorAlert_SetsLastAdmin()
    {
        var alert = new ErrorAlert { ProjectId = _project.Id, Name = "Test" };
        _db.ErrorAlerts.Add(alert);
        await _db.SaveChangesAsync();

        var updated = await _mutation.UpdateErrorAlert(
            _project.Id, alert.Id, "Updated", null, null, null, null, null,
            _principal, _authz, _db, CancellationToken.None);

        Assert.Equal(_admin.Id, updated.LastAdminToEditId);
    }

    [Fact]
    public async Task UpdateErrorAlert_Nonexistent_Throws()
    {
        await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.UpdateErrorAlert(
                _project.Id, 99999, "Name", null, null, null, null, null,
                _principal, _authz, _db, CancellationToken.None));
    }

    [Fact]
    public async Task UpdateErrorAlert_NullsLeaveUnchanged()
    {
        var alert = new ErrorAlert
        {
            ProjectId = _project.Id, Name = "Keep", Query = "error",
            CountThreshold = 5, ThresholdWindow = 10, Frequency = 60,
        };
        _db.ErrorAlerts.Add(alert);
        await _db.SaveChangesAsync();

        var updated = await _mutation.UpdateErrorAlert(
            _project.Id, alert.Id, null, null, null, null, null, null,
            _principal, _authz, _db, CancellationToken.None);

        Assert.Equal("Keep", updated.Name);
        Assert.Equal("error", updated.Query);
        Assert.Equal(5, updated.CountThreshold);
    }

    [Fact]
    public async Task DeleteErrorAlert_Success()
    {
        var alert = new ErrorAlert { ProjectId = _project.Id, Name = "Delete Me" };
        _db.ErrorAlerts.Add(alert);
        await _db.SaveChangesAsync();

        var deleted = await _mutation.DeleteErrorAlert(
            _project.Id, alert.Id, _principal, _authz, _db, CancellationToken.None);

        Assert.Equal("Delete Me", deleted.Name);
        Assert.Null(await _db.ErrorAlerts.FindAsync(alert.Id));
    }

    [Fact]
    public async Task DeleteErrorAlert_Nonexistent_Throws()
    {
        await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.DeleteErrorAlert(
                _project.Id, 99999, _principal, _authz, _db, CancellationToken.None));
    }

    [Fact]
    public async Task UpdateErrorAlertIsDisabled_Enable()
    {
        var alert = new ErrorAlert { ProjectId = _project.Id, Name = "Disabled", Disabled = true };
        _db.ErrorAlerts.Add(alert);
        await _db.SaveChangesAsync();

        var updated = await _mutation.UpdateErrorAlertIsDisabled(
            alert.Id, _project.Id, false, _principal, _authz, _db, CancellationToken.None);

        Assert.False(updated.Disabled);
    }

    [Fact]
    public async Task UpdateErrorAlertIsDisabled_Disable()
    {
        var alert = new ErrorAlert { ProjectId = _project.Id, Name = "Active" };
        _db.ErrorAlerts.Add(alert);
        await _db.SaveChangesAsync();

        var updated = await _mutation.UpdateErrorAlertIsDisabled(
            alert.Id, _project.Id, true, _principal, _authz, _db, CancellationToken.None);

        Assert.True(updated.Disabled);
    }

    // ── SessionAlert CRUD ─────────────────────────────────────────────

    [Fact]
    public async Task UpdateSessionAlert_UpdateName()
    {
        var alert = new SessionAlert { ProjectId = _project.Id, Name = "Old", Type = "NEW_SESSION" };
        _db.SessionAlerts.Add(alert);
        await _db.SaveChangesAsync();

        var updated = await _mutation.UpdateSessionAlert(
            alert.Id, _project.Id, "Renamed", null, null, null, null,
            _principal, _authz, _db, CancellationToken.None);

        Assert.Equal("Renamed", updated.Name);
    }

    [Fact]
    public async Task UpdateSessionAlert_Nonexistent_Throws()
    {
        await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.UpdateSessionAlert(
                99999, _project.Id, "Name", null, null, null, null,
                _principal, _authz, _db, CancellationToken.None));
    }

    [Fact]
    public async Task DeleteSessionAlert_Success()
    {
        var alert = new SessionAlert { ProjectId = _project.Id, Name = "Delete", Type = "NEW_SESSION" };
        _db.SessionAlerts.Add(alert);
        await _db.SaveChangesAsync();

        var deleted = await _mutation.DeleteSessionAlert(
            alert.Id, _project.Id, _principal, _authz, _db, CancellationToken.None);

        Assert.Equal("Delete", deleted.Name);
    }

    [Fact]
    public async Task UpdateSessionAlertIsDisabled_Toggle()
    {
        var alert = new SessionAlert { ProjectId = _project.Id, Name = "Toggle", Type = "NEW_SESSION" };
        _db.SessionAlerts.Add(alert);
        await _db.SaveChangesAsync();

        var disabled = await _mutation.UpdateSessionAlertIsDisabled(
            alert.Id, _project.Id, true, _principal, _authz, _db, CancellationToken.None);
        Assert.True(disabled.Disabled);

        var enabled = await _mutation.UpdateSessionAlertIsDisabled(
            alert.Id, _project.Id, false, _principal, _authz, _db, CancellationToken.None);
        Assert.False(enabled.Disabled);
    }

    // ── LogAlert CRUD ─────────────────────────────────────────────────

    [Fact]
    public async Task UpdateLogAlert_UpdateFields()
    {
        var alert = new LogAlert { ProjectId = _project.Id, Name = "Log Alert" };
        _db.LogAlerts.Add(alert);
        await _db.SaveChangesAsync();

        var updated = await _mutation.UpdateLogAlert(
            alert.Id, _project.Id, "Updated Log Alert", 5, 60, null, "error", null,
            _principal, _authz, _db, CancellationToken.None);

        Assert.Equal("Updated Log Alert", updated.Name);
        Assert.Equal(5, updated.CountThreshold);
        Assert.Equal(60, updated.ThresholdWindow);
        Assert.Equal("error", updated.Query);
    }

    [Fact]
    public async Task UpdateLogAlert_Nonexistent_Throws()
    {
        await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.UpdateLogAlert(
                99999, _project.Id, "Name", null, null, null, null, null,
                _principal, _authz, _db, CancellationToken.None));
    }

    [Fact]
    public async Task DeleteLogAlert_Success()
    {
        var alert = new LogAlert { ProjectId = _project.Id, Name = "Log Delete" };
        _db.LogAlerts.Add(alert);
        await _db.SaveChangesAsync();

        var deleted = await _mutation.DeleteLogAlert(
            alert.Id, _project.Id, _principal, _authz, _db, CancellationToken.None);

        Assert.Equal("Log Delete", deleted.Name);
    }

    [Fact]
    public async Task UpdateLogAlertIsDisabled_Toggle()
    {
        var alert = new LogAlert { ProjectId = _project.Id, Name = "Log Toggle" };
        _db.LogAlerts.Add(alert);
        await _db.SaveChangesAsync();

        var disabled = await _mutation.UpdateLogAlertIsDisabled(
            alert.Id, _project.Id, true, _principal, _authz, _db, CancellationToken.None);
        Assert.True(disabled.Disabled);
    }

    // ── MetricMonitor IsDisabled ──────────────────────────────────────

    [Fact]
    public async Task UpdateMetricMonitorIsDisabled_Toggle()
    {
        var monitor = new MetricMonitor
        {
            ProjectId = _project.Id, Name = "CPU Alert",
            MetricToMonitor = "cpu.usage", Aggregator = "avg",
        };
        _db.MetricMonitors.Add(monitor);
        await _db.SaveChangesAsync();

        var disabled = await _mutation.UpdateMetricMonitorIsDisabled(
            monitor.Id, _project.Id, true, _principal, _authz, _db, CancellationToken.None);
        Assert.True(disabled.Disabled);

        var enabled = await _mutation.UpdateMetricMonitorIsDisabled(
            monitor.Id, _project.Id, false, _principal, _authz, _db, CancellationToken.None);
        Assert.False(enabled.Disabled);
    }
}
