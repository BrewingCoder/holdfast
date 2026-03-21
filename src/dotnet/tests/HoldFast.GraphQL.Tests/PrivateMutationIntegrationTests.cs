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
/// Tests for integration-related mutations added to close the cutover gap:
/// DeleteInviteLinkFromWorkspace, UpdateVercelProjectMappings, UpdateClickUpProjectMappings.
/// </summary>
public class PrivateMutationIntegrationTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly HoldFastDbContext _db;
    private readonly AuthorizationService _authz;
    private readonly PrivateMutation _mutation;
    private readonly ClaimsPrincipal _principal;
    private readonly Admin _admin;
    private readonly Workspace _workspace;
    private readonly Project _project;

    public PrivateMutationIntegrationTests()
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

    // ══════════════════════════════════════════════════════════════════
    // DeleteInviteLinkFromWorkspace
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task DeleteInviteLinkFromWorkspace_Success()
    {
        var invite = new WorkspaceInviteLink
        {
            WorkspaceId = _workspace.Id,
            InviteeEmail = "del@test.com",
            InviteeRole = "MEMBER",
            Secret = "secret-del",
            ExpirationDate = DateTime.UtcNow.AddDays(7),
        };
        _db.WorkspaceInviteLinks.Add(invite);
        await _db.SaveChangesAsync();

        var result = await _mutation.DeleteInviteLinkFromWorkspace(
            _workspace.Id, invite.Id, _principal, _authz, _db, CancellationToken.None);

        Assert.True(result);
        Assert.Null(await _db.WorkspaceInviteLinks.FindAsync(invite.Id));
    }

    [Fact]
    public async Task DeleteInviteLinkFromWorkspace_NotFound_Throws()
    {
        await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.DeleteInviteLinkFromWorkspace(
                _workspace.Id, 99999, _principal, _authz, _db, CancellationToken.None));
    }

    [Fact]
    public async Task DeleteInviteLinkFromWorkspace_WrongWorkspace_Throws()
    {
        // Create invite in a different workspace
        var otherWs = new Workspace { Name = "Other" };
        _db.Workspaces.Add(otherWs);
        await _db.SaveChangesAsync();

        var invite = new WorkspaceInviteLink
        {
            WorkspaceId = otherWs.Id,
            InviteeEmail = "other@test.com",
            InviteeRole = "MEMBER",
            Secret = "other-secret",
            ExpirationDate = DateTime.UtcNow.AddDays(7),
        };
        _db.WorkspaceInviteLinks.Add(invite);
        await _db.SaveChangesAsync();

        // Try to delete it via _workspace.Id — should not find it
        await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.DeleteInviteLinkFromWorkspace(
                _workspace.Id, invite.Id, _principal, _authz, _db, CancellationToken.None));
    }

    [Fact]
    public async Task DeleteInviteLinkFromWorkspace_ExpiredInvite_CanBeDeleted()
    {
        // Even expired links should be deletable (cleanup scenario)
        var invite = new WorkspaceInviteLink
        {
            WorkspaceId = _workspace.Id,
            InviteeEmail = "expired@test.com",
            InviteeRole = "MEMBER",
            Secret = "expired-secret",
            ExpirationDate = DateTime.UtcNow.AddDays(-1),
        };
        _db.WorkspaceInviteLinks.Add(invite);
        await _db.SaveChangesAsync();

        var result = await _mutation.DeleteInviteLinkFromWorkspace(
            _workspace.Id, invite.Id, _principal, _authz, _db, CancellationToken.None);

        Assert.True(result);
        Assert.Null(await _db.WorkspaceInviteLinks.FindAsync(invite.Id));
    }

    [Fact]
    public async Task DeleteInviteLinkFromWorkspace_MultipleLinks_OnlyDeletesTarget()
    {
        var invite1 = new WorkspaceInviteLink
        {
            WorkspaceId = _workspace.Id,
            InviteeEmail = "a@test.com",
            InviteeRole = "MEMBER",
            Secret = "secret-a",
            ExpirationDate = DateTime.UtcNow.AddDays(7),
        };
        var invite2 = new WorkspaceInviteLink
        {
            WorkspaceId = _workspace.Id,
            InviteeEmail = "b@test.com",
            InviteeRole = "MEMBER",
            Secret = "secret-b",
            ExpirationDate = DateTime.UtcNow.AddDays(7),
        };
        _db.WorkspaceInviteLinks.AddRange(invite1, invite2);
        await _db.SaveChangesAsync();

        await _mutation.DeleteInviteLinkFromWorkspace(
            _workspace.Id, invite1.Id, _principal, _authz, _db, CancellationToken.None);

        Assert.Null(await _db.WorkspaceInviteLinks.FindAsync(invite1.Id));
        Assert.NotNull(await _db.WorkspaceInviteLinks.FindAsync(invite2.Id));
    }

    // ══════════════════════════════════════════════════════════════════
    // UpdateVercelProjectMappings
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task UpdateVercelProjectMappings_AddsMappings()
    {
        var mappings = new List<VercelProjectMappingInput>
        {
            new("vercel-proj-1", null, _project.Id),
        };

        var result = await _mutation.UpdateVercelProjectMappings(
            _project.Id, mappings, _principal, _authz, _db, CancellationToken.None);

        Assert.True(result);

        var configs = await _db.VercelIntegrationConfigs
            .Where(v => v.WorkspaceId == _workspace.Id)
            .ToListAsync();
        Assert.Single(configs);
        Assert.Equal("vercel-proj-1", configs[0].VercelProjectId);
        Assert.Equal(_project.Id, configs[0].ProjectId);
    }

    [Fact]
    public async Task UpdateVercelProjectMappings_ReplacesExistingMappings()
    {
        // Seed an existing mapping
        _db.VercelIntegrationConfigs.Add(new VercelIntegrationConfig
        {
            WorkspaceId = _workspace.Id,
            ProjectId = _project.Id,
            VercelProjectId = "old-vercel-id",
        });
        await _db.SaveChangesAsync();

        var mappings = new List<VercelProjectMappingInput>
        {
            new("new-vercel-id", null, _project.Id),
        };

        await _mutation.UpdateVercelProjectMappings(
            _project.Id, mappings, _principal, _authz, _db, CancellationToken.None);

        var configs = await _db.VercelIntegrationConfigs
            .Where(v => v.WorkspaceId == _workspace.Id)
            .ToListAsync();
        Assert.Single(configs);
        Assert.Equal("new-vercel-id", configs[0].VercelProjectId);
    }

    [Fact]
    public async Task UpdateVercelProjectMappings_EmptyList_ClearsAll()
    {
        _db.VercelIntegrationConfigs.Add(new VercelIntegrationConfig
        {
            WorkspaceId = _workspace.Id,
            ProjectId = _project.Id,
            VercelProjectId = "to-be-removed",
        });
        await _db.SaveChangesAsync();

        var result = await _mutation.UpdateVercelProjectMappings(
            _project.Id, [], _principal, _authz, _db, CancellationToken.None);

        Assert.True(result);
        var remaining = await _db.VercelIntegrationConfigs
            .Where(v => v.WorkspaceId == _workspace.Id)
            .ToListAsync();
        Assert.Empty(remaining);
    }

    [Fact]
    public async Task UpdateVercelProjectMappings_MultipleMappings()
    {
        var proj2 = new Project { Name = "Proj2", WorkspaceId = _workspace.Id };
        _db.Projects.Add(proj2);
        await _db.SaveChangesAsync(); // ensure proj2.Id is populated

        var mappings = new List<VercelProjectMappingInput>
        {
            new("vercel-a", null, _project.Id),
            new("vercel-b", null, proj2.Id),
        };

        await _mutation.UpdateVercelProjectMappings(
            _project.Id, mappings, _principal, _authz, _db, CancellationToken.None);

        var configs = await _db.VercelIntegrationConfigs
            .Where(v => v.WorkspaceId == _workspace.Id)
            .ToListAsync();
        Assert.Equal(2, configs.Count);
        Assert.Contains(configs, c => c.VercelProjectId == "vercel-a");
        Assert.Contains(configs, c => c.VercelProjectId == "vercel-b");
    }

    [Fact]
    public async Task UpdateVercelProjectMappings_SkipsNewProjectNameEntries()
    {
        // Entries with NewProjectName but no ProjectId require Vercel API
        // — self-hosted path skips them rather than failing
        var mappings = new List<VercelProjectMappingInput>
        {
            new("vercel-1", "Brand New Project", null),
            new("vercel-2", null, _project.Id),
        };

        var result = await _mutation.UpdateVercelProjectMappings(
            _project.Id, mappings, _principal, _authz, _db, CancellationToken.None);

        Assert.True(result);

        var configs = await _db.VercelIntegrationConfigs
            .Where(v => v.WorkspaceId == _workspace.Id)
            .ToListAsync();
        // Only the one with a real project ID should be saved
        Assert.Single(configs);
        Assert.Equal("vercel-2", configs[0].VercelProjectId);
    }

    [Fact]
    public async Task UpdateVercelProjectMappings_ProjectNotFound_Throws()
    {
        await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.UpdateVercelProjectMappings(
                99999, [], _principal, _authz, _db, CancellationToken.None));
    }

    // ══════════════════════════════════════════════════════════════════
    // UpdateClickUpProjectMappings
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task UpdateClickUpProjectMappings_AddsMappings()
    {
        var mappings = new List<ClickUpProjectMappingInput>
        {
            new(_project.Id, "clickup-space-1"),
        };

        var result = await _mutation.UpdateClickUpProjectMappings(
            _workspace.Id, mappings, _principal, _authz, _db, CancellationToken.None);

        Assert.True(result);

        var saved = await _db.IntegrationProjectMappings
            .Where(m => m.IntegrationType == "ClickUp" && m.ProjectId == _project.Id)
            .ToListAsync();
        Assert.Single(saved);
        Assert.Equal("clickup-space-1", saved[0].ExternalId);
    }

    [Fact]
    public async Task UpdateClickUpProjectMappings_ReplacesExistingMappings()
    {
        _db.IntegrationProjectMappings.Add(new IntegrationProjectMapping
        {
            IntegrationType = "ClickUp",
            ProjectId = _project.Id,
            ExternalId = "old-space",
        });
        await _db.SaveChangesAsync();

        var mappings = new List<ClickUpProjectMappingInput>
        {
            new(_project.Id, "new-space"),
        };

        await _mutation.UpdateClickUpProjectMappings(
            _workspace.Id, mappings, _principal, _authz, _db, CancellationToken.None);

        var saved = await _db.IntegrationProjectMappings
            .Where(m => m.IntegrationType == "ClickUp")
            .ToListAsync();
        Assert.Single(saved);
        Assert.Equal("new-space", saved[0].ExternalId);
    }

    [Fact]
    public async Task UpdateClickUpProjectMappings_EmptyList_ClearsAll()
    {
        _db.IntegrationProjectMappings.Add(new IntegrationProjectMapping
        {
            IntegrationType = "ClickUp",
            ProjectId = _project.Id,
            ExternalId = "to-remove",
        });
        await _db.SaveChangesAsync();

        var result = await _mutation.UpdateClickUpProjectMappings(
            _workspace.Id, [], _principal, _authz, _db, CancellationToken.None);

        Assert.True(result);
        var remaining = await _db.IntegrationProjectMappings
            .Where(m => m.IntegrationType == "ClickUp")
            .ToListAsync();
        Assert.Empty(remaining);
    }

    [Fact]
    public async Task UpdateClickUpProjectMappings_DoesNotAffectOtherIntegrationTypes()
    {
        // Seed a Linear mapping — should survive a ClickUp update
        _db.IntegrationProjectMappings.Add(new IntegrationProjectMapping
        {
            IntegrationType = "Linear",
            ProjectId = _project.Id,
            ExternalId = "linear-id",
        });
        await _db.SaveChangesAsync();

        await _mutation.UpdateClickUpProjectMappings(
            _workspace.Id, [], _principal, _authz, _db, CancellationToken.None);

        var linear = await _db.IntegrationProjectMappings
            .Where(m => m.IntegrationType == "Linear")
            .ToListAsync();
        Assert.Single(linear);
    }

    [Fact]
    public async Task UpdateClickUpProjectMappings_MultipleMappings()
    {
        var proj2 = new Project { Name = "Proj2", WorkspaceId = _workspace.Id };
        _db.Projects.Add(proj2);
        await _db.SaveChangesAsync();

        var mappings = new List<ClickUpProjectMappingInput>
        {
            new(_project.Id, "space-a"),
            new(proj2.Id, "space-b"),
        };

        await _mutation.UpdateClickUpProjectMappings(
            _workspace.Id, mappings, _principal, _authz, _db, CancellationToken.None);

        var saved = await _db.IntegrationProjectMappings
            .Where(m => m.IntegrationType == "ClickUp")
            .ToListAsync();
        Assert.Equal(2, saved.Count);
        Assert.Contains(saved, m => m.ExternalId == "space-a");
        Assert.Contains(saved, m => m.ExternalId == "space-b");
    }

    [Fact]
    public async Task UpdateClickUpProjectMappings_DoesNotAffectOtherWorkspaceProjects()
    {
        // Seed a ClickUp mapping for a project in a different workspace
        var otherWs = new Workspace { Name = "Other" };
        _db.Workspaces.Add(otherWs);
        await _db.SaveChangesAsync();

        var otherProj = new Project { Name = "OtherProj", WorkspaceId = otherWs.Id };
        _db.Projects.Add(otherProj);
        await _db.SaveChangesAsync();

        _db.IntegrationProjectMappings.Add(new IntegrationProjectMapping
        {
            IntegrationType = "ClickUp",
            ProjectId = otherProj.Id,
            ExternalId = "other-space",
        });
        await _db.SaveChangesAsync();

        // Clear ClickUp for our workspace — should not touch the other workspace's mapping
        await _mutation.UpdateClickUpProjectMappings(
            _workspace.Id, [], _principal, _authz, _db, CancellationToken.None);

        var other = await _db.IntegrationProjectMappings
            .Where(m => m.IntegrationType == "ClickUp" && m.ProjectId == otherProj.Id)
            .ToListAsync();
        Assert.Single(other);
    }
}
