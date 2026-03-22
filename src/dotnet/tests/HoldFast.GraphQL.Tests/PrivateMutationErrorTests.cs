using System.Security.Claims;
using HoldFast.Data;
using HoldFast.Domain.Entities;
using HoldFast.Domain.Enums;
using HoldFast.GraphQL.Private;
using HoldFast.Shared.Auth;
using HotChocolate;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace HoldFast.GraphQL.Tests;

/// <summary>
/// Tests for PrivateMutation error management operations:
/// UpdateErrorGroupState, UpdateErrorGroupIsPublic,
/// MarkErrorGroupAsViewed, CreateErrorTag,
/// CreateErrorComment, DeleteErrorComment,
/// UpdateSessionIsPublic.
/// </summary>
public class PrivateMutationErrorTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly HoldFastDbContext _db;
    private readonly AuthorizationService _authz;
    private readonly PrivateMutation _mutation;
    private readonly Admin _admin;
    private readonly Workspace _workspace;
    private readonly Project _project;
    private readonly ErrorGroup _errorGroup;

    public PrivateMutationErrorTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<HoldFastDbContext>()
            .UseSqlite(_connection).Options;
        _db = new HoldFastDbContext(options);
        _db.Database.EnsureCreated();

        _admin = new Admin { Uid = "err-admin", Email = "err@test.com", Name = "Error Admin" };
        _db.Admins.Add(_admin);
        _workspace = new Workspace { Name = "ErrWS" };
        _db.Workspaces.Add(_workspace);
        _db.SaveChanges();

        _db.WorkspaceAdmins.Add(new WorkspaceAdmin
        {
            AdminId = _admin.Id, WorkspaceId = _workspace.Id, Role = "ADMIN",
        });
        _project = new Project { Name = "ErrProj", WorkspaceId = _workspace.Id };
        _db.Projects.Add(_project);
        _db.SaveChanges();

        _errorGroup = new ErrorGroup
        {
            ProjectId = _project.Id,
            Event = "NullReferenceException",
            Type = "System.NullReferenceException",
            State = ErrorGroupState.Open,
            SecureId = "eg-secure-1",
        };
        _db.ErrorGroups.Add(_errorGroup);
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

    // ── UpdateErrorGroupState ───────────────────────────────────────

    [Fact]
    public async Task UpdateErrorGroupState_OpenToResolved()
    {
        var result = await _mutation.UpdateErrorGroupState(
            _errorGroup.Id, ErrorGroupState.Resolved,
            MakePrincipal("err-admin"), _authz, _db, CancellationToken.None);

        Assert.Equal(ErrorGroupState.Resolved, result.State);
    }

    [Fact]
    public async Task UpdateErrorGroupState_ResolvedToIgnored()
    {
        _errorGroup.State = ErrorGroupState.Resolved;
        _db.SaveChanges();

        var result = await _mutation.UpdateErrorGroupState(
            _errorGroup.Id, ErrorGroupState.Ignored,
            MakePrincipal("err-admin"), _authz, _db, CancellationToken.None);

        Assert.Equal(ErrorGroupState.Ignored, result.State);
    }

    [Fact]
    public async Task UpdateErrorGroupState_IgnoredToOpen()
    {
        _errorGroup.State = ErrorGroupState.Ignored;
        _db.SaveChanges();

        var result = await _mutation.UpdateErrorGroupState(
            _errorGroup.Id, ErrorGroupState.Open,
            MakePrincipal("err-admin"), _authz, _db, CancellationToken.None);

        Assert.Equal(ErrorGroupState.Open, result.State);
    }

    [Fact]
    public async Task UpdateErrorGroupState_CreatesActivityLog()
    {
        await _mutation.UpdateErrorGroupState(
            _errorGroup.Id, ErrorGroupState.Resolved,
            MakePrincipal("err-admin"), _authz, _db, CancellationToken.None);

        var log = await _db.ErrorGroupActivityLogs
            .FirstOrDefaultAsync(l => l.ErrorGroupId == _errorGroup.Id);
        Assert.NotNull(log);
        Assert.Equal("Resolved", log!.Action);
        Assert.Equal(_admin.Id, log.AdminId);
    }

    [Fact]
    public async Task UpdateErrorGroupState_SameState_StillCreatesLog()
    {
        // Transitioning to the same state is allowed
        await _mutation.UpdateErrorGroupState(
            _errorGroup.Id, ErrorGroupState.Open,
            MakePrincipal("err-admin"), _authz, _db, CancellationToken.None);

        var count = await _db.ErrorGroupActivityLogs
            .CountAsync(l => l.ErrorGroupId == _errorGroup.Id);
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task UpdateErrorGroupState_NonexistentGroup_Throws()
    {
        await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.UpdateErrorGroupState(
                99999, ErrorGroupState.Resolved,
                MakePrincipal("err-admin"), _authz, _db, CancellationToken.None));
    }

    [Fact]
    public async Task UpdateErrorGroupState_Unauthenticated_Throws()
    {
        await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.UpdateErrorGroupState(
                _errorGroup.Id, ErrorGroupState.Resolved,
                AnonymousPrincipal, _authz, _db, CancellationToken.None));
    }

    [Theory]
    [InlineData(ErrorGroupState.Open)]
    [InlineData(ErrorGroupState.Resolved)]
    [InlineData(ErrorGroupState.Ignored)]
    public async Task UpdateErrorGroupState_AllStates(ErrorGroupState state)
    {
        var result = await _mutation.UpdateErrorGroupState(
            _errorGroup.Id, state,
            MakePrincipal("err-admin"), _authz, _db, CancellationToken.None);

        Assert.Equal(state, result.State);
    }

    // ── UpdateErrorGroupIsPublic ────────────────────────────────────

    [Fact]
    public async Task UpdateErrorGroupIsPublic_SetsTrue()
    {
        var result = await _mutation.UpdateErrorGroupIsPublic(
            _errorGroup.Id, true,
            MakePrincipal("err-admin"), _authz, _db, CancellationToken.None);

        Assert.True(result.IsPublic);
    }

    [Fact]
    public async Task UpdateErrorGroupIsPublic_SetsFalse()
    {
        _errorGroup.IsPublic = true;
        _db.SaveChanges();

        var result = await _mutation.UpdateErrorGroupIsPublic(
            _errorGroup.Id, false,
            MakePrincipal("err-admin"), _authz, _db, CancellationToken.None);

        Assert.False(result.IsPublic);
    }

    [Fact]
    public async Task UpdateErrorGroupIsPublic_ToggleTwice()
    {
        await _mutation.UpdateErrorGroupIsPublic(
            _errorGroup.Id, true,
            MakePrincipal("err-admin"), _authz, _db, CancellationToken.None);

        var result = await _mutation.UpdateErrorGroupIsPublic(
            _errorGroup.Id, false,
            MakePrincipal("err-admin"), _authz, _db, CancellationToken.None);

        Assert.False(result.IsPublic);
    }

    [Fact]
    public async Task UpdateErrorGroupIsPublic_NonexistentGroup_Throws()
    {
        await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.UpdateErrorGroupIsPublic(
                99999, true,
                MakePrincipal("err-admin"), _authz, _db, CancellationToken.None));
    }

    // ── MarkErrorGroupAsViewed ──────────────────────────────────────

    [Fact]
    public async Task MarkErrorGroupAsViewed_CreatesViewRecord()
    {
        var result = await _mutation.MarkErrorGroupAsViewed(
            _errorGroup.Id,
            MakePrincipal("err-admin"), _authz, _db, CancellationToken.None);

        Assert.True(result);

        var view = await _db.ErrorGroupAdminsViews
            .FirstOrDefaultAsync(v => v.ErrorGroupId == _errorGroup.Id && v.AdminId == _admin.Id);
        Assert.NotNull(view);
    }

    [Fact]
    public async Task MarkErrorGroupAsViewed_Idempotent()
    {
        await _mutation.MarkErrorGroupAsViewed(
            _errorGroup.Id,
            MakePrincipal("err-admin"), _authz, _db, CancellationToken.None);

        // Call again — should not create duplicate
        await _mutation.MarkErrorGroupAsViewed(
            _errorGroup.Id,
            MakePrincipal("err-admin"), _authz, _db, CancellationToken.None);

        var count = await _db.ErrorGroupAdminsViews
            .CountAsync(v => v.ErrorGroupId == _errorGroup.Id && v.AdminId == _admin.Id);
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task MarkErrorGroupAsViewed_NonexistentGroup_Throws()
    {
        await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.MarkErrorGroupAsViewed(
                99999, MakePrincipal("err-admin"), _authz, _db, CancellationToken.None));
    }

    [Fact]
    public async Task MarkErrorGroupAsViewed_MultipleAdmins()
    {
        var admin2 = new Admin { Uid = "err-admin2", Email = "err2@test.com" };
        _db.Admins.Add(admin2);
        _db.SaveChanges();
        _db.WorkspaceAdmins.Add(new WorkspaceAdmin
        {
            AdminId = admin2.Id, WorkspaceId = _workspace.Id, Role = "MEMBER",
        });
        _db.SaveChanges();

        await _mutation.MarkErrorGroupAsViewed(
            _errorGroup.Id,
            MakePrincipal("err-admin"), _authz, _db, CancellationToken.None);
        await _mutation.MarkErrorGroupAsViewed(
            _errorGroup.Id,
            MakePrincipal("err-admin2"), _authz, _db, CancellationToken.None);

        var count = await _db.ErrorGroupAdminsViews
            .CountAsync(v => v.ErrorGroupId == _errorGroup.Id);
        Assert.Equal(2, count);
    }

    // ── CreateErrorTag ──────────────────────────────────────────────

    [Fact]
    public async Task CreateErrorTag_CreatesTag()
    {
        var result = await _mutation.CreateErrorTag(
            _errorGroup.Id, "Critical", "Needs immediate fix",
            MakePrincipal("err-admin"), _authz, _db, CancellationToken.None);

        Assert.Equal("Critical", result.Title);
        Assert.Equal("Needs immediate fix", result.Description);
        Assert.Equal(_errorGroup.Id, result.ErrorGroupId);
    }

    [Fact]
    public async Task CreateErrorTag_NullDescription()
    {
        var result = await _mutation.CreateErrorTag(
            _errorGroup.Id, "Bug", null,
            MakePrincipal("err-admin"), _authz, _db, CancellationToken.None);

        Assert.Null(result.Description);
    }

    [Fact]
    public async Task CreateErrorTag_MultipleTags_SameGroup()
    {
        await _mutation.CreateErrorTag(
            _errorGroup.Id, "Tag1", null,
            MakePrincipal("err-admin"), _authz, _db, CancellationToken.None);
        await _mutation.CreateErrorTag(
            _errorGroup.Id, "Tag2", null,
            MakePrincipal("err-admin"), _authz, _db, CancellationToken.None);

        var count = await _db.ErrorTags.CountAsync(t => t.ErrorGroupId == _errorGroup.Id);
        Assert.Equal(2, count);
    }

    [Fact]
    public async Task CreateErrorTag_NonexistentGroup_Throws()
    {
        await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.CreateErrorTag(
                99999, "Tag", null,
                MakePrincipal("err-admin"), _authz, _db, CancellationToken.None));
    }

    // ── CreateErrorComment ──────────────────────────────────────────

    [Fact]
    public async Task CreateErrorComment_CreatesComment()
    {
        var result = await _mutation.CreateErrorComment(
            _errorGroup.Id, "This needs investigation",
            MakePrincipal("err-admin"), _authz, _db, CancellationToken.None);

        Assert.Equal("This needs investigation", result.Text);
        Assert.Equal(_admin.Id, result.AdminId);
        Assert.Equal(_errorGroup.Id, result.ErrorGroupId);
    }

    [Fact]
    public async Task CreateErrorComment_EmptyText()
    {
        var result = await _mutation.CreateErrorComment(
            _errorGroup.Id, "",
            MakePrincipal("err-admin"), _authz, _db, CancellationToken.None);

        Assert.Equal("", result.Text);
    }

    [Fact]
    public async Task CreateErrorComment_NonexistentGroup_Throws()
    {
        await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.CreateErrorComment(
                99999, "Text",
                MakePrincipal("err-admin"), _authz, _db, CancellationToken.None));
    }

    [Fact]
    public async Task CreateErrorComment_MultipleComments()
    {
        await _mutation.CreateErrorComment(
            _errorGroup.Id, "First",
            MakePrincipal("err-admin"), _authz, _db, CancellationToken.None);
        await _mutation.CreateErrorComment(
            _errorGroup.Id, "Second",
            MakePrincipal("err-admin"), _authz, _db, CancellationToken.None);

        var count = await _db.ErrorComments.CountAsync(c => c.ErrorGroupId == _errorGroup.Id);
        Assert.Equal(2, count);
    }

    // ── DeleteErrorComment ──────────────────────────────────────────

    [Fact]
    public async Task DeleteErrorComment_RemovesComment()
    {
        var comment = new ErrorComment
        {
            ErrorGroupId = _errorGroup.Id, AdminId = _admin.Id, Text = "To delete",
        };
        _db.ErrorComments.Add(comment);
        _db.SaveChanges();

        var result = await _mutation.DeleteErrorComment(
            comment.Id, MakePrincipal("err-admin"), _authz, _db, CancellationToken.None);

        Assert.True(result);
        Assert.Null(await _db.ErrorComments.FindAsync(comment.Id));
    }

    [Fact]
    public async Task DeleteErrorComment_NonexistentComment_Throws()
    {
        await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.DeleteErrorComment(
                99999, MakePrincipal("err-admin"), _authz, _db, CancellationToken.None));
    }

    // ── UpdateSessionIsPublic ───────────────────────────────────────

    [Fact]
    public async Task UpdateSessionIsPublic_SetsStarred()
    {
        var session = new Session { SecureId = "public-sess", ProjectId = _project.Id };
        _db.Sessions.Add(session);
        _db.SaveChanges();

        var result = await _mutation.UpdateSessionIsPublic(
            "public-sess", true,
            MakePrincipal("err-admin"), _authz, _db, CancellationToken.None);

        Assert.True(result.Starred);
    }

    [Fact]
    public async Task UpdateSessionIsPublic_UnsetsStarred()
    {
        var session = new Session { SecureId = "unstar-sess", ProjectId = _project.Id, Starred = true };
        _db.Sessions.Add(session);
        _db.SaveChanges();

        var result = await _mutation.UpdateSessionIsPublic(
            "unstar-sess", false,
            MakePrincipal("err-admin"), _authz, _db, CancellationToken.None);

        Assert.False(result.Starred);
    }

    [Fact]
    public async Task UpdateSessionIsPublic_NonexistentSession_Throws()
    {
        await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.UpdateSessionIsPublic(
                "no-such-sess", true,
                MakePrincipal("err-admin"), _authz, _db, CancellationToken.None));
    }

    [Fact]
    public async Task UpdateSessionIsPublic_Unauthenticated_Throws()
    {
        var session = new Session { SecureId = "auth-sess", ProjectId = _project.Id };
        _db.Sessions.Add(session);
        _db.SaveChanges();

        await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.UpdateSessionIsPublic(
                "auth-sess", true,
                AnonymousPrincipal, _authz, _db, CancellationToken.None));
    }
}
