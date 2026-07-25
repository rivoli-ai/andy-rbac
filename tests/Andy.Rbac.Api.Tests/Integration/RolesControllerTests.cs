using System.Net;
using System.Net.Http.Json;
using Andy.Rbac.Api.Controllers;
using Andy.Rbac.Api.Services;
using FluentAssertions;
using Xunit;

namespace Andy.Rbac.Api.Tests.Integration;

public class RolesControllerTests : IClassFixture<RbacWebApplicationFactory>
{
    private readonly HttpClient _client;

    public RolesControllerTests(RbacWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetRoles_ReturnsRoleList()
    {
        // Act
        var response = await _client.GetAsync("/api/roles");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var roles = await response.Content.ReadFromJsonAsync<List<RoleDetail>>(TestJsonOptions.Default);
        roles.Should().NotBeNull();
        roles.Should().Contain(r => r.Code == "admin");
        roles.Should().Contain(r => r.Code == "editor");
        roles.Should().Contain(r => r.Code == "viewer");
    }

    [Fact]
    public async Task GetRoles_WithApplicationFilter_ReturnsFilteredRoles()
    {
        // Act
        var response = await _client.GetAsync("/api/roles?applicationCode=test-app");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var roles = await response.Content.ReadFromJsonAsync<List<RoleDetail>>(TestJsonOptions.Default);
        roles.Should().NotBeNull();
        roles.Should().NotBeEmpty();
    }

    [Fact]
    public async Task GetRole_WithValidId_ReturnsRole()
    {
        // Arrange
        var adminRoleId = Guid.Parse("55555555-5555-5555-5555-555555555555");

        // Act
        var response = await _client.GetAsync($"/api/roles/{adminRoleId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var role = await response.Content.ReadFromJsonAsync<RoleDetail>(TestJsonOptions.Default);
        role.Should().NotBeNull();
        role!.Code.Should().Be("admin");
        role.IsSystem.Should().BeTrue();
    }

    [Fact]
    public async Task GetRole_WithInvalidId_ReturnsNotFound()
    {
        // Act
        var response = await _client.GetAsync($"/api/roles/{Guid.NewGuid()}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetRoleByCode_WithValidScopedCode_ReturnsRole()
    {
        // "admin" exists in many seeded applications, so it must be scoped.
        // This assertion previously used the bare code and passed only because
        // the lookup silently bound whichever row matched first — the same
        // defect #86 fixed for assign/revoke.
        var response = await _client.GetAsync("/api/roles/by-code/admin?applicationCode=test-app");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var role = await response.Content.ReadFromJsonAsync<RoleDetail>(TestJsonOptions.Default);
        role.Should().NotBeNull();
        role!.Code.Should().Be("admin");
        role.ApplicationCode.Should().Be("test-app");
    }

    [Fact]
    public async Task GetRoleByCode_WithAmbiguousBareCode_ReturnsBadRequest()
    {
        // Act — "admin" is seeded for several applications.
        var response = await _client.GetAsync("/api/roles/by-code/admin");

        // Assert — must not silently bind an arbitrary application's role.
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("ambiguous");
    }

    [Fact]
    public async Task GetRoleByCode_WithInvalidCode_ReturnsNotFound()
    {
        // Act
        var response = await _client.GetAsync("/api/roles/by-code/non-existent");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task CreateRole_WithValidData_ReturnsCreated()
    {
        // Arrange
        var request = new CreateRoleRequest($"new-role-{Guid.NewGuid():N}", "New Role", "A test role", "test-app");

        // Act
        var response = await _client.PostAsJsonAsync("/api/roles", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var role = await response.Content.ReadFromJsonAsync<RoleDetail>(TestJsonOptions.Default);
        role.Should().NotBeNull();
        role!.Code.Should().Be(request.Code);
        role.IsSystem.Should().BeFalse();
    }

    [Fact]
    public async Task CreateRole_WithInvalidApplication_ReturnsBadRequest()
    {
        // Arrange
        var request = new CreateRoleRequest("new-role", "New Role", null, "non-existent-app");

        // Act
        var response = await _client.PostAsJsonAsync("/api/roles", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task DeleteRole_WithSystemRole_ReturnsBadRequest()
    {
        // Arrange - admin role is a system role
        var adminRoleId = Guid.Parse("55555555-5555-5555-5555-555555555555");

        // Act
        var response = await _client.DeleteAsync($"/api/roles/{adminRoleId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task DeleteRole_WithNonSystemRole_ReturnsNoContent()
    {
        // Arrange - create a non-system role to delete
        var createRequest = new CreateRoleRequest($"delete-role-{Guid.NewGuid():N}", "Delete Me", null, "test-app");
        var createResponse = await _client.PostAsJsonAsync("/api/roles", createRequest);
        var created = await createResponse.Content.ReadFromJsonAsync<RoleDetail>(TestJsonOptions.Default);

        // Act
        var response = await _client.DeleteAsync($"/api/roles/{created!.Id}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task AssignRole_WithValidData_ReturnsOk()
    {
        // Arrange
        var request = new AssignRoleRequest("no-role-user", "editor");

        // Act
        var response = await _client.PostAsJsonAsync("/api/roles/assign", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadAsStringAsync();
        result.Should().Contain("Successfully assigned");
    }

    [Fact]
    public async Task AssignRole_WithInvalidSubject_ReturnsBadRequest()
    {
        // Arrange
        var request = new AssignRoleRequest("non-existent-user", "admin");

        // Act
        var response = await _client.PostAsJsonAsync("/api/roles/assign", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task AssignRole_WithInvalidRole_ReturnsBadRequest()
    {
        // Arrange
        var request = new AssignRoleRequest("admin-user", "non-existent-role");

        // Act
        var response = await _client.PostAsJsonAsync("/api/roles/assign", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task RevokeRole_WithValidData_ReturnsOk()
    {
        // Arrange - admin-user has test-app's admin role. The production
        // seeder also creates "admin" roles in other applications, so the
        // code must be scoped — a bare ambiguous code is a 400 by design.
        var request = new AssignRoleRequest("admin-user", "admin", ApplicationCode: "test-app");

        // Act
        var response = await _client.PostAsJsonAsync("/api/roles/revoke", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadAsStringAsync();
        result.Should().Contain("Successfully revoked");
    }

    [Fact]
    public async Task RevokeRole_WithNoAssignment_ReturnsOk()
    {
        // Arrange - viewer-user doesn't have test-app's admin role
        var request = new AssignRoleRequest("viewer-user", "admin", ApplicationCode: "test-app");

        // Act
        var response = await _client.PostAsJsonAsync("/api/roles/revoke", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadAsStringAsync();
        result.Should().Contain("not assigned");
    }
}
