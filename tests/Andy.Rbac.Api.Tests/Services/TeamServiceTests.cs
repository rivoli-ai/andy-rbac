using Andy.Rbac.Api.Services;
using Andy.Rbac.Models;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Andy.Rbac.Api.Tests.Services;

public class TeamServiceTests
{
    private readonly Mock<ILogger<TeamService>> _loggerMock = new();

    [Fact]
    public async Task GetAllAsync_ReturnsAllTeams()
    {
        // Arrange
        using var context = await TestDbContextFactory.CreateWithSeedDataAsync();
        var service = new TeamService(context, _loggerMock.Object);

        // Act
        var result = await service.GetAllAsync();

        // Assert
        result.Teams.Should().ContainSingle();
        result.Teams[0].Code.Should().Be("test-team");
    }

    [Fact]
    public async Task GetByIdAsync_WithValidId_ReturnsTeamWithDetails()
    {
        // Arrange
        using var context = await TestDbContextFactory.CreateWithSeedDataAsync();
        var service = new TeamService(context, _loggerMock.Object);
        var teamId = Guid.Parse("77777777-7777-7777-7777-777777777777");

        // Act
        var result = await service.GetByIdAsync(teamId);

        // Assert
        result.Should().NotBeNull();
        result!.Team.Code.Should().Be("test-team");
        result.Team.Members.Should().ContainSingle();
        result.Team.Members[0].ExternalId.Should().Be("editor-user");
    }

