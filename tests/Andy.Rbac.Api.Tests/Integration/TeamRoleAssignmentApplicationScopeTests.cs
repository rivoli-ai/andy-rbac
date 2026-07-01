using System.Net;
using System.Net.Http.Json;
using Andy.Rbac.Api.Controllers;
using FluentAssertions;
using Xunit;

namespace Andy.Rbac.Api.Tests.Integration;

/// <summary>
/// End-to-end regression tests for TEAM role assignment ignoring
/// applicationCode — the Teams-path twin of
/// <see cref="RoleAssignmentApplicationScopeTests"/> (issue #86).
/// Role codes repeat across applications (one "admin" per andy-* service);
/// the team assign/revoke endpoints must resolve (roleCode, applicationCode)
/// together, and must 400 — never silently pick — when a bare role code is
/// ambiguous across applications.
///
/// Uses its own factory instance (per-class fixture) so the extra
/// duplicate-coded roles created here can't leak into other test classes.
/// </summary>
public class TeamRoleAssignmentApplicationScopeTests : IClassFixture<RbacWebApplicationFactory>
{
    private readonly HttpClient _client;

    public TeamRoleAssignmentApplicationScopeTests(RbacWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    // ---- helpers -----------------------------------------------------

    private async Task CreateApplicationAsync(string code)
    {
        var response = await _client.PostAsJsonAsync(
            "/api/applications",
            new Andy.Rbac.Api.Services.CreateApplicationRequest(code, $"App {code}"));
        response.IsSuccessStatusCode.Should().BeTrue($"seeding application '{code}' must succeed");
    }

    private async Task CreateRoleAsync(string roleCode, string applicationCode)
    {
        var response = await _client.PostAsJsonAsync(
            "/api/roles",
            new Andy.Rbac.Api.Services.CreateRoleRequest(roleCode, $"Role {roleCode}", null, applicationCode));
        response.IsSuccessStatusCode.Should().BeTrue(
            $"seeding role '{roleCode}' in '{applicationCode}' must succeed");
    }

    private async Task<Guid> CreateTeamAsync(string code)
    {
        var response = await _client.PostAsJsonAsync(
            "/api/teams", new CreateTeamRequest(code, $"Team {code}"));
        response.IsSuccessStatusCode.Should().BeTrue($"seeding team '{code}' must succeed");
        var team = await response.Content.ReadFromJsonAsync<TeamDto>(TestJsonOptions.Default);
        return team!.Id;
    }

    private async Task<TeamDetailDto> GetTeamAsync(Guid id)
    {
        var response = await _client.GetAsync($"/api/teams/{id}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<TeamDetailDto>(TestJsonOptions.Default))!;
    }

    /// <summary>Creates two applications that both define the same role code.</summary>
    private async Task SeedDuplicateRoleAsync(string roleCode, string appA, string appB)
    {
        await CreateApplicationAsync(appA);
        await CreateApplicationAsync(appB);
        await CreateRoleAsync(roleCode, appA);
        await CreateRoleAsync(roleCode, appB);
    }

    // ---- POST /api/teams/{id}/roles -----------------------------------

    [Fact]
    public async Task AssignTeamRole_WithApplicationCode_BindsRoleFromRequestedApplication()
    {
        // Arrange — "deployer" exists in tscope-app-a AND tscope-app-b.
        await SeedDuplicateRoleAsync("deployer", "tscope-app-a", "tscope-app-b");
        var teamId = await CreateTeamAsync("scope-team-1");

        // Act — this is the production repro: the caller names app-b explicitly.
        var response = await _client.PostAsJsonAsync(
            $"/api/teams/{teamId}/roles",
            new AssignTeamRoleRequest("deployer", ApplicationCode: "tscope-app-b"));

        // Assert — bound to tscope-app-b's role, not tscope-app-a's.
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var team = await GetTeamAsync(teamId);
        var role = team.Roles.Should().ContainSingle(r => r.RoleCode == "deployer").Subject;
        role.ApplicationCode.Should().Be("tscope-app-b",
            "the assignment must honour the requested applicationCode, not whichever role matched the code first");
    }

    [Fact]
    public async Task AssignTeamRole_WithAmbiguousCodeAndNoApplicationCode_ReturnsBadRequestListingCandidates()
    {
        // Arrange — "auditor" exists in two applications.
        await SeedDuplicateRoleAsync("auditor", "tambig-app-a", "tambig-app-b");
        var teamId = await CreateTeamAsync("scope-team-2");

        // Act — no applicationCode given for an ambiguous code.
        var response = await _client.PostAsJsonAsync(
            $"/api/teams/{teamId}/roles",
            new AssignTeamRoleRequest("auditor"));

        // Assert — 400 naming the candidate applications; nothing assigned.
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("tambig-app-a");
        body.Should().Contain("tambig-app-b");

        var team = await GetTeamAsync(teamId);
        team.Roles.Should().BeEmpty("an ambiguous request must never silently pick an application's role");
    }

    // ---- DELETE /api/teams/{id}/roles/{roleCode} -----------------------

    [Fact]
    public async Task RevokeTeamRole_WithApplicationCode_RevokesOnlyRequestedApplicationsRole()
    {
        // Arrange — team holds "releaser" from BOTH applications.
        await SeedDuplicateRoleAsync("releaser", "trev-app-a", "trev-app-b");
        var teamId = await CreateTeamAsync("scope-team-3");
        foreach (var app in new[] { "trev-app-a", "trev-app-b" })
        {
            var assign = await _client.PostAsJsonAsync(
                $"/api/teams/{teamId}/roles",
                new AssignTeamRoleRequest("releaser", ApplicationCode: app));
            assign.StatusCode.Should().Be(HttpStatusCode.Created);
        }

        // Act — revoke only trev-app-b's role.
        var response = await _client.DeleteAsync(
            $"/api/teams/{teamId}/roles/releaser?applicationCode=trev-app-b");

        // Assert — trev-app-a's assignment survives.
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var team = await GetTeamAsync(teamId);
        var remaining = team.Roles.Should().ContainSingle(r => r.RoleCode == "releaser").Subject;
        remaining.ApplicationCode.Should().Be("trev-app-a");
    }

    [Fact]
    public async Task RevokeTeamRole_WithAmbiguousCodeAndNoApplicationCode_ReturnsBadRequest()
    {
        // Arrange
        await SeedDuplicateRoleAsync("archiver", "trevamb-app-a", "trevamb-app-b");
        var teamId = await CreateTeamAsync("scope-team-4");
        var assign = await _client.PostAsJsonAsync(
            $"/api/teams/{teamId}/roles",
            new AssignTeamRoleRequest("archiver", ApplicationCode: "trevamb-app-a"));
        assign.StatusCode.Should().Be(HttpStatusCode.Created);

        // Act — bare ambiguous code.
        var response = await _client.DeleteAsync($"/api/teams/{teamId}/roles/archiver");

        // Assert — refused; the assignment is untouched.
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var team = await GetTeamAsync(teamId);
        team.Roles.Should().ContainSingle(r => r.RoleCode == "archiver");
    }
}
