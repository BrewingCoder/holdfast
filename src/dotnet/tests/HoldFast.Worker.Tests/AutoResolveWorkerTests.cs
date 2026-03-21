using HoldFast.Data;
using HoldFast.Domain.Entities;
using HoldFast.Domain.Enums;
using HoldFast.Worker;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HoldFast.Worker.Tests;

/// <summary>
/// Tests for AutoResolveWorker: stale error group auto-resolution.
/// </summary>
public class AutoResolveWorkerTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly HoldFastDbContext _db;
    private readonly AutoResolveWorker _worker;
    private readonly Workspace _workspace;
    private readonly Project _project;

    public AutoResolveWorkerTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<HoldFastDbContext>()
            .UseSqlite(_connection).Options;
        _db = new HoldFastDbContext(options);
        _db.Database.EnsureCreated();

        _workspace = new Workspace { Name = "WS", PlanTier = "Enterprise" };
        _db.Workspaces.Add(_workspace);
        _db.SaveChanges();

        _project = new Project { Name = "Proj", WorkspaceId = _workspace.Id };
        _db.Projects.Add(_project);
        _db.SaveChanges();

        var services = new ServiceCollection();
        services.AddSingleton(new DbContextOptionsBuilder<HoldFastDbContext>()
            .UseSqlite(_connection).Options);
        services.AddScoped(sp => new HoldFastDbContext(sp.GetRequiredService<DbContextOptions<HoldFastDbContext>>()));

        var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
        _worker = new AutoResolveWorker(scopeFactory, NullLogger<AutoResolveWorker>.Instance);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Close();
        _connection.Dispose();
    }

    private ErrorGroup CreateErrorGroup(ErrorGroupState state, DateTime? lastErrorTime = null)
    {
        var group = new ErrorGroup
        {
            ProjectId = _project.Id,
            Event = "TestError",
            Type = "BACKEND",
            State = state,
            SecureId = Guid.NewGuid().ToString("N"),
        };
        _db.ErrorGroups.Add(group);
        _db.SaveChanges();

        if (lastErrorTime.HasValue)
        {
            _db.ErrorObjects.Add(new ErrorObject
            {
                ProjectId = _project.Id,
                ErrorGroupId = group.Id,
                Event = "TestError",
                Type = "BACKEND",
                Timestamp = lastErrorTime.Value,
            });
            _db.SaveChanges();
        }

        return group;
    }

    [Fact]
    public async Task AutoResolve_NoSettings_NothingHappens()
    {
        CreateErrorGroup(ErrorGroupState.Open, DateTime.UtcNow.AddDays(-30));
        await _worker.RunAutoResolveAsync(CancellationToken.None);

        var group = await _db.ErrorGroups.FirstAsync();
        Assert.Equal(ErrorGroupState.Open, group.State);
    }

    [Fact]
    public async Task AutoResolve_StaleError_Resolved()
    {
        _db.ProjectFilterSettings.Add(new ProjectFilterSettings
        {
            ProjectId = _project.Id,
            AutoResolveStaleErrorsDayInterval = 7,
        });
        _db.SaveChanges();

        // Error 30 days old — well past the 7-day threshold
        var group = CreateErrorGroup(ErrorGroupState.Open, DateTime.UtcNow.AddDays(-30));

        await _worker.RunAutoResolveAsync(CancellationToken.None);

        await _db.Entry(group).ReloadAsync();
        Assert.Equal(ErrorGroupState.Resolved, group.State);
    }

    [Fact]
    public async Task AutoResolve_RecentError_NotResolved()
    {
        _db.ProjectFilterSettings.Add(new ProjectFilterSettings
        {
            ProjectId = _project.Id,
            AutoResolveStaleErrorsDayInterval = 7,
        });
        _db.SaveChanges();

        // Error 3 days old — within 7-day threshold
        var group = CreateErrorGroup(ErrorGroupState.Open, DateTime.UtcNow.AddDays(-3));

        await _worker.RunAutoResolveAsync(CancellationToken.None);

        await _db.Entry(group).ReloadAsync();
        Assert.Equal(ErrorGroupState.Open, group.State);
    }

    [Fact]
    public async Task AutoResolve_AlreadyResolved_NotTouched()
    {
        _db.ProjectFilterSettings.Add(new ProjectFilterSettings
        {
            ProjectId = _project.Id,
            AutoResolveStaleErrorsDayInterval = 7,
        });
        _db.SaveChanges();

        var group = CreateErrorGroup(ErrorGroupState.Resolved, DateTime.UtcNow.AddDays(-30));

        await _worker.RunAutoResolveAsync(CancellationToken.None);

        await _db.Entry(group).ReloadAsync();
        Assert.Equal(ErrorGroupState.Resolved, group.State);
    }

    [Fact]
    public async Task AutoResolve_IgnoredError_NotResolved()
    {
        _db.ProjectFilterSettings.Add(new ProjectFilterSettings
        {
            ProjectId = _project.Id,
            AutoResolveStaleErrorsDayInterval = 7,
        });
        _db.SaveChanges();

        var group = CreateErrorGroup(ErrorGroupState.Ignored, DateTime.UtcNow.AddDays(-30));

        await _worker.RunAutoResolveAsync(CancellationToken.None);

        await _db.Entry(group).ReloadAsync();
        Assert.Equal(ErrorGroupState.Ignored, group.State);
    }

    [Fact]
    public async Task AutoResolve_NoErrors_Resolved()
    {
        _db.ProjectFilterSettings.Add(new ProjectFilterSettings
        {
            ProjectId = _project.Id,
            AutoResolveStaleErrorsDayInterval = 7,
        });
        _db.SaveChanges();

        // Error group with NO error objects at all
        var group = CreateErrorGroup(ErrorGroupState.Open);

        await _worker.RunAutoResolveAsync(CancellationToken.None);

        await _db.Entry(group).ReloadAsync();
        Assert.Equal(ErrorGroupState.Resolved, group.State);
    }

    [Fact]
    public async Task AutoResolve_MultipleProjects_Independent()
    {
        var project2 = new Project { Name = "P2", WorkspaceId = _workspace.Id };
        _db.Projects.Add(project2);
        _db.SaveChanges();

        // P1: 7-day auto-resolve
        _db.ProjectFilterSettings.Add(new ProjectFilterSettings
        {
            ProjectId = _project.Id,
            AutoResolveStaleErrorsDayInterval = 7,
        });
        // P2: no auto-resolve
        _db.SaveChanges();

        var group1 = CreateErrorGroup(ErrorGroupState.Open, DateTime.UtcNow.AddDays(-30));
        var group2 = new ErrorGroup
        {
            ProjectId = project2.Id, Event = "E2", Type = "BACKEND",
            State = ErrorGroupState.Open, SecureId = "g2",
        };
        _db.ErrorGroups.Add(group2);
        _db.SaveChanges();

        await _worker.RunAutoResolveAsync(CancellationToken.None);

        await _db.Entry(group1).ReloadAsync();
        await _db.Entry(group2).ReloadAsync();
        Assert.Equal(ErrorGroupState.Resolved, group1.State);
        Assert.Equal(ErrorGroupState.Open, group2.State); // P2 has no auto-resolve
    }

    [Fact]
    public async Task AutoResolve_ZeroInterval_NotEnabled()
    {
        _db.ProjectFilterSettings.Add(new ProjectFilterSettings
        {
            ProjectId = _project.Id,
            AutoResolveStaleErrorsDayInterval = 0,
        });
        _db.SaveChanges();

        var group = CreateErrorGroup(ErrorGroupState.Open, DateTime.UtcNow.AddDays(-30));

        await _worker.RunAutoResolveAsync(CancellationToken.None);

        await _db.Entry(group).ReloadAsync();
        Assert.Equal(ErrorGroupState.Open, group.State);
    }
}
