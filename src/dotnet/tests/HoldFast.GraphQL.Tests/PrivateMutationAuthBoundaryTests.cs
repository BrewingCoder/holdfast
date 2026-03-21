using System.Security.Claims;
using HoldFast.Data;
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
/// Tests verifying authorization boundaries in mutations:
/// - MEMBER role cannot perform ADMIN-only operations
/// - Unauthenticated users are rejected
/// - Cross-workspace access is denied
/// - Cross-project access is denied
/// </summary>
public class PrivateMutationAuthBoundaryTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly HoldFastDbContext _db;
    private readonly AuthorizationService _authz;
    private readonly PrivateMutation _mutation;
    private readonly PrivateQuery _query;

    // Admin user (full access)
    private readonly Admin _adminUser;
    private readonly ClaimsPrincipal _adminPrincipal;

    // Member user (limited access)
    private readonly Admin _memberUser;
    private readonly ClaimsPrincipal _memberPrincipal;

    // Outsider (not in workspace)
    private readonly Admin _outsider;
    private readonly ClaimsPrincipal _outsiderPrincipal;

    private readonly Workspace _workspace;
    private readonly Project _project;

    public PrivateMutationAuthBoundaryTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<HoldFastDbContext>()
            .UseSqlite(_connection).Options;
        _db = new HoldFastDbContext(options);
        _db.Database.EnsureCreated();
        _mutation = new PrivateMutation();
        _query = new PrivateQuery();

        // Create users
        _adminUser = new Admin { Uid = "admin-uid", Email = "admin@test.com" };
        _memberUser = new Admin { Uid = "member-uid", Email = "member@test.com" };
        _outsider = new Admin { Uid = "outsider-uid", Email = "outsider@test.com" };
        _db.Admins.AddRange(_adminUser, _memberUser, _outsider);

        _workspace = new Workspace { Name = "TestWS" };
        _db.Workspaces.Add(_workspace);
        _db.SaveChanges();

        // Admin user has ADMIN role
        _db.WorkspaceAdmins.Add(new WorkspaceAdmin
            { AdminId = _adminUser.Id, WorkspaceId = _workspace.Id, Role = "ADMIN" });
        // Member user has MEMBER role
        _db.WorkspaceAdmins.Add(new WorkspaceAdmin
            { AdminId = _memberUser.Id, WorkspaceId = _workspace.Id, Role = "MEMBER" });
        // Outsider has NO workspace membership

        _project = new Project { Name = "TestProj", WorkspaceId = _workspace.Id };
        _db.Projects.Add(_project);
        _db.SaveChanges();

        _authz = new AuthorizationService(_db);

        _adminPrincipal = MakePrincipal(_adminUser);
        _memberPrincipal = MakePrincipal(_memberUser);
        _outsiderPrincipal = MakePrincipal(_outsider);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private static ClaimsPrincipal MakePrincipal(Admin admin) =>
        new(new ClaimsIdentity(new[]
        {
            new Claim(HoldFastClaimTypes.Uid, admin.Uid!),
            new Claim(HoldFastClaimTypes.AdminId, admin.Id.ToString()),
        }, "Test"));

    private static readonly ClaimsPrincipal AnonymousPrincipal =
        new(new ClaimsIdentity()); // No auth claims

    // ── SaveBillingPlan requires ADMIN role ──────────────────────────

    [Fact]
    public async Task SaveBillingPlan_MemberRole_Throws()
    {
        await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.SaveBillingPlan(
                _workspace.Id,
                RetentionPeriod.ThirtyDays, RetentionPeriod.ThirtyDays,
                RetentionPeriod.ThirtyDays, RetentionPeriod.ThirtyDays,
                RetentionPeriod.ThirtyDays,
                _memberPrincipal, _authz, _db, CancellationToken.None));
    }

    [Fact]
    public async Task SaveBillingPlan_Outsider_Throws()
    {
        await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.SaveBillingPlan(
                _workspace.Id,
                RetentionPeriod.ThirtyDays, RetentionPeriod.ThirtyDays,
                RetentionPeriod.ThirtyDays, RetentionPeriod.ThirtyDays,
                RetentionPeriod.ThirtyDays,
                _outsiderPrincipal, _authz, _db, CancellationToken.None));
    }

    [Fact]
    public async Task SaveBillingPlan_Anonymous_Throws()
    {
        await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.SaveBillingPlan(
                _workspace.Id,
                RetentionPeriod.ThirtyDays, RetentionPeriod.ThirtyDays,
                RetentionPeriod.ThirtyDays, RetentionPeriod.ThirtyDays,
                RetentionPeriod.ThirtyDays,
                AnonymousPrincipal, _authz, _db, CancellationToken.None));
    }

    // ── DeleteSessions requires ADMIN role ──────────────────────────

    [Fact]
    public async Task DeleteSessions_MemberRole_Throws()
    {
        await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.DeleteSessions(
                _project.Id, _memberPrincipal, _authz, _db, CancellationToken.None));
    }

    [Fact]
    public async Task DeleteSessions_Outsider_Throws()
    {
        await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.DeleteSessions(
                _project.Id, _outsiderPrincipal, _authz, _db, CancellationToken.None));
    }

    // ── ChangeProjectMembership requires ADMIN role ─────────────────

    [Fact]
    public async Task ChangeProjectMembership_MemberRole_Throws()
    {
        await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.ChangeProjectMembership(
                _workspace.Id, _memberUser.Id,
                new List<int> { _project.Id },
                _memberPrincipal, _authz, _db, CancellationToken.None));
    }

    // ── UpdateAllowedEmailOrigins requires ADMIN role ────────────────

    [Fact]
    public async Task UpdateAllowedEmailOrigins_MemberRole_Throws()
    {
        await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.UpdateAllowedEmailOrigins(
                _workspace.Id,
                new List<string> { "company.com" },
                _memberPrincipal, _authz, _db, CancellationToken.None));
    }

    // ── EditWorkspace requires ADMIN role ────────────────────────────

    [Fact]
    public async Task EditWorkspace_MemberRole_Throws()
    {
        await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.EditWorkspace(
                _workspace.Id, "New Name",
                _memberPrincipal, _authz, _db, CancellationToken.None));
    }

    [Fact]
    public async Task EditWorkspace_AdminRole_Succeeds()
    {
        var ws = await _mutation.EditWorkspace(
            _workspace.Id, "Renamed",
            _adminPrincipal, _authz, _db, CancellationToken.None);
        Assert.Equal("Renamed", ws.Name);
    }

    // ── Cross-workspace isolation ────────────────────────────────────

    [Fact]
    public async Task CreateProject_InOtherWorkspace_Throws()
    {
        var otherWs = new Workspace { Name = "OtherWS" };
        _db.Workspaces.Add(otherWs);
        await _db.SaveChangesAsync();

        // Admin is not in otherWs
        await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.CreateProject(
                otherWs.Id, "Sneaky Project",
                _adminPrincipal, _authz, _db, CancellationToken.None));
    }

    // ── Member can perform project-level operations ──────────────────

    [Fact]
    public async Task UpdateErrorGroupState_MemberCanAccess()
    {
        var eg = new ErrorGroup
        {
            ProjectId = _project.Id, Event = "err", SecureId = "eg-member",
            State = ErrorGroupState.Open
        };
        _db.ErrorGroups.Add(eg);
        await _db.SaveChangesAsync();

        var result = await _mutation.UpdateErrorGroupState(
            eg.Id, ErrorGroupState.Resolved,
            _memberPrincipal, _authz, _db, CancellationToken.None);

        Assert.Equal(ErrorGroupState.Resolved, result.State);
    }

    [Fact]
    public async Task CreateErrorComment_MemberCanAccess()
    {
        var eg = new ErrorGroup
        {
            ProjectId = _project.Id, Event = "err", SecureId = "eg-comment-member",
            State = ErrorGroupState.Open
        };
        _db.ErrorGroups.Add(eg);
        await _db.SaveChangesAsync();

        var comment = await _mutation.CreateErrorComment(
            eg.Id, "Member comment",
            _memberPrincipal, _authz, _db, CancellationToken.None);

        Assert.Equal("Member comment", comment.Text);
    }

    // ── Outsider cannot access project resources ─────────────────────

    [Fact]
    public async Task UpdateErrorGroupState_Outsider_Throws()
    {
        var eg = new ErrorGroup
        {
            ProjectId = _project.Id, Event = "err", SecureId = "eg-outsider",
            State = ErrorGroupState.Open
        };
        _db.ErrorGroups.Add(eg);
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.UpdateErrorGroupState(
                eg.Id, ErrorGroupState.Resolved,
                _outsiderPrincipal, _authz, _db, CancellationToken.None));
    }

    [Fact]
    public async Task CreateSessionComment_Outsider_Throws()
    {
        var session = new Session
        {
            SecureId = "sess-outsider", ProjectId = _project.Id,
            CreatedAt = DateTime.UtcNow
        };
        _db.Sessions.Add(session);
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.CreateSessionComment(
                _project.Id, session.Id, "Sneaky", 0, 0, 0,
                _outsiderPrincipal, _authz, _db, CancellationToken.None));
    }

    // ── Query auth boundaries ────────────────────────────────────────

    [Fact]
    public async Task GetWorkspace_Outsider_Throws()
    {
        await Assert.ThrowsAsync<GraphQLException>(() =>
            _query.GetWorkspace(
                _workspace.Id, _outsiderPrincipal, _authz, _db, CancellationToken.None));
    }

    [Fact]
    public async Task GetProject_Outsider_Throws()
    {
        await Assert.ThrowsAsync<GraphQLException>(() =>
            _query.GetProject(
                _project.Id, _outsiderPrincipal, _authz, _db, CancellationToken.None));
    }

    [Fact]
    public async Task GetServices_Outsider_Throws()
    {
        await Assert.ThrowsAsync<GraphQLException>(() =>
            _query.GetServices(
                _project.Id, _outsiderPrincipal, _authz, _db, CancellationToken.None));
    }

    [Fact]
    public async Task GetSavedSegments_Outsider_Throws()
    {
        await Assert.ThrowsAsync<GraphQLException>(() =>
            _query.GetSavedSegments(
                _project.Id, null, _outsiderPrincipal, _authz, _db, CancellationToken.None));
    }

    [Fact]
    public async Task GetWorkspace_Member_Succeeds()
    {
        var ws = await _query.GetWorkspace(
            _workspace.Id, _memberPrincipal, _authz, _db, CancellationToken.None);
        Assert.NotNull(ws);
        Assert.Equal("TestWS", ws!.Name);
    }

    [Fact]
    public async Task GetProject_Member_Succeeds()
    {
        var proj = await _query.GetProject(
            _project.Id, _memberPrincipal, _authz, _db, CancellationToken.None);
        Assert.NotNull(proj);
    }
}
