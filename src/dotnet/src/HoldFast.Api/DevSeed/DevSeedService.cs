using HoldFast.Data;
using HoldFast.Domain;
using HoldFast.Domain.Entities;
using HoldFast.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HoldFast.Api.DevSeed;

/// <summary>
/// Runs once at startup when DevSeed:Enabled=true.
/// Creates a dev admin account and the configured workspaces (each with a default project),
/// idempotently — safe to run on every restart.
/// </summary>
public class DevSeedService : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly DevSeedOptions _options;
    private readonly ILogger<DevSeedService> _logger;

    public DevSeedService(
        IServiceScopeFactory scopeFactory,
        IOptions<DevSeedOptions> options,
        ILogger<DevSeedService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_options.Enabled) return;

        _logger.LogInformation("DevSeed: seeding dev instance...");

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HoldFastDbContext>();

        var admin = await EnsureAdminAsync(db, cancellationToken);

        foreach (var workspaceName in _options.Workspaces)
        {
            var workspace = await EnsureWorkspaceAsync(db, workspaceName, cancellationToken);
            await EnsureWorkspaceMemberAsync(db, admin, workspace, cancellationToken);
            await EnsureProjectAsync(db, workspace, cancellationToken);
        }

        _logger.LogInformation(
            "DevSeed: complete — admin={Email}, workspaces={Count}",
            _options.AdminEmail, _options.Workspaces.Count);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task<Admin> EnsureAdminAsync(HoldFastDbContext db, CancellationToken ct)
    {
        var admin = await db.Admins
            .FirstOrDefaultAsync(a => a.Email == _options.AdminEmail, ct);

        if (admin != null) return admin;

        admin = new Admin
        {
            Email = _options.AdminEmail,
            Name = _options.AdminName,
            Uid = _options.AdminEmail,
            EmailVerified = true,
        };
        db.Admins.Add(admin);
        await db.SaveChangesAsync(ct);
        _logger.LogInformation("DevSeed: created admin {Email}", admin.Email);
        return admin;
    }

    private async Task<Workspace> EnsureWorkspaceAsync(
        HoldFastDbContext db, string name, CancellationToken ct)
    {
        var workspace = await db.Workspaces
            .FirstOrDefaultAsync(w => w.Name == name, ct);

        if (workspace != null) return workspace;

        workspace = new Workspace
        {
            Name = name,
            Secret = Guid.NewGuid().ToString("N"),
            PlanTier = "Enterprise",
            UnlimitedMembers = true,
            RetentionPeriod = RetentionPeriod.SixMonths,
            ErrorsRetentionPeriod = RetentionPeriod.SixMonths,
            LogsRetentionPeriod = RetentionPeriod.ThirtyDays,
            TracesRetentionPeriod = RetentionPeriod.ThirtyDays,
            MetricsRetentionPeriod = RetentionPeriod.ThirtyDays,
        };
        db.Workspaces.Add(workspace);
        await db.SaveChangesAsync(ct);
        _logger.LogInformation("DevSeed: created workspace '{Name}'", name);
        return workspace;
    }

    private async Task EnsureWorkspaceMemberAsync(
        HoldFastDbContext db, Admin admin, Workspace workspace, CancellationToken ct)
    {
        var exists = await db.Set<WorkspaceAdmin>()
            .AnyAsync(wa => wa.AdminId == admin.Id && wa.WorkspaceId == workspace.Id, ct);

        if (exists) return;

        db.Set<WorkspaceAdmin>().Add(new WorkspaceAdmin
        {
            AdminId = admin.Id,
            WorkspaceId = workspace.Id,
            Role = WorkspaceRoles.Admin,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync(ct);
    }

    private async Task EnsureProjectAsync(
        HoldFastDbContext db, Workspace workspace, CancellationToken ct)
    {
        var exists = await db.Projects
            .AnyAsync(p => p.WorkspaceId == workspace.Id && p.Name == "default", ct);

        if (exists) return;

        var project = new Project
        {
            Name = "default",
            WorkspaceId = workspace.Id,
            Secret = Guid.NewGuid().ToString("N"),
            BackendSetup = true,
        };
        db.Projects.Add(project);
        await db.SaveChangesAsync(ct);
        _logger.LogInformation(
            "DevSeed: created project 'default' in workspace '{Name}'", workspace.Name);
    }
}
