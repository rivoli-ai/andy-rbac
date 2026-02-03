using System.Net;
using System.Net.Http.Json;
using Andy.Rbac.Api.Controllers;
using Andy.Rbac.Models;
using FluentAssertions;
using Xunit;

namespace Andy.Rbac.Api.Tests.Integration;

public class TeamsControllerTests : IClassFixture<RbacWebApplicationFactory>
{
    private readonly HttpClient _client;

    public TeamsControllerTests(RbacWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetTeams_ReturnsTeamList()
    {
        // Act
        var response = await _client.GetAsync("/api/teams");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var teams = await response.Content.ReadFromJsonAsync<List<TeamDto>>();
        teams.Should().NotBeNull();
        teams.Should().Contain(t => t.Code == "test-team");
    }

    [Fact]
    public async Task GetTeam_WithValidId_ReturnsTeamDetail()
    {
        // Arrange
        var teamId = Guid.Parse("77777777-7777-7777-7777-777777777777");

        // Act
        var response = await _client.GetAsync($"/api/teams/{teamId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var team = await response.Content.ReadFromJsonAsync<TeamDetailDto>();
        team.Should().NotBeNull();
        team!.Code.Should().Be("test-team");
        team.Members.Should().Contain(m => m.SubjectExternalId == "editor-user");
    }

    [Fact]
    public async Task GetTeam_WithInvalidId_ReturnsNotFound()
    {
        // Act
        var response = await _client.GetAsync($"/api/teams/{Guid.NewGuid()}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetTeamByCode_WithValidCode_ReturnsTeam()
    {
        // Act
        var response = await _client.GetAsync("/api/teams/by-code/test-team");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var team = await response.Content.ReadFromJsonAsync<TeamDetailDto>();
        team.Should().NotBeNull();
        team!.Code.Should().Be("test-team");
    }

    [Fact]
    public async Task GetTeamByCode_WithInvalidCode_ReturnsNotFound()
    {
        // Act
        var response = await _client.GetAsync("/api/teams/by-code/non-existent");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task CreateTeam_WithValidData_ReturnsCreated()
    {
        // Arrange
        var request = new CreateTeamRequest($"new-team-{Guid.NewGuid():N}", "New Team", "A test team");

        // Act
        var response = await _client.PostAsJsonAsync("/api/teams", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var team = await response.Content.ReadFromJsonAsync<TeamDto>();
        team.Should().NotBeNull();
        team!.Code.Should().Be(request.Code);
        team.Name.Should().Be("New Team");
    }

    [Fact]
    public async Task CreateTeam_WithDuplicateCode_ReturnsBadRequest()
    {
        // Arrange
        var request = new CreateTeamRequest("test-team", "Duplicate Team");

        // Act
        var response = await _client.PostAsJsonAsync("/api/teams", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateTeam_WithParentTeam_CreatesHierarchy()
    {
        // Arrange
        var request = new CreateTeamRequest($"child-team-{Guid.NewGuid():N}", "Child Team", null, "test-team");

        // Act
        var response = await _client.PostAsJsonAsync("/api/teams", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var team = await response.Content.ReadFromJsonAsync<TeamDto>();
        team.Should().NotBeNull();
        team!.ParentTeamCode.Should().Be("test-team");
    }

    [Fact]
    public async Task CreateTeam_WithInvalidParent_ReturnsBadRequest()
    {
        // Arrange
        var request = new CreateTeamRequest("orphan-team", "Orphan Team", null, "non-existent-parent");

        // Act
        var response = await _client.PostAsJsonAsync("/api/teams", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UpdateTeam_WithValidData_ReturnsOk()
    {
        // Arrange
        var teamId = Guid.Parse("77777777-7777-7777-7777-777777777777");
        var request = new UpdateTeamRequest("Updated Team Name", "Updated description");

        // Act
        var response = await _client.PutAsJsonAsync($"/api/teams/{teamId}", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var team = await response.Content.ReadFromJsonAsync<TeamDto>();
        team.Should().NotBeNull();
        team!.Name.Should().Be("Updated Team Name");
    }

    [Fact]
    public async Task UpdateTeam_WithInvalidId_ReturnsNotFound()
    {
        // Arrange
        var request = new UpdateTeamRequest("Updated Name");

        // Act
        var response = await _client.PutAsJsonAsync($"/api/teams/{Guid.NewGuid()}", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task AddMember_WithValidData_ReturnsCreated()
    {
        // Arrange
        var teamId = Guid.Parse("77777777-7777-7777-7777-777777777777");
        var request = new AddTeamMemberRequest("admin-user", "test-provider", TeamMembershipRole.Admin);

        // Act
        var response = await _client.PostAsJsonAsync($"/api/teams/{teamId}/members", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task AddMember_WithInvalidTeam_ReturnsNotFound()
    {
        // Arrange
        var request = new AddTeamMemberRequest("admin-user", "test-provider");

        // Act
        var response = await _client.PostAsJsonAsync($"/api/teams/{Guid.NewGuid()}/members", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task AddMember_WithInvalidSubject_ReturnsNotFound()
    {
        // Arrange
        var teamId = Guid.Parse("77777777-7777-7777-7777-777777777777");
        var request = new AddTeamMemberRequest("non-existent-user", "test-provider");

        // Act
        var response = await _client.PostAsJsonAsync($"/api/teams/{teamId}/members", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task AddMember_WithDuplicateMember_ReturnsBadRequest()
    {
        // Arrange - editor-user is already a member of test-team
        var teamId = Guid.Parse("77777777-7777-7777-7777-777777777777");
        var request = new AddTeamMemberRequest("editor-user", "test-provider");

        // Act
        var response = await _client.PostAsJsonAsync($"/api/teams/{teamId}/members", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task RemoveMember_WithValidData_ReturnsNoContent()
    {
        // Arrange - first add a member, then remove
        var teamId = Guid.Parse("77777777-7777-7777-7777-777777777777");
        var viewerId = Guid.Parse("66666666-6666-6666-6666-666666666668");

        // Add member first
        var addRequest = new AddTeamMemberRequest("viewer-user", "test-provider");
        await _client.PostAsJsonAsync($"/api/teams/{teamId}/members", addRequest);

        // Act
        var response = await _client.DeleteAsync($"/api/teams/{teamId}/members/{viewerId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task RemoveMember_WithNonMember_ReturnsNotFound()
    {
        // Arrange - use no-role user who is not a member of the team
        var teamId = Guid.Parse("77777777-7777-7777-7777-777777777777");
        var noRoleId = Guid.Parse("66666666-6666-6666-6666-666666666669");

        // Act - no-role user is not a member
        var response = await _client.DeleteAsync($"/api/teams/{teamId}/members/{noRoleId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task AssignTeamRole_WithValidData_ReturnsCreated()
    {
        // Arrange
        var teamId = Guid.Parse("77777777-7777-7777-7777-777777777777");
        var request = new AssignTeamRoleRequest("viewer");

        // Act
        var response = await _client.PostAsJsonAsync($"/api/teams/{teamId}/roles", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task AssignTeamRole_WithInvalidTeam_ReturnsNotFound()
    {
        // Arrange
        var request = new AssignTeamRoleRequest("admin");

        // Act
        var response = await _client.PostAsJsonAsync($"/api/teams/{Guid.NewGuid()}/roles", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task AssignTeamRole_WithInvalidRole_ReturnsNotFound()
    {
        // Arrange
        var teamId = Guid.Parse("77777777-7777-7777-7777-777777777777");
        var request = new AssignTeamRoleRequest("non-existent-role");

        // Act
        var response = await _client.PostAsJsonAsync($"/api/teams/{teamId}/roles", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task RevokeTeamRole_WithValidData_ReturnsNoContent()
    {
        // Arrange - first assign, then revoke
        var teamId = Guid.Parse("77777777-7777-7777-7777-777777777777");
        var assignRequest = new AssignTeamRoleRequest("editor");
        await _client.PostAsJsonAsync($"/api/teams/{teamId}/roles", assignRequest);

        // Act
        var response = await _client.DeleteAsync($"/api/teams/{teamId}/roles/editor");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task RevokeTeamRole_WithInvalidRole_ReturnsNotFound()
    {
        // Arrange
        var teamId = Guid.Parse("77777777-7777-7777-7777-777777777777");

        // Act
        var response = await _client.DeleteAsync($"/api/teams/{teamId}/roles/non-existent-role");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