    [Fact]
    public async Task GetByIdAsync_WithInvalidId_ReturnsNull()
    {
        // Arrange
        using var context = await TestDbContextFactory.CreateWithSeedDataAsync();
        var service = new TeamService(context, _loggerMock.Object);

        // Act
        var result = await service.GetByIdAsync(Guid.NewGuid());

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetByCodeAsync_WithValidCode_ReturnsTeam()
    {
        // Arrange
        using var context = await TestDbContextFactory.CreateWithSeedDataAsync();
        var service = new TeamService(context, _loggerMock.Object);

        // Act
        var result = await service.GetByCodeAsync("test-team");

        // Assert
        result.Should().NotBeNull();
        result!.Team.Name.Should().Be("Test Team");
    }

    [Fact]
    public async Task GetByCodeAsync_WithInvalidCode_ReturnsNull()
    {
        // Arrange
        using var context = await TestDbContextFactory.CreateWithSeedDataAsync();
        var service = new TeamService(context, _loggerMock.Object);

        // Act
        var result = await service.GetByCodeAsync("non-existent");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task CreateAsync_WithValidData_CreatesTeam()
    {
        // Arrange
        using var context = TestDbContextFactory.Create();
        var service = new TeamService(context, _loggerMock.Object);
        var request = new CreateTeamRequest("new-team", "New Team", "Description");

        // Act
        var result = await service.CreateAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.Team.Code.Should().Be("new-team");
        result.Team.Name.Should().Be("New Team");
        result.Team.Description.Should().Be("Description");
        result.Team.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task CreateAsync_WithDuplicateCode_ThrowsException()
    {
        // Arrange
        using var context = await TestDbContextFactory.CreateWithSeedDataAsync();
        var service = new TeamService(context, _loggerMock.Object);
        var request = new CreateTeamRequest("test-team", "Duplicate");

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.CreateAsync(request));
    }

    [Fact]
    public async Task CreateAsync_WithParentTeam_CreatesHierarchy()
    {
        // Arrange
        using var context = await TestDbContextFactory.CreateWithSeedDataAsync();
        var service = new TeamService(context, _loggerMock.Object);
        var request = new CreateTeamRequest("child-team", "Child Team", null, "test-team");

        // Act
        var result = await service.CreateAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.Team.ParentTeamCode.Should().Be("test-team");
    }

    [Fact]
    public async Task CreateAsync_WithInvalidParentTeam_ThrowsException()
    {
        // Arrange
        using var context = await TestDbContextFactory.CreateWithSeedDataAsync();
        var service = new TeamService(context, _loggerMock.Object);
        var request = new CreateTeamRequest("child-team", "Child Team", null, "non-existent");

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.CreateAsync(request));
    }

    [Fact]
    public async Task CreateAsync_WithApplication_ScopesToApplication()
    {
        // Arrange
        using var context = await TestDbContextFactory.CreateWithSeedDataAsync();
        var service = new TeamService(context, _loggerMock.Object);
        var request = new CreateTeamRequest("app-team", "App Team", null, null, "test-app");

        // Act
        var result = await service.CreateAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.Team.ApplicationCode.Should().Be("test-app");
    }

    [Fact]
    public async Task DeleteAsync_WithValidId_DeletesTeam()
    {
        // Arrange
        using var context = TestDbContextFactory.Create();
        var service = new TeamService(context, _loggerMock.Object);

        // Create a team to delete
        var created = await service.CreateAsync(new CreateTeamRequest("to-delete", "To Delete"));

        // Act
        var result = await service.DeleteAsync(created.Team.Id);

        // Assert
        result.Should().BeTrue();
        var retrieved = await service.GetByCodeAsync("to-delete");
        retrieved.Should().BeNull();
    }

    [Fact]
    public async Task DeleteAsync_WithInvalidId_ReturnsFalse()
    {
        // Arrange
        using var context = await TestDbContextFactory.CreateWithSeedDataAsync();
        var service = new TeamService(context, _loggerMock.Object);

        // Act
        var result = await service.DeleteAsync(Guid.NewGuid());

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task AddMemberAsync_WithValidData_AddsMember()
    {
        // Arrange
        using var context = await TestDbContextFactory.CreateWithSeedDataAsync();
        var service = new TeamService(context, _loggerMock.Object);

        // Act - add viewer user to team
        var result = await service.AddMemberAsync("test-team", "viewer-user", TeamMembershipRole.Member);

        // Assert
        result.Should().Contain("Successfully added");
    }

    [Fact]
    public async Task AddMemberAsync_WithDuplicateMember_ReturnsAlreadyMember()
    {
        // Arrange
        using var context = await TestDbContextFactory.CreateWithSeedDataAsync();
        var service = new TeamService(context, _loggerMock.Object);

        // Act - editor is already a member
        var result = await service.AddMemberAsync("test-team", "editor-user", TeamMembershipRole.Member);

        // Assert
        result.Should().Contain("already a member");
    }

    [Fact]
    public async Task AddMemberAsync_WithInvalidTeam_ReturnsError()
    {
        // Arrange
        using var context = await TestDbContextFactory.CreateWithSeedDataAsync();
        var service = new TeamService(context, _loggerMock.Object);

        // Act
        var result = await service.AddMemberAsync("non-existent", "admin-user");

        // Assert
        result.Should().StartWith("Error:");
    }

    [Fact]
    public async Task AddMemberAsync_WithInvalidSubject_ReturnsError()
    {
        // Arrange
        using var context = await TestDbContextFactory.CreateWithSeedDataAsync();
        var service = new TeamService(context, _loggerMock.Object);

        // Act
        var result = await service.AddMemberAsync("test-team", "non-existent");

        // Assert
        result.Should().StartWith("Error:");
    }

    [Fact]
    public async Task AddMemberAsync_WithAdminRole_SetsCorrectRole()
    {
        // Arrange
        using var context = await TestDbContextFactory.CreateWithSeedDataAsync();
        var service = new TeamService(context, _loggerMock.Object);

        // Act
        var result = await service.AddMemberAsync("test-team", "admin-user", TeamMembershipRole.Admin);

        // Assert
        result.Should().Contain("Admin");

        // Verify the membership role
        var team = await service.GetByCodeAsync("test-team");
        team!.Team.Members.Should().Contain(m => m.ExternalId == "admin-user" && m.MembershipRole == "Admin");
    }

    [Fact]
    public async Task RemoveMemberAsync_WithValidData_RemovesMember()
    {
        // Arrange
        using var context = await TestDbContextFactory.CreateWithSeedDataAsync();
        var service = new TeamService(context, _loggerMock.Object);

        // Act - remove editor from team
        var result = await service.RemoveMemberAsync("test-team", "editor-user");

        // Assert
        result.Should().Contain("Successfully removed");

        // Verify member was removed
        var team = await service.GetByCodeAsync("test-team");
        team!.Team.Members.Should().BeEmpty();
    }

    [Fact]
    public async Task RemoveMemberAsync_WithNonMember_ReturnsNotMember()
    {
        // Arrange
        using var context = await TestDbContextFactory.CreateWithSeedDataAsync();
        var service = new TeamService(context, _loggerMock.Object);

        // Act - admin is not a member of the team
        var result = await service.RemoveMemberAsync("test-team", "admin-user");

        // Assert
        result.Should().Contain("not a member");
    }
}
