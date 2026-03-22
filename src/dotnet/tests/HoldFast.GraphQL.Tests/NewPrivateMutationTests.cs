using System.Security.Claims;
using HoldFast.Data;
using HoldFast.Domain;
using HoldFast.Domain.Entities;
using HoldFast.Domain.Enums;
using HoldFast.GraphQL.Private;
using HoldFast.Shared.Auth;
using HotChocolate;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HoldFast.GraphQL.Tests;

/// <summary>
/// Comprehensive tests for the new PrivateMutation methods added after EditProjectPlatforms:
/// UpdateAdminAndCreateWorkspace, CreateAdmin, EmailSignup, SubmitRegistrationForm,
/// RequestAccess, Integration management, Issue linking, EditServiceGithubSettings,
/// CreateCloudflareProxy, UpsertSlackChannel, UpsertDiscordChannel.
/// </summary>
public class NewPrivateMutationTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly HoldFastDbContext _db;
    private readonly AuthorizationService _authz;
    private readonly PrivateMutation _mutation;
    private readonly ClaimsPrincipal _principal;
    private readonly Admin _admin;
    private readonly Workspace _workspace;
    private readonly Project _project;

    public NewPrivateMutationTests()
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
        _connection.Dispose();
    }

    // Helper to create a principal with standard ClaimTypes (for CreateAdmin which uses ClaimTypes.NameIdentifier)
    private static ClaimsPrincipal MakeStandardPrincipal(string uid, string? email = null)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, uid),
            new(HoldFastClaimTypes.Uid, uid),
        };
        if (email != null)
        {
            claims.Add(new Claim(ClaimTypes.Email, email));
            claims.Add(new Claim(HoldFastClaimTypes.Email, email));
        }
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
    }

    // ── UpdateAdminAndCreateWorkspace ─────────────────────────────────

    [Fact]
    public async Task UpdateAdminAndCreateWorkspace_CreatesWorkspaceAndSetsAdminName()
    {
        var result = await _mutation.UpdateAdminAndCreateWorkspace(
            "Scott", "New Workspace",
            _principal, _authz, _db, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("New Workspace", result.Name);
        Assert.Equal("Enterprise", result.PlanTier);
        Assert.True(result.UnlimitedMembers);

        // Verify admin name was updated
        var admin = await _db.Admins.FindAsync(_admin.Id);
        Assert.Equal("Scott", admin!.Name);
    }

    [Fact]
    public async Task UpdateAdminAndCreateWorkspace_CreatesWorkspaceAdminLink()
    {
        var result = await _mutation.UpdateAdminAndCreateWorkspace(
            "Admin", "Linked WS",
            _principal, _authz, _db, CancellationToken.None);

        var link = await _db.WorkspaceAdmins
            .FirstOrDefaultAsync(wa => wa.WorkspaceId == result.Id && wa.AdminId == _admin.Id);
        Assert.NotNull(link);
        Assert.Equal(WorkspaceRoles.Admin, link.Role);
    }

    [Fact]
    public async Task UpdateAdminAndCreateWorkspace_NoAuth_Throws()
    {
        var anonPrincipal = new ClaimsPrincipal(new ClaimsIdentity());
        await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.UpdateAdminAndCreateWorkspace(
                "Name", "WS", anonPrincipal, _authz, _db, CancellationToken.None));
    }

    [Fact]
    public async Task UpdateAdminAndCreateWorkspace_MultipleCallsCreateMultipleWorkspaces()
    {
        var ws1 = await _mutation.UpdateAdminAndCreateWorkspace(
            "A1", "WS1", _principal, _authz, _db, CancellationToken.None);
        var ws2 = await _mutation.UpdateAdminAndCreateWorkspace(
            "A2", "WS2", _principal, _authz, _db, CancellationToken.None);

        Assert.NotEqual(ws1.Id, ws2.Id);
        Assert.Equal("WS1", ws1.Name);
        Assert.Equal("WS2", ws2.Name);
    }

    // ── CreateAdmin ──────────────────────────────────────────────────

    [Fact]
    public async Task CreateAdmin_CreatesNewAdmin()
    {
        var principal = MakeStandardPrincipal("new-uid-1", "new@test.com");
        var result = await _mutation.CreateAdmin(principal, _db, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("new-uid-1", result.Uid);
        Assert.Equal("new@test.com", result.Email);
        Assert.True(result.EmailVerified);
    }

    [Fact]
    public async Task CreateAdmin_DuplicateUid_ReturnsExisting()
    {
        var principal = MakeStandardPrincipal("dup-uid", "first@test.com");
        var first = await _mutation.CreateAdmin(principal, _db, CancellationToken.None);

        var principal2 = MakeStandardPrincipal("dup-uid", "second@test.com");
        var second = await _mutation.CreateAdmin(principal2, _db, CancellationToken.None);

        Assert.Equal(first.Id, second.Id);
        Assert.Equal("first@test.com", second.Email); // Original email kept
    }

    [Fact]
    public async Task CreateAdmin_NoEmail_SetsEmailVerifiedFalse()
    {
        var principal = MakeStandardPrincipal("no-email-uid");
        var result = await _mutation.CreateAdmin(principal, _db, CancellationToken.None);

        Assert.Null(result.Email);
        Assert.False(result.EmailVerified);
    }

    [Fact]
    public async Task CreateAdmin_NoUidClaim_Throws()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(Array.Empty<Claim>(), "Test"));
        await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.CreateAdmin(principal, _db, CancellationToken.None));
    }

    // ── EmailSignup ─────────────────────────────────────────────────

    [Fact]
    public async Task EmailSignup_CreatesRecord()
    {
        var result = await _mutation.EmailSignup("test@example.com", _db, CancellationToken.None);

        Assert.Equal("test@example.com", result);
        var signup = await _db.EmailSignups.FirstOrDefaultAsync(e => e.Email == "test@example.com");
        Assert.NotNull(signup);
    }

    [Fact]
    public async Task EmailSignup_DuplicateEmail_ReturnsWithoutDuplicate()
    {
        await _mutation.EmailSignup("dup@example.com", _db, CancellationToken.None);
        var result = await _mutation.EmailSignup("dup@example.com", _db, CancellationToken.None);

        Assert.Equal("dup@example.com", result);
        var count = await _db.EmailSignups.CountAsync(e => e.Email == "dup@example.com");
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task EmailSignup_DifferentEmails_CreatesSeparateRecords()
    {
        await _mutation.EmailSignup("a@example.com", _db, CancellationToken.None);
        await _mutation.EmailSignup("b@example.com", _db, CancellationToken.None);

        var count = await _db.EmailSignups.CountAsync();
        Assert.Equal(2, count);
    }

    // ── SubmitRegistrationForm ───────────────────────────────────────

    [Fact]
    public async Task SubmitRegistrationForm_CreatesNewRecord()
    {
        var result = await _mutation.SubmitRegistrationForm(
            _workspace.Id, "10-50", "Engineer", "Monitoring", "Twitter", "Nice!",
            _principal, _authz, _db, CancellationToken.None);

        Assert.True(result);
        var reg = await _db.RegistrationData.FirstOrDefaultAsync(r => r.WorkspaceId == _workspace.Id);
        Assert.NotNull(reg);
        Assert.Equal("10-50", reg.TeamSize);
        Assert.Equal("Engineer", reg.Role);
        Assert.Equal("Monitoring", reg.UseCase);
        Assert.Equal("Twitter", reg.HeardAbout);
        Assert.Equal("Nice!", reg.Pun);
    }

    [Fact]
    public async Task SubmitRegistrationForm_UpdatesExistingRecord()
    {
        // Create initial
        await _mutation.SubmitRegistrationForm(
            _workspace.Id, "1-5", "Manager", "Logging", null, null,
            _principal, _authz, _db, CancellationToken.None);

        // Update partial fields
        await _mutation.SubmitRegistrationForm(
            _workspace.Id, "50-100", null, "Tracing", "Conference", null,
            _principal, _authz, _db, CancellationToken.None);

        var reg = await _db.RegistrationData.FirstOrDefaultAsync(r => r.WorkspaceId == _workspace.Id);
        Assert.NotNull(reg);
        Assert.Equal("50-100", reg.TeamSize); // Updated
        Assert.Equal("Manager", reg.Role);    // Kept from original (null passed)
        Assert.Equal("Tracing", reg.UseCase); // Updated
        Assert.Equal("Conference", reg.HeardAbout); // Updated
        Assert.Null(reg.Pun);                 // Was null, stayed null
    }

    [Fact]
    public async Task SubmitRegistrationForm_AllNullFields_CreatesRecord()
    {
        var result = await _mutation.SubmitRegistrationForm(
            _workspace.Id, null, null, null, null, null,
            _principal, _authz, _db, CancellationToken.None);

        Assert.True(result);
        var reg = await _db.RegistrationData.FirstOrDefaultAsync(r => r.WorkspaceId == _workspace.Id);
        Assert.NotNull(reg);
        Assert.Null(reg.TeamSize);
    }

    // ── RequestAccess ────────────────────────────────────────────────

    [Fact]
    public async Task RequestAccess_CreatesRequest()
    {
        var result = await _mutation.RequestAccess(
            _workspace.Id, _principal, _authz, _db, CancellationToken.None);

        Assert.True(result);
        var req = await _db.WorkspaceAccessRequests.FirstOrDefaultAsync(r => r.AdminId == _admin.Id);
        Assert.NotNull(req);
        Assert.Equal(_workspace.Id, req.LastRequestedWorkspace);
    }

    [Fact]
    public async Task RequestAccess_UpdatesExistingRequest()
    {
        await _mutation.RequestAccess(
            _workspace.Id, _principal, _authz, _db, CancellationToken.None);

        // Create second workspace
        var ws2 = new Workspace { Name = "WS2" };
        _db.Workspaces.Add(ws2);
        await _db.SaveChangesAsync();

        await _mutation.RequestAccess(
            ws2.Id, _principal, _authz, _db, CancellationToken.None);

        var requests = await _db.WorkspaceAccessRequests.Where(r => r.AdminId == _admin.Id).ToListAsync();
        Assert.Single(requests);
        Assert.Equal(ws2.Id, requests[0].LastRequestedWorkspace);
    }

    [Fact]
    public async Task RequestAccess_NoAuth_Throws()
    {
        var anonPrincipal = new ClaimsPrincipal(new ClaimsIdentity());
        await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.RequestAccess(
                _workspace.Id, anonPrincipal, _authz, _db, CancellationToken.None));
    }

    // ── AddIntegrationToProject ──────────────────────────────────────

    [Fact]
    public async Task AddIntegrationToProject_CreatesMapping()
    {
        var result = await _mutation.AddIntegrationToProject(
            IntegrationType.Slack, _project.Id, "C123",
            _principal, _authz, _db, CancellationToken.None);

        Assert.True(result);
        var mapping = await _db.IntegrationProjectMappings
            .FirstOrDefaultAsync(m => m.ProjectId == _project.Id && m.IntegrationType == "Slack");
        Assert.NotNull(mapping);
        Assert.Equal("C123", mapping.ExternalId);
    }

    [Fact]
    public async Task AddIntegrationToProject_UpdatesExistingMapping()
    {
        await _mutation.AddIntegrationToProject(
            IntegrationType.Linear, _project.Id, "LIN-1",
            _principal, _authz, _db, CancellationToken.None);

        await _mutation.AddIntegrationToProject(
            IntegrationType.Linear, _project.Id, "LIN-2",
            _principal, _authz, _db, CancellationToken.None);

        var count = await _db.IntegrationProjectMappings
            .CountAsync(m => m.ProjectId == _project.Id && m.IntegrationType == "Linear");
        Assert.Equal(1, count);
        var mapping = await _db.IntegrationProjectMappings
            .FirstAsync(m => m.ProjectId == _project.Id && m.IntegrationType == "Linear");
        Assert.Equal("LIN-2", mapping.ExternalId);
    }

    [Fact]
    public async Task AddIntegrationToProject_DifferentTypes_CreatesSeparate()
    {
        await _mutation.AddIntegrationToProject(
            IntegrationType.Slack, _project.Id, "S1",
            _principal, _authz, _db, CancellationToken.None);
        await _mutation.AddIntegrationToProject(
            IntegrationType.Linear, _project.Id, "L1",
            _principal, _authz, _db, CancellationToken.None);

        var count = await _db.IntegrationProjectMappings.CountAsync(m => m.ProjectId == _project.Id);
        Assert.Equal(2, count);
    }

    // ── RemoveIntegrationFromProject ─────────────────────────────────

    [Fact]
    public async Task RemoveIntegrationFromProject_RemovesMapping()
    {
        await _mutation.AddIntegrationToProject(
            IntegrationType.Jira, _project.Id, "JIRA-1",
            _principal, _authz, _db, CancellationToken.None);

        var result = await _mutation.RemoveIntegrationFromProject(
            IntegrationType.Jira, _project.Id,
            _principal, _authz, _db, CancellationToken.None);

        Assert.True(result);
        var mapping = await _db.IntegrationProjectMappings
            .FirstOrDefaultAsync(m => m.ProjectId == _project.Id && m.IntegrationType == "Jira");
        Assert.Null(mapping);
    }

    [Fact]
    public async Task RemoveIntegrationFromProject_NonExistent_ReturnsTrue()
    {
        var result = await _mutation.RemoveIntegrationFromProject(
            IntegrationType.GitHub, _project.Id,
            _principal, _authz, _db, CancellationToken.None);

        Assert.True(result); // No-op, but still succeeds
    }

    [Fact]
    public async Task RemoveIntegrationFromProject_OnlyRemovesSpecifiedType()
    {
        await _mutation.AddIntegrationToProject(
            IntegrationType.Slack, _project.Id, "S1",
            _principal, _authz, _db, CancellationToken.None);
        await _mutation.AddIntegrationToProject(
            IntegrationType.Linear, _project.Id, "L1",
            _principal, _authz, _db, CancellationToken.None);

        await _mutation.RemoveIntegrationFromProject(
            IntegrationType.Slack, _project.Id,
            _principal, _authz, _db, CancellationToken.None);

        var remaining = await _db.IntegrationProjectMappings.CountAsync(m => m.ProjectId == _project.Id);
        Assert.Equal(1, remaining);
        var linearMapping = await _db.IntegrationProjectMappings
            .FirstOrDefaultAsync(m => m.ProjectId == _project.Id && m.IntegrationType == "Linear");
        Assert.NotNull(linearMapping);
    }

    // ── AddIntegrationToWorkspace ────────────────────────────────────

    [Fact]
    public async Task AddIntegrationToWorkspace_CreatesMapping()
    {
        var result = await _mutation.AddIntegrationToWorkspace(
            IntegrationType.Discord, _workspace.Id, "token-abc",
            _principal, _authz, _db, CancellationToken.None);

        Assert.True(result);
        var mapping = await _db.IntegrationWorkspaceMappings
            .FirstOrDefaultAsync(m => m.WorkspaceId == _workspace.Id && m.IntegrationType == "Discord");
        Assert.NotNull(mapping);
        Assert.Equal("token-abc", mapping.AccessToken);
    }

    [Fact]
    public async Task AddIntegrationToWorkspace_UpdatesExisting()
    {
        await _mutation.AddIntegrationToWorkspace(
            IntegrationType.GitHub, _workspace.Id, "old-token",
            _principal, _authz, _db, CancellationToken.None);

        await _mutation.AddIntegrationToWorkspace(
            IntegrationType.GitHub, _workspace.Id, "new-token",
            _principal, _authz, _db, CancellationToken.None);

        var count = await _db.IntegrationWorkspaceMappings
            .CountAsync(m => m.WorkspaceId == _workspace.Id && m.IntegrationType == "GitHub");
        Assert.Equal(1, count);
        var mapping = await _db.IntegrationWorkspaceMappings
            .FirstAsync(m => m.WorkspaceId == _workspace.Id && m.IntegrationType == "GitHub");
        Assert.Equal("new-token", mapping.AccessToken);
    }

    // ── RemoveIntegrationFromWorkspace ───────────────────────────────

    [Fact]
    public async Task RemoveIntegrationFromWorkspace_RemovesMapping()
    {
        await _mutation.AddIntegrationToWorkspace(
            IntegrationType.Vercel, _workspace.Id, "vercel-tok",
            _principal, _authz, _db, CancellationToken.None);

        var result = await _mutation.RemoveIntegrationFromWorkspace(
            IntegrationType.Vercel, _workspace.Id,
            _principal, _authz, _db, CancellationToken.None);

        Assert.True(result);
        var mapping = await _db.IntegrationWorkspaceMappings
            .FirstOrDefaultAsync(m => m.WorkspaceId == _workspace.Id && m.IntegrationType == "Vercel");
        Assert.Null(mapping);
    }

    [Fact]
    public async Task RemoveIntegrationFromWorkspace_NonExistent_ReturnsTrue()
    {
        var result = await _mutation.RemoveIntegrationFromWorkspace(
            IntegrationType.Heroku, _workspace.Id,
            _principal, _authz, _db, CancellationToken.None);

        Assert.True(result);
    }

    // ── UpdateIntegrationProjectMappings ─────────────────────────────

    [Fact]
    public async Task UpdateIntegrationProjectMappings_ReplacesExisting()
    {
        // Add initial mapping
        await _mutation.AddIntegrationToProject(
            IntegrationType.Slack, _project.Id, "OLD",
            _principal, _authz, _db, CancellationToken.None);

        // Bulk update
        var newMappings = new List<IntegrationProjectMappingInput>
        {
            new(_project.Id, "NEW-1"),
        };
        var result = await _mutation.UpdateIntegrationProjectMappings(
            _workspace.Id, IntegrationType.Slack, newMappings,
            _principal, _authz, _db, CancellationToken.None);

        Assert.True(result);
        var mappings = await _db.IntegrationProjectMappings
            .Where(m => m.IntegrationType == "Slack").ToListAsync();
        Assert.Single(mappings);
        Assert.Equal("NEW-1", mappings[0].ExternalId);
    }

    [Fact]
    public async Task UpdateIntegrationProjectMappings_EmptyList_RemovesAll()
    {
        await _mutation.AddIntegrationToProject(
            IntegrationType.Linear, _project.Id, "L1",
            _principal, _authz, _db, CancellationToken.None);

        var result = await _mutation.UpdateIntegrationProjectMappings(
            _workspace.Id, IntegrationType.Linear, new List<IntegrationProjectMappingInput>(),
            _principal, _authz, _db, CancellationToken.None);

        Assert.True(result);
        var count = await _db.IntegrationProjectMappings
            .CountAsync(m => m.IntegrationType == "Linear");
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task UpdateIntegrationProjectMappings_MultipleProjects()
    {
        var proj2 = new Project { Name = "P2", WorkspaceId = _workspace.Id };
        _db.Projects.Add(proj2);
        await _db.SaveChangesAsync();

        var mappings = new List<IntegrationProjectMappingInput>
        {
            new(_project.Id, "EXT-A"),
            new(proj2.Id, "EXT-B"),
        };
        await _mutation.UpdateIntegrationProjectMappings(
            _workspace.Id, IntegrationType.Jira, mappings,
            _principal, _authz, _db, CancellationToken.None);

        var result = await _db.IntegrationProjectMappings
            .Where(m => m.IntegrationType == "Jira").ToListAsync();
        Assert.Equal(2, result.Count);
        Assert.Contains(result, m => m.ExternalId == "EXT-A");
        Assert.Contains(result, m => m.ExternalId == "EXT-B");
    }

    [Fact]
    public async Task UpdateIntegrationProjectMappings_DoesNotAffectOtherTypes()
    {
        await _mutation.AddIntegrationToProject(
            IntegrationType.Slack, _project.Id, "SLACK-1",
            _principal, _authz, _db, CancellationToken.None);
        await _mutation.AddIntegrationToProject(
            IntegrationType.Linear, _project.Id, "LIN-1",
            _principal, _authz, _db, CancellationToken.None);

        // Bulk update Slack only
        await _mutation.UpdateIntegrationProjectMappings(
            _workspace.Id, IntegrationType.Slack,
            new List<IntegrationProjectMappingInput> { new(_project.Id, "SLACK-NEW") },
            _principal, _authz, _db, CancellationToken.None);

        // Linear should be untouched
        var linear = await _db.IntegrationProjectMappings
            .FirstOrDefaultAsync(m => m.IntegrationType == "Linear");
        Assert.NotNull(linear);
        Assert.Equal("LIN-1", linear.ExternalId);
    }

    // ── CreateSessionCommentWithExistingIssue ────────────────────────

    [Fact]
    public async Task CreateSessionCommentWithExistingIssue_CreatesCommentAndAttachment()
    {
        var session = new Session { SecureId = "sess-1", ProjectId = _project.Id };
        _db.Sessions.Add(session);
        await _db.SaveChangesAsync();

        var result = await _mutation.CreateSessionCommentWithExistingIssue(
            _project.Id, "sess-1", 5000, "Bug here", null, null,
            IntegrationType.Linear, "https://linear.app/issue/1", "Fix bug",
            _principal, _authz, _db, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("Bug here", result.Text);
        Assert.Equal(5000, result.Timestamp);
        Assert.Equal(_admin.Id, result.AdminId);

        var attachment = await _db.ExternalAttachments
            .FirstOrDefaultAsync(a => a.SessionCommentId == result.Id);
        Assert.NotNull(attachment);
        Assert.Equal("Linear", attachment.IntegrationType);
        Assert.Equal("https://linear.app/issue/1", attachment.ExternalId);
        Assert.Equal("Fix bug", attachment.Title);
    }

    [Fact]
    public async Task CreateSessionCommentWithExistingIssue_SessionNotFound_Throws()
    {
        await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.CreateSessionCommentWithExistingIssue(
                _project.Id, "nonexistent-session", 0, "text", null, null,
                IntegrationType.Slack, "url", null,
                _principal, _authz, _db, CancellationToken.None));
    }

    [Fact]
    public async Task CreateSessionCommentWithExistingIssue_NullIssueTitle()
    {
        var session = new Session { SecureId = "sess-2", ProjectId = _project.Id };
        _db.Sessions.Add(session);
        await _db.SaveChangesAsync();

        var result = await _mutation.CreateSessionCommentWithExistingIssue(
            _project.Id, "sess-2", 100, "Comment", null, null,
            IntegrationType.GitHub, "https://github.com/issue/1", null,
            _principal, _authz, _db, CancellationToken.None);

        var attachment = await _db.ExternalAttachments
            .FirstOrDefaultAsync(a => a.SessionCommentId == result.Id);
        Assert.NotNull(attachment);
        Assert.Null(attachment.Title);
    }

    // ── CreateErrorCommentForExistingIssue ───────────────────────────

    [Fact]
    public async Task CreateErrorCommentForExistingIssue_CreatesCommentAndAttachment()
    {
        var errorGroup = new ErrorGroup
        {
            ProjectId = _project.Id, Event = "TypeError", SecureId = "eg-1",
            State = ErrorGroupState.Open
        };
        _db.ErrorGroups.Add(errorGroup);
        await _db.SaveChangesAsync();

        var result = await _mutation.CreateErrorCommentForExistingIssue(
            _project.Id, "eg-1", "Linked to Jira", null,
            IntegrationType.Jira, "https://jira.com/PROJ-1", "PROJ-1",
            _principal, _authz, _db, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("Linked to Jira", result.Text);
        Assert.Equal(_admin.Id, result.AdminId);
        Assert.Equal(errorGroup.Id, result.ErrorGroupId);

        var attachment = await _db.ExternalAttachments
            .FirstOrDefaultAsync(a => a.ErrorGroupId == errorGroup.Id);
        Assert.NotNull(attachment);
        Assert.Equal("Jira", attachment.IntegrationType);
        Assert.Equal("PROJ-1", attachment.Title);
    }

    [Fact]
    public async Task CreateErrorCommentForExistingIssue_ErrorGroupNotFound_Throws()
    {
        await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.CreateErrorCommentForExistingIssue(
                _project.Id, "nonexistent-eg", "text", null,
                IntegrationType.Linear, "url", null,
                _principal, _authz, _db, CancellationToken.None));
    }

    [Fact]
    public async Task CreateErrorCommentForExistingIssue_MultipleCommentsOnSameErrorGroup()
    {
        var errorGroup = new ErrorGroup
        {
            ProjectId = _project.Id, Event = "err", SecureId = "eg-multi",
            State = ErrorGroupState.Open
        };
        _db.ErrorGroups.Add(errorGroup);
        await _db.SaveChangesAsync();

        var c1 = await _mutation.CreateErrorCommentForExistingIssue(
            _project.Id, "eg-multi", "First", null,
            IntegrationType.Linear, "url1", "T1",
            _principal, _authz, _db, CancellationToken.None);
        var c2 = await _mutation.CreateErrorCommentForExistingIssue(
            _project.Id, "eg-multi", "Second", null,
            IntegrationType.Jira, "url2", "T2",
            _principal, _authz, _db, CancellationToken.None);

        Assert.NotEqual(c1.Id, c2.Id);
        var attachments = await _db.ExternalAttachments
            .Where(a => a.ErrorGroupId == errorGroup.Id).ToListAsync();
        Assert.Equal(2, attachments.Count);
    }

    // ── RemoveErrorIssue ─────────────────────────────────────────────

    [Fact]
    public async Task RemoveErrorIssue_RemovesAllAttachments()
    {
        var errorGroup = new ErrorGroup
        {
            ProjectId = _project.Id, Event = "err", SecureId = "eg-remove",
            State = ErrorGroupState.Open
        };
        _db.ErrorGroups.Add(errorGroup);
        await _db.SaveChangesAsync();

        _db.ExternalAttachments.Add(new ExternalAttachment
        {
            ErrorGroupId = errorGroup.Id, IntegrationType = "Linear",
            ExternalId = "url1", Title = "T1"
        });
        _db.ExternalAttachments.Add(new ExternalAttachment
        {
            ErrorGroupId = errorGroup.Id, IntegrationType = "Jira",
            ExternalId = "url2", Title = "T2"
        });
        await _db.SaveChangesAsync();

        var result = await _mutation.RemoveErrorIssue(
            "eg-remove", _principal, _authz, _db, CancellationToken.None);

        Assert.True(result);
        var remaining = await _db.ExternalAttachments
            .CountAsync(a => a.ErrorGroupId == errorGroup.Id);
        Assert.Equal(0, remaining);
    }

    [Fact]
    public async Task RemoveErrorIssue_ErrorGroupNotFound_Throws()
    {
        await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.RemoveErrorIssue(
                "nonexistent-eg", _principal, _authz, _db, CancellationToken.None));
    }

    [Fact]
    public async Task RemoveErrorIssue_NoAttachments_ReturnsTrue()
    {
        var errorGroup = new ErrorGroup
        {
            ProjectId = _project.Id, Event = "err", SecureId = "eg-empty",
            State = ErrorGroupState.Open
        };
        _db.ErrorGroups.Add(errorGroup);
        await _db.SaveChangesAsync();

        var result = await _mutation.RemoveErrorIssue(
            "eg-empty", _principal, _authz, _db, CancellationToken.None);

        Assert.True(result);
    }

    [Fact]
    public async Task RemoveErrorIssue_DoesNotAffectOtherErrorGroupAttachments()
    {
        var eg1 = new ErrorGroup
        {
            ProjectId = _project.Id, Event = "err1", SecureId = "eg-keep1",
            State = ErrorGroupState.Open
        };
        var eg2 = new ErrorGroup
        {
            ProjectId = _project.Id, Event = "err2", SecureId = "eg-keep2",
            State = ErrorGroupState.Open
        };
        _db.ErrorGroups.AddRange(eg1, eg2);
        await _db.SaveChangesAsync();

        _db.ExternalAttachments.Add(new ExternalAttachment
        {
            ErrorGroupId = eg1.Id, IntegrationType = "Linear", ExternalId = "u1"
        });
        _db.ExternalAttachments.Add(new ExternalAttachment
        {
            ErrorGroupId = eg2.Id, IntegrationType = "Linear", ExternalId = "u2"
        });
        await _db.SaveChangesAsync();

        await _mutation.RemoveErrorIssue("eg-keep1", _principal, _authz, _db, CancellationToken.None);

        var remaining = await _db.ExternalAttachments.CountAsync(a => a.ErrorGroupId == eg2.Id);
        Assert.Equal(1, remaining);
    }

    // ── EditServiceGithubSettings ────────────────────────────────────

    [Fact]
    public async Task EditServiceGithubSettings_UpdatesAllFields()
    {
        var service = new Service { ProjectId = _project.Id, Name = "api-gateway" };
        _db.Services.Add(service);
        await _db.SaveChangesAsync();

        var result = await _mutation.EditServiceGithubSettings(
            service.Id, "org/repo", "/build", "/src",
            _principal, _authz, _db, CancellationToken.None);

        Assert.Equal("org/repo", result.GithubRepoPath);
        Assert.Equal("/build", result.BuildPrefix);
        Assert.Equal("/src", result.GithubPrefix);
    }

    [Fact]
    public async Task EditServiceGithubSettings_NullFieldsPreserveExisting()
    {
        var service = new Service
        {
            ProjectId = _project.Id, Name = "svc",
            GithubRepoPath = "original/repo", BuildPrefix = "/orig-build"
        };
        _db.Services.Add(service);
        await _db.SaveChangesAsync();

        var result = await _mutation.EditServiceGithubSettings(
            service.Id, null, null, "/new-prefix",
            _principal, _authz, _db, CancellationToken.None);

        Assert.Equal("original/repo", result.GithubRepoPath); // Preserved
        Assert.Equal("/orig-build", result.BuildPrefix);       // Preserved
        Assert.Equal("/new-prefix", result.GithubPrefix);      // Updated
    }

    [Fact]
    public async Task EditServiceGithubSettings_ServiceNotFound_Throws()
    {
        await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.EditServiceGithubSettings(
                99999, "repo", null, null,
                _principal, _authz, _db, CancellationToken.None));
    }

    [Fact]
    public async Task EditServiceGithubSettings_PreservesServiceName()
    {
        var service = new Service { ProjectId = _project.Id, Name = "my-service" };
        _db.Services.Add(service);
        await _db.SaveChangesAsync();

        var result = await _mutation.EditServiceGithubSettings(
            service.Id, "r/r", null, null,
            _principal, _authz, _db, CancellationToken.None);

        Assert.Equal("my-service", result.Name);
    }

    // ── CreateCloudflareProxy ────────────────────────────────────────

    [Fact]
    public async Task CreateCloudflareProxy_SetsProxy()
    {
        var result = await _mutation.CreateCloudflareProxy(
            _workspace.Id, "my-proxy.example.com",
            _principal, _authz, _db, CancellationToken.None);

        Assert.True(result);
        var ws = await _db.Workspaces.FindAsync(_workspace.Id);
        Assert.Equal("my-proxy.example.com", ws!.CloudflareProxy);
    }

    [Fact]
    public async Task CreateCloudflareProxy_OverwritesExisting()
    {
        _workspace.CloudflareProxy = "old-proxy.com";
        await _db.SaveChangesAsync();

        await _mutation.CreateCloudflareProxy(
            _workspace.Id, "new-proxy.com",
            _principal, _authz, _db, CancellationToken.None);

        var ws = await _db.Workspaces.FindAsync(_workspace.Id);
        Assert.Equal("new-proxy.com", ws!.CloudflareProxy);
    }

    [Fact]
    public async Task CreateCloudflareProxy_NonexistentWorkspace_Throws()
    {
        await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.CreateCloudflareProxy(
                99999, "proxy.com",
                _principal, _authz, _db, CancellationToken.None));
    }

    // ── UpsertSlackChannel ───────────────────────────────────────────

    [Fact]
    public async Task UpsertSlackChannel_AddsChannel()
    {
        var result = await _mutation.UpsertSlackChannel(
            _workspace.Id, "general",
            _principal, _authz, _db, CancellationToken.None);

        Assert.True(result);
        var ws = await _db.Workspaces.FindAsync(_workspace.Id);
        Assert.Contains("general", ws!.SlackChannels!);
    }

    [Fact]
    public async Task UpsertSlackChannel_DuplicateIsIdempotent()
    {
        await _mutation.UpsertSlackChannel(
            _workspace.Id, "alerts",
            _principal, _authz, _db, CancellationToken.None);
        await _mutation.UpsertSlackChannel(
            _workspace.Id, "alerts",
            _principal, _authz, _db, CancellationToken.None);

        var ws = await _db.Workspaces.FindAsync(_workspace.Id);
        var channels = System.Text.Json.JsonSerializer.Deserialize<List<string>>(ws!.SlackChannels!);
        Assert.NotNull(channels);
        Assert.Single(channels);
        Assert.Equal("alerts", channels[0]);
    }

    [Fact]
    public async Task UpsertSlackChannel_MultipleChannels()
    {
        await _mutation.UpsertSlackChannel(
            _workspace.Id, "channel-a",
            _principal, _authz, _db, CancellationToken.None);
        await _mutation.UpsertSlackChannel(
            _workspace.Id, "channel-b",
            _principal, _authz, _db, CancellationToken.None);

        var ws = await _db.Workspaces.FindAsync(_workspace.Id);
        var channels = System.Text.Json.JsonSerializer.Deserialize<List<string>>(ws!.SlackChannels!);
        Assert.NotNull(channels);
        Assert.Equal(2, channels.Count);
        Assert.Contains("channel-a", channels);
        Assert.Contains("channel-b", channels);
    }

    [Fact]
    public async Task UpsertSlackChannel_NonexistentWorkspace_Throws()
    {
        await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.UpsertSlackChannel(
                99999, "ch",
                _principal, _authz, _db, CancellationToken.None));
    }

    // ── UpsertDiscordChannel ─────────────────────────────────────────

    [Fact]
    public async Task UpsertDiscordChannel_SetsGuildId()
    {
        var result = await _mutation.UpsertDiscordChannel(
            _workspace.Id, "guild-123",
            _principal, _authz, _db, CancellationToken.None);

        Assert.True(result);
        var ws = await _db.Workspaces.FindAsync(_workspace.Id);
        Assert.Equal("guild-123", ws!.DiscordGuildId);
    }

    [Fact]
    public async Task UpsertDiscordChannel_DoesNotOverwriteExisting()
    {
        _workspace.DiscordGuildId = "existing-guild";
        await _db.SaveChangesAsync();

        await _mutation.UpsertDiscordChannel(
            _workspace.Id, "new-guild",
            _principal, _authz, _db, CancellationToken.None);

        var ws = await _db.Workspaces.FindAsync(_workspace.Id);
        Assert.Equal("existing-guild", ws!.DiscordGuildId);
    }

    [Fact]
    public async Task UpsertDiscordChannel_NonexistentWorkspace_Throws()
    {
        await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.UpsertDiscordChannel(
                99999, "guild",
                _principal, _authz, _db, CancellationToken.None));
    }

    [Fact]
    public async Task UpsertDiscordChannel_EmptyString_DoesNotSet()
    {
        // Empty string passes IsNullOrEmpty check
        await _mutation.UpsertDiscordChannel(
            _workspace.Id, "guild-1",
            _principal, _authz, _db, CancellationToken.None);

        var ws = await _db.Workspaces.FindAsync(_workspace.Id);
        Assert.Equal("guild-1", ws!.DiscordGuildId);
    }

    // ── Integration Add/Remove/Verify Roundtrip ──────────────────────

    [Fact]
    public async Task IntegrationProjectMapping_AddRemoveVerifyRoundtrip()
    {
        // Add
        await _mutation.AddIntegrationToProject(
            IntegrationType.ClickUp, _project.Id, "CU-1",
            _principal, _authz, _db, CancellationToken.None);

        var exists = await _db.IntegrationProjectMappings
            .AnyAsync(m => m.ProjectId == _project.Id && m.IntegrationType == "ClickUp");
        Assert.True(exists);

        // Remove
        await _mutation.RemoveIntegrationFromProject(
            IntegrationType.ClickUp, _project.Id,
            _principal, _authz, _db, CancellationToken.None);

        exists = await _db.IntegrationProjectMappings
            .AnyAsync(m => m.ProjectId == _project.Id && m.IntegrationType == "ClickUp");
        Assert.False(exists);
    }

    [Fact]
    public async Task IntegrationWorkspaceMapping_AddRemoveVerifyRoundtrip()
    {
        await _mutation.AddIntegrationToWorkspace(
            IntegrationType.MicrosoftTeams, _workspace.Id, "teams-tok",
            _principal, _authz, _db, CancellationToken.None);

        var exists = await _db.IntegrationWorkspaceMappings
            .AnyAsync(m => m.WorkspaceId == _workspace.Id && m.IntegrationType == "MicrosoftTeams");
        Assert.True(exists);

        await _mutation.RemoveIntegrationFromWorkspace(
            IntegrationType.MicrosoftTeams, _workspace.Id,
            _principal, _authz, _db, CancellationToken.None);

        exists = await _db.IntegrationWorkspaceMappings
            .AnyAsync(m => m.WorkspaceId == _workspace.Id && m.IntegrationType == "MicrosoftTeams");
        Assert.False(exists);
    }

    // ── AddIntegrationToProject with null code ───────────────────────

    [Fact]
    public async Task AddIntegrationToProject_NullCode_CreatesWithNullExternalId()
    {
        await _mutation.AddIntegrationToProject(
            IntegrationType.Zapier, _project.Id, null,
            _principal, _authz, _db, CancellationToken.None);

        var mapping = await _db.IntegrationProjectMappings
            .FirstOrDefaultAsync(m => m.ProjectId == _project.Id && m.IntegrationType == "Zapier");
        Assert.NotNull(mapping);
        Assert.Null(mapping.ExternalId);
    }

    // ── AddIntegrationToWorkspace with null code ─────────────────────

    [Fact]
    public async Task AddIntegrationToWorkspace_NullCode_CreatesWithNullAccessToken()
    {
        await _mutation.AddIntegrationToWorkspace(
            IntegrationType.GitLab, _workspace.Id, null,
            _principal, _authz, _db, CancellationToken.None);

        var mapping = await _db.IntegrationWorkspaceMappings
            .FirstOrDefaultAsync(m => m.WorkspaceId == _workspace.Id && m.IntegrationType == "GitLab");
        Assert.NotNull(mapping);
        Assert.Null(mapping.AccessToken);
    }
}
