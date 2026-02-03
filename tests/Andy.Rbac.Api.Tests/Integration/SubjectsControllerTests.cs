using System.Net;
using System.Net.Http.Json;
using Andy.Rbac.Api.Controllers;
using Andy.Rbac.Models;
using FluentAssertions;
using Xunit;

namespace Andy.Rbac.Api.Tests.Integration;

public class SubjectsControllerTests : IClassFixture<RbacWebApplicationFactory>
{
    private readonly HttpClient _client;

    public SubjectsControllerTests(RbacWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task SearchSubjects_WithNoQuery_ReturnsAllSubjects()
    {
        // Act
        var response = await _client.GetAsync("/api/subjects");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<PagedResult<SubjectDto>>();
        result.Should().NotBeNull();
        result!.Items.Should().NotBeEmpty();
        result.Items.Should().Contain(s => s.ExternalId == "admin-user");
    }

    [Fact]
    public async Task SearchSubjects_WithQuery_ReturnsMatchingSubjects()
    {
        // Act
        var response = await _client.GetAsync("/api/subjects?query=admin");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<PagedResult<SubjectDto>>();
        result.Should().NotBeNull();
        result!.Items.Should().Contain(s => s.ExternalId == "admin-user");
    }

    [Fact]
    public async Task SearchSubjects_WithProvider_ReturnsFilteredSubjects()
    {
        // Act
        var response = await _client.GetAsync("/api/subjects?provider=test-provider");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<PagedResult<SubjectDto>>();
        result.Should().NotBeNull();
        result!.Items.Should().OnlyContain(s => s.Provider == "test-provider");
    }

    [Fact]
    public async Task SearchSubjects_WithPagination_RespectsLimits()
    {
        // Act
        var response = await _client.GetAsync("/api/subjects?skip=0&take=2");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<PagedResult<SubjectDto>>();
        result.Should().NotBeNull();
        result!.Items.Should().HaveCountLessOrEqualTo(2);
        result.Skip.Should().Be(0);
        result.Take.Should().Be(2);
    }

    [Fact]
    public async Task GetSubject_WithValidId_ReturnsSubjectDetail()
    {
        // Arrange
        var adminId = Guid.Parse("66666666-6666-6666-6666-666666666666");

        // Act
        var response = await _client.GetAsync($"/api/subjects/{adminId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var subject = await response.Content.ReadFromJsonAsync<SubjectDetailDto>();
        subject.Should().NotBeNull();
        subject!.ExternalId.Should().Be("admin-user");
        subject.Roles.Should().Contain(r => r.RoleCode == "admin");
    }

    [Fact]
    public async Task GetSubject_WithInvalidId_ReturnsNotFound()
    {
        // Act
        var response = await _client.GetAsync($"/api/subjects/{Guid.NewGuid()}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetSubjectByExternalId_WithValidData_ReturnsSubject()
    {
        // Act
        var response = await _client.GetAsync("/api/subjects/by-external/test-provider/admin-user");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var subject = await response.Content.ReadFromJsonAsync<SubjectDetailDto>();
        subject.Should().NotBeNull();
        subject!.ExternalId.Should().Be("admin-user");
    }

    [Fact]
    public async Task GetSubjectByExternalId_WithInvalidData_ReturnsNotFound()
    {
        // Act
        var response = await _client.GetAsync("/api/subjects/by-external/test-provider/non-existent");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ProvisionSubject_NewSubject_ReturnsCreated()
    {
        // Arrange
        var request = new ProvisionSubjectRequest(
            $"new-user-{Guid.NewGuid():N}",
            "test-provider",
            SubjectType.User,
            "new@test.com",
            "New User");

        // Act
        var response = await _client.PostAsJsonAsync("/api/subjects", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var subject = await response.Content.ReadFromJsonAsync<SubjectDto>();
        subject.Should().NotBeNull();
        subject!.ExternalId.Should().Be(request.ExternalId);
        subject.Email.Should().Be("new@test.com");
    }

    [Fact]
    public async Task ProvisionSubject_ExistingSubject_ReturnsOkAndUpdates()
    {
        // Arrange - admin-user already exists
        var request = new ProvisionSubjectRequest(
            "admin-user",
            "test-provider",
            SubjectType.User,
            "updated-admin@test.com",
            "Updated Admin");

        // Act
        var response = await _client.PostAsJsonAsync("/api/subjects", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var subject = await response.Content.ReadFromJsonAsync<SubjectDto>();
        subject.Should().NotBeNull();
        subject!.Email.Should().Be("updated-admin@test.com");
    }

    [Fact]
    public async Task UpdateSubject_WithValidData_ReturnsOk()
    {
        // Arrange
        var adminId = Guid.Parse("66666666-6666-6666-6666-666666666666");
        var request = new UpdateSubjectRequest("new-email@test.com", "New Display Name");

        // Act
        var response = await _client.PutAsJsonAsync($"/api/subjects/{adminId}", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var subject = await response.Content.ReadFromJsonAsync<SubjectDto>();
        subject.Should().NotBeNull();
        subject!.Email.Should().Be("new-email@test.com");
    }

    [Fact]
    public async Task UpdateSubject_WithInvalidId_ReturnsNotFound()
    {
        // Arrange
        var request = new UpdateSubjectRequest("new@test.com");

        // Act
        var response = await _client.PutAsJsonAsync($"/api/subjects/{Guid.NewGuid()}", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeactivateSubject_WithValidId_ReturnsNoContent()
    {
        // Arrange
        var noRoleId = Guid.Parse("66666666-6666-6666-6666-666666666669");

        // Act
        var response = await _client.PostAsync($"/api/subjects/{noRoleId}/deactivate", null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verify deactivation
        var getResponse = await _client.GetAsync($"/api/subjects/{noRoleId}");
        var subject = await getResponse.Content.ReadFromJsonAsync<SubjectDetailDto>();
        subject!.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task DeactivateSubject_WithInvalidId_ReturnsNotFound()
    {
        // Act
        var response = await _client.PostAsync($"/api/subjects/{Guid.NewGuid()}/deactivate", null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task AssignRole_WithValidData_ReturnsCreated()
    {
        // Arrange
        var noRoleId = Guid.Parse("66666666-6666-6666-6666-666666666669");
        var request = new SubjectAssignRoleRequest("viewer");

        // Act
        var response = await _client.PostAsJsonAsync($"/api/subjects/{noRoleId}/roles", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task AssignRole_WithInvalidSubject_ReturnsNotFound()
    {
        // Arrange
        var request = new SubjectAssignRoleRequest("admin");

        // Act
        var response = await _client.PostAsJsonAsync($"/api/subjects/{Guid.NewGuid()}/roles", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task AssignRole_WithInvalidRole_ReturnsNotFound()
    {
        // Arrange
        var adminId = Guid.Parse("66666666-6666-6666-6666-666666666666");
        var request = new SubjectAssignRoleRequest("non-existent-role");

        // Act
        var response = await _client.PostAsJsonAsync($"/api/subjects/{adminId}/roles", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task RevokeRole_WithValidData_ReturnsNoContent()
    {
        // Arrange - editor-user has editor role
        var editorId = Guid.Parse("66666666-6666-6666-6666-666666666667");

        // Act
        var response = await _client.DeleteAsync($"/api/subjects/{editorId}/roles/editor");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task RevokeRole_WithInvalidRole_ReturnsNotFound()
    {
        // Arrange
        var adminId = Guid.Parse("66666666-6666-6666-6666-666666666666");

        // Act
        var response = await _client.DeleteAsync($"/api/subjects/{adminId}/roles/non-existent-role");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
