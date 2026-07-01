using System.Net;
using System.Net.Http.Json;
using Andy.Rbac.Api.Controllers;
using Andy.Rbac.Api.Services;
using Andy.Rbac.Models;
using FluentAssertions;
using Xunit;

namespace Andy.Rbac.Api.Tests.Integration;

/// <summary>
/// End-to-end regression tests for role assignment ignoring applicationCode.
/// Role codes repeat across applications (one "admin" per andy-* service);
/// the assign/revoke endpoints must resolve (roleCode, applicationCode)
/// together, and must 400 — never silently pick — when a bare role code is
/// ambiguous across applications.
///
/// Uses its own factory instance (per-class fixture) so the extra
/// duplicate-coded roles created here can't leak into other test classes.
/// </summary>
public class RoleAssignmentApplicationScopeTests : IClassFixture<RbacWebApplicationFactory>
{
    private readonly HttpClient _client;

    public RoleAssignmentApplicationScopeTests(RbacWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    // ---- helpers -----------------------------------------------------

    private async Task CreateApplicationAsync(string code)
    {
        var response = await _client.PostAsJsonAsync(
            "/api/applications", new CreateApplicationRequest(code, $"App {code}"));
        response.IsSuccessStatusCode.Should().BeTrue($"seeding application '{code}' must succeed");
    }

    private async Task CreateRoleAsync(string roleCode, string applicationCode)
    {
        var response = await _client.PostAsJsonAsync(
            "/api/roles", new CreateRoleRequest(roleCode, $"Role {roleCode}", null, applicationCode));
        response.IsSuccessStatusCode.Should().BeTrue(
            $"seeding role '{roleCode}' in '{applicationCode}' must succeed");
    }

    private async Task<Guid> ProvisionSubjectAsync(string externalId)
    {
        var response = await _client.PostAsJsonAsync(
            "/api/subjects",
            new ProvisionSubjectRequest(externalId, "test-provider", SubjectType.User));
        response.IsSuccessStatusCode.Should().BeTrue($"provisioning subject '{externalId}' must succeed");
        var subject = await response.Content.ReadFromJsonAsync<SubjectDto>(TestJsonOptions.Default);
        return subject!.Id;
    }

    private async Task<SubjectDetailDto> GetSubjectAsync(Guid id)
    {
        var response = await _client.GetAsync($"/api/subjects/{id}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<SubjectDetailDto>(TestJsonOptions.Default))!;
    }

    /// <summary>Creates two applications that both define the same role code.</summary>
    private async Task SeedDuplicateRoleAsync(string roleCode, string appA, string appB)
    {
        await CreateApplicationAsync(appA);
        await CreateApplicationAsync(appB);
        await CreateRoleAsync(roleCode, appA);
        await CreateRoleAsync(roleCode, appB);
    }

    // ---- POST /api/subjects/{id}/roles -------------------------------

    [Fact]
    public async Task AssignRole_WithApplicationCode_BindsRoleFromRequestedApplication()
    {
        // Arrange — "deployer" exists in scope-app-a AND scope-app-b.
        await SeedDuplicateRoleAsync("deployer", "scope-app-a", "scope-app-b");
        var subjectId = await ProvisionSubjectAsync("scope-user-1");

        // Act — this is the production repro: the caller names app-b explicitly.
        var response = await _client.PostAsJsonAsync(
            $"/api/subjects/{subjectId}/roles",
            new SubjectAssignRoleRequest("deployer", ApplicationCode: "scope-app-b"));

        // Assert — bound to scope-app-b's role, not scope-app-a's.
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var subject = await GetSubjectAsync(subjectId);
        var role = subject.Roles.Should().ContainSingle(r => r.RoleCode == "deployer").Subject;
        role.ApplicationCode.Should().Be("scope-app-b",
            "the assignment must honour the requested applicationCode, not whichever role matched the code first");
    }

    [Fact]
    public async Task AssignRole_WithAmbiguousCodeAndNoApplicationCode_ReturnsBadRequestListingCandidates()
    {
        // Arrange — "auditor" exists in two applications.
        await SeedDuplicateRoleAsync("auditor", "ambig-app-a", "ambig-app-b");
        var subjectId = await ProvisionSubjectAsync("scope-user-2");

        // Act — no applicationCode given for an ambiguous code.
        var response = await _client.PostAsJsonAsync(
            $"/api/subjects/{subjectId}/roles",
            new SubjectAssignRoleRequest("auditor"));

        // Assert — 400 naming the candidate applications; nothing assigned.
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("ambig-app-a");
        body.Should().Contain("ambig-app-b");

        var subject = await GetSubjectAsync(subjectId);
        subject.Roles.Should().BeEmpty("an ambiguous request must never silently pick an application's role");
    }

    // ---- DELETE /api/subjects/{id}/roles/{roleCode} -------------------

    [Fact]
    public async Task RevokeRole_WithApplicationCode_RevokesOnlyRequestedApplicationsRole()
    {
        // Arrange — subject holds "releaser" from BOTH applications.
        await SeedDuplicateRoleAsync("releaser", "rev-app-a", "rev-app-b");
        var subjectId = await ProvisionSubjectAsync("scope-user-3");
        foreach (var app in new[] { "rev-app-a", "rev-app-b" })
        {
            var assign = await _client.PostAsJsonAsync(
                $"/api/subjects/{subjectId}/roles",
                new SubjectAssignRoleRequest("releaser", ApplicationCode: app));
            assign.StatusCode.Should().Be(HttpStatusCode.Created);
        }

        // Act — revoke only rev-app-b's role.
        var response = await _client.DeleteAsync(
            $"/api/subjects/{subjectId}/roles/releaser?applicationCode=rev-app-b");

        // Assert — rev-app-a's assignment survives.
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var subject = await GetSubjectAsync(subjectId);
        var remaining = subject.Roles.Should().ContainSingle(r => r.RoleCode == "releaser").Subject;
        remaining.ApplicationCode.Should().Be("rev-app-a");
    }

    [Fact]
    public async Task RevokeRole_WithAmbiguousCodeAndNoApplicationCode_ReturnsBadRequest()
    {
        // Arrange
        await SeedDuplicateRoleAsync("archiver", "revamb-app-a", "revamb-app-b");
        var subjectId = await ProvisionSubjectAsync("scope-user-4");
        var assign = await _client.PostAsJsonAsync(
            $"/api/subjects/{subjectId}/roles",
            new SubjectAssignRoleRequest("archiver", ApplicationCode: "revamb-app-a"));
        assign.StatusCode.Should().Be(HttpStatusCode.Created);

        // Act — bare ambiguous code.
        var response = await _client.DeleteAsync($"/api/subjects/{subjectId}/roles/archiver");

        // Assert — refused; the assignment is untouched.
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var subject = await GetSubjectAsync(subjectId);
        subject.Roles.Should().ContainSingle(r => r.RoleCode == "archiver");
    }

    // ---- POST /api/roles/assign + /api/roles/revoke -------------------

    [Fact]
    public async Task RolesAssignEndpoint_WithApplicationCode_BindsRoleFromRequestedApplication()
    {
        // Arrange — "operator" exists in svc-app-a AND svc-app-b.
        await SeedDuplicateRoleAsync("operator", "svc-app-a", "svc-app-b");
        var subjectId = await ProvisionSubjectAsync("scope-user-5");

        // Act
        var response = await _client.PostAsJsonAsync(
            "/api/roles/assign",
            new AssignRoleRequest("scope-user-5", "operator", ApplicationCode: "svc-app-b"));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var subject = await GetSubjectAsync(subjectId);
        var role = subject.Roles.Should().ContainSingle(r => r.RoleCode == "operator").Subject;
        role.ApplicationCode.Should().Be("svc-app-b");
    }

    [Fact]
    public async Task RolesAssignEndpoint_WithAmbiguousCodeAndNoApplicationCode_ReturnsBadRequest()
    {
        // Arrange
        await SeedDuplicateRoleAsync("inspector", "insp-app-a", "insp-app-b");
        var subjectId = await ProvisionSubjectAsync("scope-user-6");

        // Act
        var response = await _client.PostAsJsonAsync(
            "/api/roles/assign",
            new AssignRoleRequest("scope-user-6", "inspector"));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("insp-app-a");
        body.Should().Contain("insp-app-b");

        var subject = await GetSubjectAsync(subjectId);
        subject.Roles.Should().BeEmpty();
    }

    [Fact]
    public async Task RolesRevokeEndpoint_WithApplicationCode_RevokesOnlyRequestedApplicationsRole()
    {
        // Arrange — subject holds "publisher" from BOTH applications.
        await SeedDuplicateRoleAsync("publisher", "pub-app-a", "pub-app-b");
        var subjectId = await ProvisionSubjectAsync("scope-user-7");
        foreach (var app in new[] { "pub-app-a", "pub-app-b" })
        {
            var assign = await _client.PostAsJsonAsync(
                "/api/roles/assign",
                new AssignRoleRequest("scope-user-7", "publisher", ApplicationCode: app));
            assign.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        // Act
        var response = await _client.PostAsJsonAsync(
            "/api/roles/revoke",
            new AssignRoleRequest("scope-user-7", "publisher", ApplicationCode: "pub-app-b"));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var subject = await GetSubjectAsync(subjectId);
        var remaining = subject.Roles.Should().ContainSingle(r => r.RoleCode == "publisher").Subject;
        remaining.ApplicationCode.Should().Be("pub-app-a");
    }
}
