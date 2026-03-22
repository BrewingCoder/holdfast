using System.Security.Claims;
using HoldFast.Data;
using HoldFast.Domain.Entities;
using HoldFast.GraphQL.Private;
using HoldFast.Shared.Auth;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HoldFast.GraphQL.Tests;

/// <summary>
/// Tests for PrivateQuery.GetErrorsKeys — pure logic that filters reserved error keys.
/// </summary>
public class PrivateQueryErrorKeysTests
{
    private readonly PrivateQuery _query = new();
    private readonly ClaimsPrincipal _principal;
    private readonly StubAuthorizationService _authz;

    public PrivateQueryErrorKeysTests()
    {
        var identity = new ClaimsIdentity(
        [
            new Claim(HoldFastClaimTypes.Uid, "test-uid"),
            new Claim(HoldFastClaimTypes.AdminId, "1"),
        ], "test");
        _principal = new ClaimsPrincipal(identity);
        _authz = new StubAuthorizationService();
    }

    [Fact]
    public async Task GetErrorsKeys_NoQuery_ReturnsAllKeys()
    {
        var keys = await _query.GetErrorsKeys(1, null, _principal, _authz, CancellationToken.None);
        Assert.Equal(11, keys.Count);
        Assert.All(keys, k => Assert.Equal("String", k.Type));
    }

    [Fact]
    public async Task GetErrorsKeys_EmptyQuery_ReturnsAllKeys()
    {
        var keys = await _query.GetErrorsKeys(1, "", _principal, _authz, CancellationToken.None);
        Assert.Equal(11, keys.Count);
    }

    [Fact]
    public async Task GetErrorsKeys_FilterByPartialName()
    {
        var keys = await _query.GetErrorsKeys(1, "service", _principal, _authz, CancellationToken.None);
        Assert.Equal(2, keys.Count); // service_name, service_version
        Assert.Contains(keys, k => k.Name == "service_name");
        Assert.Contains(keys, k => k.Name == "service_version");
    }

    [Fact]
    public async Task GetErrorsKeys_FilterByExactName()
    {
        var keys = await _query.GetErrorsKeys(1, "browser", _principal, _authz, CancellationToken.None);
        Assert.Single(keys);
        Assert.Equal("browser", keys[0].Name);
    }

    [Fact]
    public async Task GetErrorsKeys_CaseInsensitive()
    {
        var keys = await _query.GetErrorsKeys(1, "BROWSER", _principal, _authz, CancellationToken.None);
        Assert.Single(keys);
        Assert.Equal("browser", keys[0].Name);
    }

    [Fact]
    public async Task GetErrorsKeys_NoMatch_ReturnsEmpty()
    {
        var keys = await _query.GetErrorsKeys(1, "nonexistent_key", _principal, _authz, CancellationToken.None);
        Assert.Empty(keys);
    }

    [Fact]
    public async Task GetErrorsKeys_ContainsExpectedKeys()
    {
        var keys = await _query.GetErrorsKeys(1, null, _principal, _authz, CancellationToken.None);
        var names = keys.Select(k => k.Name).ToArray();

        Assert.Contains("browser", names);
        Assert.Contains("environment", names);
        Assert.Contains("event", names);
        Assert.Contains("os_name", names);
        Assert.Contains("service_name", names);
        Assert.Contains("service_version", names);
        Assert.Contains("secure_session_id", names);
        Assert.Contains("status", names);
        Assert.Contains("tag", names);
        Assert.Contains("type", names);
        Assert.Contains("visited_url", names);
    }

    [Fact]
    public async Task GetErrorsKeys_FilterByUnderscorePrefix()
    {
        var keys = await _query.GetErrorsKeys(1, "os", _principal, _authz, CancellationToken.None);
        Assert.Single(keys);
        Assert.Equal("os_name", keys[0].Name);
    }

    [Fact]
    public async Task GetErrorsKeys_FilterBySingleChar()
    {
        var keys = await _query.GetErrorsKeys(1, "t", _principal, _authz, CancellationToken.None);
        // tag, type, status (contains 't'), visited_url (contains 't'), environment (contains 't')
        Assert.True(keys.Count > 0);
        Assert.All(keys, k => Assert.Contains("t", k.Name, StringComparison.OrdinalIgnoreCase));
    }

    // Minimal stub that returns a fixed admin for auth checks
    private class StubAuthorizationService : IAuthorizationService
    {
        public Task<Admin> GetCurrentAdminAsync(string uid, CancellationToken ct) =>
            Task.FromResult(new Admin { Id = 1, Uid = uid, Email = "test@test.com" });

        public Task<Workspace> IsAdminInWorkspaceAsync(int adminId, int workspaceId, CancellationToken ct) =>
            Task.FromResult(new Workspace { Id = workspaceId, Name = "Test" });

        public Task<Workspace> IsAdminInWorkspaceFullAccessAsync(int adminId, int workspaceId, CancellationToken ct) =>
            Task.FromResult(new Workspace { Id = workspaceId, Name = "Test" });

        public Task<Project> IsAdminInProjectAsync(int adminId, int projectId, CancellationToken ct) =>
            Task.FromResult(new Project { Id = projectId, Name = "Test", WorkspaceId = 1 });

        public Task<(string Role, List<int>? ProjectIds)?> GetAdminRoleAsync(int adminId, int workspaceId, CancellationToken ct) =>
            Task.FromResult<(string Role, List<int>? ProjectIds)?>(("ADMIN", null));

        public Task ValidateAdminRoleAsync(int adminId, int workspaceId, CancellationToken ct) =>
            Task.CompletedTask;
    }
}
