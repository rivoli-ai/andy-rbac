using System.Net;
using System.Net.Http.Json;
using Andy.Rbac.Api.Controllers;
using FluentAssertions;
using Xunit;

namespace Andy.Rbac.Api.Tests.Integration;

public class CheckControllerTests : IClassFixture<RbacWebApplicationFactory>
{
    private readonly HttpClient _client;

    public CheckControllerTests(RbacWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task CheckPermission_WithValidPermission_ReturnsAllowed()
    {
        // Arrange - admin-user has admin role with document:read permission
        var request = new CheckPermissionRequest("admin-user", "test-app:document:read");

        // Act
        var response = await _client.PostAsJsonAsync("/api/check", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<CheckPermissionResponse>();
        result.Should().NotBeNull();
        result!.Allowed.Should().BeTrue();
    }

    [Fact]
    public async Task CheckPermission_WithInvalidPermission_ReturnsDenied()
    {
        // Arrange - viewer-user only has document:read, not document:delete
        var request = new CheckPermissionRequest("viewer-user", "test-app:document:delete");

        // Act
        var response = await _client.PostAsJsonAsync("/api/check", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<CheckPermissionResponse>();
        result.Should().NotBeNull();
        result!.Allowed.Should().BeFalse();
    }

    [Fact]
    public async Task CheckPermission_WithNonExistentSubject_ReturnsDenied()
    {
        // Arrange
        var request = new CheckPermissionRequest("non-existent-user", "test-app:document:read");

        // Act
        var response = await _client.PostAsJsonAsync("/api/check", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<CheckPermissionResponse>();
        result.Should().NotBeNull();
        result!.Allowed.Should().BeFalse();
        result.Reason.Should().Be("Subject not found");
    }

    [Fact]
    public async Task CheckAnyPermission_WithOneMatching_ReturnsAllowed()
    {
        // Arrange - viewer-user has document:read but not document:write or document:delete
        var request = new CheckAnyPermissionRequest("viewer-user", ["test-app:document:delete", "test-app:document:read"]);

        // Act
        var response = await _client.PostAsJsonAsync("/api/check/any", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<CheckPermissionResponse>();
        result.Should().NotBeNull();
        result!.Allowed.Should().BeTrue();
    }

    [Fact]
    public async Task CheckAnyPermission_WithNoneMatching_ReturnsDenied()
    {
        // Arrange - viewer-user doesn't have write or delete
        var request = new CheckAnyPermissionRequest("viewer-user", ["test-app:document:write", "test-app:document:delete"]);

        // Act
        var response = await _client.PostAsJsonAsync("/api/check/any", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<CheckPermissionResponse>();
        result.Should().NotBeNull();
        result!.Allowed.Should().BeFalse();
    }

    [Fact]
    public async Task GetPermissions_WithValidSubject_ReturnsPermissions()
    {
        // Act
        var response = await _client.GetAsync("/api/check/permissions/admin-user");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<GetPermissionsResponse>();
        result.Should().NotBeNull();
        result!.Permissions.Should().NotBeEmpty();
        result.Permissions.Should().Contain(p => p.Contains("document:read"));
    }

    [Fact]
    public async Task GetPermissions_WithApplicationFilter_ReturnsFilteredPermissions()
    {
        // Act
        var response = await _client.GetAsync("/api/check/permissions/admin-user?applicationCode=test-app");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<GetPermissionsResponse>();
        result.Should().NotBeNull();
        result!.Permissions.Should().OnlyContain(p => p.StartsWith("test-app:"));
    }

    [Fact]
    public async Task GetPermissions_WithNonExistentSubject_ReturnsEmptyList()
    {
        // Act
        var response = await _client.GetAsync("/api/check/permissions/non-existent-user");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<GetPermissionsResponse>();
        result.Should().NotBeNull();
        result!.Permissions.Should().BeEmpty();
    }

    [Fact]
    public async Task GetRoles_WithValidSubject_ReturnsRoles()
    {
        // Act
        var response = await _client.GetAsync("/api/check/roles/admin-user");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<GetRolesResponse>();
        result.Should().NotBeNull();
        result!.Roles.Should().Contain("admin");
    }

    [Fact]
    public async Task GetRoles_WithNoRoles_ReturnsEmptyList()
    {
        // Act
        var response = await _client.GetAsync("/api/check/roles/no-role-user");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<GetRolesResponse>();
        result.Should().NotBeNull();
        result!.Roles.Should().BeEmpty();
    }

    [Fact]
    public async Task GetRoles_WithNonExistentSubject_ReturnsEmptyList()
    {
        // Act
        var response = await _client.GetAsync("/api/check/roles/non-existent-user");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<GetRolesResponse>();
        result.Should().NotBeNull();
        result!.Roles.Should().BeEmpty();
    }
}
