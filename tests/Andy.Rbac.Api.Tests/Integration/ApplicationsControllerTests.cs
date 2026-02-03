using System.Net;
using System.Net.Http.Json;
using Andy.Rbac.Api.Services;
using FluentAssertions;
using Xunit;

namespace Andy.Rbac.Api.Tests.Integration;

public class ApplicationsControllerTests : IClassFixture<RbacWebApplicationFactory>
{
    private readonly HttpClient _client;

    public ApplicationsControllerTests(RbacWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetApplications_ReturnsApplicationList()
    {
        // Act
        var response = await _client.GetAsync("/api/applications");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var applications = await response.Content.ReadFromJsonAsync<List<ApplicationSummary>>();
        applications.Should().NotBeNull();
        applications.Should().Contain(a => a.Code == "test-app");
    }

    [Fact]
    public async Task GetApplication_WithValidId_ReturnsApplication()
    {
        // Arrange
        var appId = Guid.Parse("11111111-1111-1111-1111-111111111111");

        // Act
        var response = await _client.GetAsync($"/api/applications/{appId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var application = await response.Content.ReadFromJsonAsync<ApplicationDetail>();
        application.Should().NotBeNull();
        application!.Code.Should().Be("test-app");
        application.Name.Should().Be("Test Application");
    }

    [Fact]
    public async Task GetApplication_WithInvalidId_ReturnsNotFound()
    {
        // Act
        var response = await _client.GetAsync($"/api/applications/{Guid.NewGuid()}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetApplicationByCode_WithValidCode_ReturnsApplication()
    {
        // Act
        var response = await _client.GetAsync("/api/applications/by-code/test-app");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var application = await response.Content.ReadFromJsonAsync<ApplicationDetail>();
        application.Should().NotBeNull();
        application!.Code.Should().Be("test-app");
    }

    [Fact]
    public async Task GetApplicationByCode_WithInvalidCode_ReturnsNotFound()
    {
        // Act
        var response = await _client.GetAsync("/api/applications/by-code/non-existent");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task CreateApplication_WithValidData_ReturnsCreated()
    {
        // Arrange
        var request = new CreateApplicationRequest($"new-app-{Guid.NewGuid():N}", "New Application", "A test application");

        // Act
        var response = await _client.PostAsJsonAsync("/api/applications", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var application = await response.Content.ReadFromJsonAsync<ApplicationDetail>();
        application.Should().NotBeNull();
        application!.Code.Should().Be(request.Code);
        application.Name.Should().Be("New Application");
    }

    [Fact]
    public async Task CreateApplication_WithDuplicateCode_ReturnsBadRequest()
    {
        // Arrange
        var request = new CreateApplicationRequest("test-app", "Duplicate App");

        // Act
        var response = await _client.PostAsJsonAsync("/api/applications", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UpdateApplication_WithValidData_ReturnsOk()
    {
        // Arrange
        var appId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var request = new UpdateApplicationRequest("Updated Test Application", "Updated description");

        // Act
        var response = await _client.PutAsJsonAsync($"/api/applications/{appId}", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var application = await response.Content.ReadFromJsonAsync<ApplicationDetail>();
        application.Should().NotBeNull();
        application!.Name.Should().Be("Updated Test Application");
    }

    [Fact]
    public async Task UpdateApplication_WithInvalidId_ReturnsNotFound()
    {
        // Arrange
        var request = new UpdateApplicationRequest("Updated Name");

        // Act
        var response = await _client.PutAsJsonAsync($"/api/applications/{Guid.NewGuid()}", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteApplication_WithValidId_ReturnsNoContent()
    {
        // Arrange - create an app to delete
        var createRequest = new CreateApplicationRequest($"delete-app-{Guid.NewGuid():N}", "Delete Me");
        var createResponse = await _client.PostAsJsonAsync("/api/applications", createRequest);
        var created = await createResponse.Content.ReadFromJsonAsync<ApplicationDetail>();

        // Act
        var response = await _client.DeleteAsync($"/api/applications/{created!.Id}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verify deletion
        var getResponse = await _client.GetAsync($"/api/applications/{created.Id}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteApplication_WithInvalidId_ReturnsNotFound()
    {
        // Act
        var response = await _client.DeleteAsync($"/api/applications/{Guid.NewGuid()}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task AddResourceType_WithValidData_ReturnsCreated()
    {
        // Arrange
        var appId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var request = new CreateResourceTypeRequest($"resource-{Guid.NewGuid():N}", "New Resource", "Description", true);

        // Act
        var response = await _client.PostAsJsonAsync($"/api/applications/{appId}/resource-types", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var resourceType = await response.Content.ReadFromJsonAsync<ResourceTypeSummary>();
        resourceType.Should().NotBeNull();
        resourceType!.Code.Should().Be(request.Code);
    }

    [Fact]
    public async Task AddResourceType_WithDuplicateCode_ReturnsBadRequest()
    {
        // Arrange
        var appId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var request = new CreateResourceTypeRequest("document", "Duplicate Document");

        // Act
        var response = await _client.PostAsJsonAsync($"/api/applications/{appId}/resource-types", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
