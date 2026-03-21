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
/// Tests for PrivateMutation session comment operations:
/// CreateSessionComment, DeleteSessionComment, ReplyToSessionComment,
/// plus workspace invite link operations:
/// CreateWorkspaceInviteLink, AcceptWorkspaceInvite.
/// </summary>
public class PrivateMutationSessionCommentTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly HoldFastDbContext _db;
    private readonly AuthorizationService _authz;
    private readonly PrivateMutation _mutation;
    private readonly Admin _admin;
    private readonly Workspace _workspace;
    private readonly Project _project;
    private readonly Session _session;

    public PrivateMutationSessionCommentTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<HoldFastDbContext>()
            .UseSqlite(_connection).Options;
        _db = new HoldFastDbContext(options);
        _db.Database.EnsureCreated();

        _admin = new Admin { Uid = "sc-admin", Email = "sc@test.com", Name = "Comment Admin" };
        _db.Admins.Add(_admin);
        _workspace = new Workspace { Name = "CommentWS" };
        _db.Workspaces.Add(_workspace);
        _db.SaveChanges();

        _db.WorkspaceAdmins.Add(new WorkspaceAdmin
        {
            AdminId = _admin.Id, WorkspaceId = _workspace.Id, Role = "ADMIN",
        });
        _project = new Project { Name = "CommentProj", WorkspaceId = _workspace.Id };
        _db.Projects.Add(_project);
        _db.SaveChanges();

        _session = new Session { SecureId = "comment-sess", ProjectId = _project.Id };
        _db.Sessions.Add(_session);
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

    // ── CreateSessionComment ────────────────────────────────────────

    [Fact]
    public async Task CreateSessionComment_CreatesWithCoordinates()
    {
        var result = await _mutation.CreateSessionComment(
            _project.Id, _session.Id, "Bug here", 5000, 120.5, 300.2,
            MakePrincipal("sc-admin"), _authz, _db, CancellationToken.None);

        Assert.Equal("Bug here", result.Text);
        Assert.Equal(5000, result.Timestamp);
        Assert.Equal(120.5, result.XCoordinate);
        Assert.Equal(300.2, result.YCoordinate);
        Assert.Equal("ADMIN", result.Type);
        Assert.Equal(_admin.Id, result.AdminId);
    }

    [Fact]
    public async Task CreateSessionComment_ZeroCoordinates()
    {
        var result = await _mutation.CreateSessionComment(
            _project.Id, _session.Id, "At origin", 0, 0.0, 0.0,
            MakePrincipal("sc-admin"), _authz, _db, CancellationToken.None);

        Assert.Equal(0.0, result.XCoordinate);
        Assert.Equal(0.0, result.YCoordinate);
        Assert.Equal(0, result.Timestamp);
    }

    [Fact]
    public async Task CreateSessionComment_NegativeCoordinates()
    {
        var result = await _mutation.CreateSessionComment(
            _project.Id, _session.Id, "Negative", 100, -10.5, -20.3,
            MakePrincipal("sc-admin"), _authz, _db, CancellationToken.None);

        Assert.Equal(-10.5, result.XCoordinate);
        Assert.Equal(-20.3, result.YCoordinate);
    }

    [Fact]
    public async Task CreateSessionComment_Unauthenticated_Throws()
    {
        await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.CreateSessionComment(
                _project.Id, _session.Id, "Text", 0, 0, 0,
                AnonymousPrincipal, _authz, _db, CancellationToken.None));
    }

    [Fact]
    public async Task CreateSessionComment_MultipleComments_SameSession()
    {
        await _mutation.CreateSessionComment(
            _project.Id, _session.Id, "First", 1000, 10, 20,
            MakePrincipal("sc-admin"), _authz, _db, CancellationToken.None);
        await _mutation.CreateSessionComment(
            _project.Id, _session.Id, "Second", 2000, 30, 40,
            MakePrincipal("sc-admin"), _authz, _db, CancellationToken.None);

        var count = await _db.SessionComments.CountAsync(c => c.SessionId == _session.Id);
        Assert.Equal(2, count);
    }

    [Fact]
    public async Task CreateSessionComment_EmptyText()
    {
        var result = await _mutation.CreateSessionComment(
            _project.Id, _session.Id, "", 0, 0, 0,
            MakePrincipal("sc-admin"), _authz, _db, CancellationToken.None);

        Assert.Equal("", result.Text);
    }

    [Fact]
    public async Task CreateSessionComment_LargeTimestamp()
    {
        var result = await _mutation.CreateSessionComment(
            _project.Id, _session.Id, "Late", int.MaxValue, 0, 0,
            MakePrincipal("sc-admin"), _authz, _db, CancellationToken.None);

        Assert.Equal(int.MaxValue, result.Timestamp);
    }

    // ── DeleteSessionComment ────────────────────────────────────────

    [Fact]
    public async Task DeleteSessionComment_RemovesComment()
    {
        var comment = new SessionComment
        {
            ProjectId = _project.Id, SessionId = _session.Id, AdminId = _admin.Id,
            Text = "To delete", Timestamp = 0, Type = "ADMIN",
        };
        _db.SessionComments.Add(comment);
        _db.SaveChanges();

        var result = await _mutation.DeleteSessionComment(
            comment.Id, MakePrincipal("sc-admin"), _authz, _db, CancellationToken.None);

        Assert.True(result);
        Assert.Null(await _db.SessionComments.FindAsync(comment.Id));
    }

    [Fact]
    public async Task DeleteSessionComment_NonexistentComment_Throws()
    {
        await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.DeleteSessionComment(
                99999, MakePrincipal("sc-admin"), _authz, _db, CancellationToken.None));
    }

    [Fact]
    public async Task DeleteSessionComment_Unauthenticated_Throws()
    {
        var comment = new SessionComment
        {
            ProjectId = _project.Id, SessionId = _session.Id, AdminId = _admin.Id,
            Text = "Auth test", Timestamp = 0, Type = "ADMIN",
        };
        _db.SessionComments.Add(comment);
        _db.SaveChanges();

        await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.DeleteSessionComment(
                comment.Id, AnonymousPrincipal, _authz, _db, CancellationToken.None));
    }

    // ── ReplyToSessionComment ───────────────────────────────────────

    [Fact]
    public async Task ReplyToSessionComment_CreatesReply()
    {
        var comment = new SessionComment
        {
            ProjectId = _project.Id, SessionId = _session.Id, AdminId = _admin.Id,
            Text = "Parent", Timestamp = 0, Type = "ADMIN",
        };
        _db.SessionComments.Add(comment);
        _db.SaveChanges();

        var result = await _mutation.ReplyToSessionComment(
            comment.Id, "This is a reply",
            MakePrincipal("sc-admin"), _authz, _db, CancellationToken.None);

        Assert.Equal("This is a reply", result.Text);
        Assert.Equal(comment.Id, result.SessionCommentId);
        Assert.Equal(_admin.Id, result.AdminId);
    }

    [Fact]
    public async Task ReplyToSessionComment_MultipleReplies()
    {
        var comment = new SessionComment
        {
            ProjectId = _project.Id, SessionId = _session.Id, AdminId = _admin.Id,
            Text = "Thread", Timestamp = 0, Type = "ADMIN",
        };
        _db.SessionComments.Add(comment);
        _db.SaveChanges();

        await _mutation.ReplyToSessionComment(
            comment.Id, "Reply 1",
            MakePrincipal("sc-admin"), _authz, _db, CancellationToken.None);
        await _mutation.ReplyToSessionComment(
            comment.Id, "Reply 2",
            MakePrincipal("sc-admin"), _authz, _db, CancellationToken.None);

        var replies = await _db.CommentReplies
            .Where(r => r.SessionCommentId == comment.Id).ToListAsync();
        Assert.Equal(2, replies.Count);
    }

    [Fact]
    public async Task ReplyToSessionComment_NonexistentComment_Throws()
    {
        await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.ReplyToSessionComment(
                99999, "Reply",
                MakePrincipal("sc-admin"), _authz, _db, CancellationToken.None));
    }

    [Fact]
    public async Task ReplyToSessionComment_Unauthenticated_Throws()
    {
        var comment = new SessionComment
        {
            ProjectId = _project.Id, SessionId = _session.Id, AdminId = _admin.Id,
            Text = "Auth", Timestamp = 0, Type = "ADMIN",
        };
        _db.SessionComments.Add(comment);
        _db.SaveChanges();

        await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.ReplyToSessionComment(
                comment.Id, "Reply",
                AnonymousPrincipal, _authz, _db, CancellationToken.None));
    }

    [Fact]
    public async Task ReplyToSessionComment_EmptyText()
    {
        var comment = new SessionComment
        {
            ProjectId = _project.Id, SessionId = _session.Id, AdminId = _admin.Id,
            Text = "Base", Timestamp = 0, Type = "ADMIN",
        };
        _db.SessionComments.Add(comment);
        _db.SaveChanges();

        var result = await _mutation.ReplyToSessionComment(
            comment.Id, "",
            MakePrincipal("sc-admin"), _authz, _db, CancellationToken.None);

        Assert.Equal("", result.Text);
    }

    // ── CreateWorkspaceInviteLink ───────────────────────────────────

    [Fact]
    public async Task CreateWorkspaceInviteLink_CreatesInvite()
    {
        var result = await _mutation.CreateWorkspaceInviteLink(
            _workspace.Id, "invite@test.com", "MEMBER", null, 7,
            MakePrincipal("sc-admin"), _authz, _db, CancellationToken.None);

        Assert.Equal("invite@test.com", result.InviteeEmail);
        Assert.Equal("MEMBER", result.InviteeRole);
        Assert.NotNull(result.Secret);
        Assert.NotEmpty(result.Secret!);
        Assert.NotNull(result.ExpirationDate);
    }

    [Fact]
    public async Task CreateWorkspaceInviteLink_DefaultExpiry_SevenDays()
    {
        var before = DateTime.UtcNow;
        var result = await _mutation.CreateWorkspaceInviteLink(
            _workspace.Id, "exp@test.com", "MEMBER", null, null,
            MakePrincipal("sc-admin"), _authz, _db, CancellationToken.None);

        // Default expiration is 7 days
        Assert.NotNull(result.ExpirationDate);
        Assert.True(result.ExpirationDate!.Value > before.AddDays(6));
        Assert.True(result.ExpirationDate.Value < before.AddDays(8));
    }

    [Fact]
    public async Task CreateWorkspaceInviteLink_DuplicateInvite_Throws()
    {
        await _mutation.CreateWorkspaceInviteLink(
            _workspace.Id, "dup@test.com", "MEMBER", null, null,
            MakePrincipal("sc-admin"), _authz, _db, CancellationToken.None);

        await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.CreateWorkspaceInviteLink(
                _workspace.Id, "dup@test.com", "MEMBER", null, null,
                MakePrincipal("sc-admin"), _authz, _db, CancellationToken.None));
    }

    [Fact]
    public async Task CreateWorkspaceInviteLink_ExistingMember_Throws()
    {
        // _admin is already a member
        await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.CreateWorkspaceInviteLink(
                _workspace.Id, "sc@test.com", "MEMBER", null, null,
                MakePrincipal("sc-admin"), _authz, _db, CancellationToken.None));
    }

    [Fact]
    public async Task CreateWorkspaceInviteLink_WithProjectIds()
    {
        var result = await _mutation.CreateWorkspaceInviteLink(
            _workspace.Id, "proj@test.com", "MEMBER", [_project.Id], 30,
            MakePrincipal("sc-admin"), _authz, _db, CancellationToken.None);

        Assert.NotNull(result.ProjectIds);
        Assert.Contains(_project.Id, result.ProjectIds!);
    }

    [Fact]
    public async Task CreateWorkspaceInviteLink_Unauthenticated_Throws()
    {
        await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.CreateWorkspaceInviteLink(
                _workspace.Id, "anon@test.com", "MEMBER", null, null,
                AnonymousPrincipal, _authz, _db, CancellationToken.None));
    }

    [Fact]
    public async Task CreateWorkspaceInviteLink_MemberRole_Throws()
    {
        var member = new Admin { Uid = "inv-member", Email = "invmem@test.com" };
        _db.Admins.Add(member);
        _db.SaveChanges();
        _db.WorkspaceAdmins.Add(new WorkspaceAdmin
        {
            AdminId = member.Id, WorkspaceId = _workspace.Id, Role = "MEMBER",
        });
        _db.SaveChanges();

        await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.CreateWorkspaceInviteLink(
                _workspace.Id, "someone@test.com", "MEMBER", null, null,
                MakePrincipal("inv-member"), _authz, _db, CancellationToken.None));
    }

    // ── AcceptWorkspaceInvite ───────────────────────────────────────

    [Fact]
    public async Task AcceptWorkspaceInvite_NonexistentSecret_Throws()
    {
        await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.AcceptWorkspaceInvite(
                "nonexistent-secret",
                MakePrincipal("sc-admin"), _authz, _db, CancellationToken.None));
    }

    [Fact]
    public async Task AcceptWorkspaceInvite_ExpiredInvite_Throws()
    {
        var invite = new WorkspaceInviteLink
        {
            WorkspaceId = _workspace.Id,
            InviteeEmail = "expired@test.com",
            InviteeRole = "MEMBER",
            Secret = "expired-secret",
            ExpirationDate = DateTime.UtcNow.AddDays(-1), // already expired
        };
        _db.WorkspaceInviteLinks.Add(invite);
        _db.SaveChanges();

        await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.AcceptWorkspaceInvite(
                "expired-secret",
                MakePrincipal("sc-admin"), _authz, _db, CancellationToken.None));
    }

    // ── UpdateAdminAboutYouDetails ──────────────────────────────────

    [Fact]
    public async Task UpdateAdminAboutYouDetails_UpdatesName()
    {
        var result = await _mutation.UpdateAdminAboutYouDetails(
            "New Name", null, null,
            MakePrincipal("sc-admin"), _authz, CancellationToken.None);

        Assert.Equal("New Name", result.Name);
    }

    [Fact]
    public async Task UpdateAdminAboutYouDetails_UpdatesAllFields()
    {
        var result = await _mutation.UpdateAdminAboutYouDetails(
            "Scott", "friend", "developer",
            MakePrincipal("sc-admin"), _authz, CancellationToken.None);

        Assert.Equal("Scott", result.Name);
        Assert.Equal("friend", result.Referral);
        Assert.Equal("developer", result.UserDefinedRole);
    }

    [Fact]
    public async Task UpdateAdminAboutYouDetails_NullFields_NoChange()
    {
        // Set initial values
        _admin.Name = "Original";
        _admin.Referral = "google";
        _db.SaveChanges();

        var result = await _mutation.UpdateAdminAboutYouDetails(
            null, null, null,
            MakePrincipal("sc-admin"), _authz, CancellationToken.None);

        Assert.Equal("Original", result.Name);
        Assert.Equal("google", result.Referral);
    }

    [Fact]
    public async Task UpdateAdminAboutYouDetails_Unauthenticated_Throws()
    {
        await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.UpdateAdminAboutYouDetails(
                "Name", null, null,
                AnonymousPrincipal, _authz, CancellationToken.None));
    }
}
