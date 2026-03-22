using HoldFast.Data;
using HoldFast.Domain.Entities;
using HoldFast.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace HoldFast.Api.Bootstrap;

/// <summary>
/// Runs at every startup — unconditionally, no feature flag.
/// Ensures the "HoldFast" system workspace and project exist so the platform can
/// send its own telemetry to itself (eating our own dogfood).
///
/// The created project ID is stored in <see cref="SystemProjectState"/> so that
/// the OTeL exporter can attach the correct x-highlight-project header.
/// </summary>
public class SystemBootstrapService : IHostedService
{
    public const string SystemWorkspaceName = "HoldFast";
    public const string SystemProjectName = "HoldFast";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly SystemProjectState _projectState;
    private readonly ILogger<SystemBootstrapService> _logger;

    public SystemBootstrapService(
        IServiceScopeFactory scopeFactory,
        SystemProjectState projectState,
        ILogger<SystemBootstrapService> logger)
    {
        _scopeFactory = scopeFactory;
        _projectState = projectState;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HoldFastDbContext>();

        var workspace = await EnsureWorkspaceAsync(db, cancellationToken);
        var project = await EnsureProjectAsync(db, workspace, cancellationToken);

        _projectState.ProjectId = project.Id;
        _projectState.ProjectSecret = project.Secret ?? string.Empty;

        _logger.LogInformation(
            "HoldFast system project ready — workspaceId={WorkspaceId} projectId={ProjectId}",
            workspace.Id, project.Id);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static async Task<Workspace> EnsureWorkspaceAsync(HoldFastDbContext db, CancellationToken ct)
    {
        var workspace = await db.Workspaces
            .FirstOrDefaultAsync(w => w.Name == SystemWorkspaceName, ct);

        if (workspace != null) return workspace;

        workspace = new Workspace
        {
            Name = SystemWorkspaceName,
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
        return workspace;
    }

    private static async Task<Project> EnsureProjectAsync(
        HoldFastDbContext db, Workspace workspace, CancellationToken ct)
    {
        var project = await db.Projects
            .FirstOrDefaultAsync(p => p.WorkspaceId == workspace.Id && p.Name == SystemProjectName, ct);

        if (project != null) return project;

        project = new Project
        {
            Name = SystemProjectName,
            WorkspaceId = workspace.Id,
            Secret = Guid.NewGuid().ToString("N"),
            BackendSetup = true,
        };
        db.Projects.Add(project);
        await db.SaveChangesAsync(ct);
        return project;
    }
}

/// <summary>
/// Singleton that holds the system project ID after bootstrap completes.
/// Captured by the OTeL exporter configuration so headers are set correctly.
/// </summary>
public class SystemProjectState
{
    public int ProjectId { get; set; }
    public string ProjectSecret { get; set; } = string.Empty;
}
