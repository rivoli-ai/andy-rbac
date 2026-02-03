using Andy.Rbac.Api.Mcp;
using Andy.Rbac.Api.Services;
using Andy.Rbac.Models;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Andy.Rbac.Api.Tests.Mcp;

public class RbacMcpToolsTests
{
    private readonly Mock<IPermissionEvaluator> _evaluatorMock = new();
    private readonly Mock<IApplicationService> _applicationServiceMock = new();
    private readonly Mock<IRoleService> _roleServiceMock = new();
    private readonly Mock<ITeamService> _teamServiceMock = new();
    private readonly Mock<ISubjectService> _subjectServiceMock = new();
    private readonly Mock<ILogger<RbacMcpTools>> _loggerMock = new();

    private RbacMcpTools CreateTools()
    {
        return new RbacMcpTools(
            _evaluatorMock.Object,
            _applicationServiceMock.Object,
            _roleServiceMock.Object,
            _teamServiceMock.Object,
            _subjectServiceMock.Object,
            _loggerMock.Object);
    }

    // ==================== Permission Checking Tests ====================

    [Fact]
    public async Task CheckPermission_WithAllowed_ReturnsAllowedResult()
    {
        // Arrange
        var tools = CreateTools();
        _evaluatorMock
            .Setup(x => x.CheckPermissionAsync("user-123", "app:doc:read", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PermissionCheckResult(true));

        // Act
        var result = await tools.CheckPermission("user-123", "app:doc:read");

        // Assert
        result.Allowed.Should().BeTrue();
        result.Reason.Should().Be("Permission granted");
    }

    [Fact]
    public async Task CheckPermission_WithDenied_ReturnsDeniedResult()
    {
        // Arrange
        var tools = CreateTools();
        _evaluatorMock
            .Setup(x => x.CheckPermissionAsync("user-123", "app:doc:delete", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PermissionCheckResult(false, "Permission denied"));

        // Act
        var result = await tools.CheckPermission("user-123", "app:doc:delete");

        // Assert
        result.Allowed.Should().BeFalse();
        result.Reason.Should().Be("Permission denied");
    }

    [Fact]
    public async Task CheckPermission_WithResourceInstance_PassesInstanceId()
    {
        // Arrange
        var tools = CreateTools();
        _evaluatorMock
            .Setup(x => x.CheckPermissionAsync("user-123", "app:doc:read", "doc-456", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PermissionCheckResult(true));

        // Act
        var result = await tools.CheckPermission("user-123", "app:doc:read", "doc-456");

        // Assert
        result.Allowed.Should().BeTrue();
        _evaluatorMock.Verify(
            x => x.CheckPermissionAsync("user-123", "app:doc:read", "doc-456", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetUserPermissions_ReturnsPermissionList()
    {
        // Arrange
        var tools = CreateTools();
        var expectedPermissions = new List<string> { "app:doc:read", "app:doc:write" };
        _evaluatorMock
            .Setup(x => x.GetPermissionsAsync("user-123", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedPermissions);

        // Act
        var result = await tools.GetUserPermissions("user-123");

        // Assert
        result.Should().BeEquivalentTo(expectedPermissions);
    }

    [Fact]
    public async Task GetUserPermissions_WithApplicationFilter_PassesAppCode()
    {
        // Arrange
        var tools = CreateTools();
        _evaluatorMock
            .Setup(x => x.GetPermissionsAsync("user-123", "my-app", It.IsAny<CancellationToken>()))
            .ReturnsAsync(["my-app:doc:read"]);

        // Act
        var result = await tools.GetUserPermissions("user-123", "my-app");

        // Assert
        _evaluatorMock.Verify(
            x => x.GetPermissionsAsync("user-123", "my-app", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetUserRoles_ReturnsRoleList()
    {
        // Arrange
        var tools = CreateTools();
        var expectedRoles = new List<string> { "admin", "editor" };
        _evaluatorMock
            .Setup(x => x.GetRolesAsync("user-123", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedRoles);

        // Act
        var result = await tools.GetUserRoles("user-123");

        // Assert
        result.Should().BeEquivalentTo(expectedRoles);
    }

    // ==================== Application Management Tests ====================

    [Fact]
    public async Task ListApplications_ReturnsApplicationList()
    {
        // Arrange
        var tools = CreateTools();
        var apps = new List<ApplicationSummary>
        {
            new(Guid.NewGuid(), "app-1", "App 1", "Description 1", 2, 3, DateTimeOffset.UtcNow),
            new(Guid.NewGuid(), "app-2", "App 2", null, 1, 2, DateTimeOffset.UtcNow)
        };
        _applicationServiceMock
            .Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApplicationResult(apps));

        // Act
        var result = await tools.ListApplications();

        // Assert
        result.Should().HaveCount(2);
        result[0].Code.Should().Be("app-1");
        result[0].Name.Should().Be("App 1");
    }

    [Fact]
    public async Task GetApplication_WithExistingApp_ReturnsDetail()
    {
        // Arrange
        var tools = CreateTools();
        var appDetail = new ApplicationDetail(
            Guid.NewGuid(),
            "test-app",
            "Test App",
            "Description",
            DateTimeOffset.UtcNow,
            [new ResourceTypeSummary(Guid.NewGuid(), "document", "Document", null, true)],
            [new RoleSummary(Guid.NewGuid(), "admin", "Admin", true)]);
        _applicationServiceMock
            .Setup(x => x.GetByCodeAsync("test-app", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApplicationDetailResult(appDetail));

        // Act
        var result = await tools.GetApplication("test-app");

        // Assert
        result.Should().NotBeNull();
        result!.Code.Should().Be("test-app");
        result.ResourceTypes.Should().HaveCount(1);
        result.Roles.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetApplication_WithNonExistentApp_ReturnsNull()
    {
        // Arrange
        var tools = CreateTools();
        _applicationServiceMock
            .Setup(x => x.GetByCodeAsync("non-existent", It.IsAny<CancellationToken>()))
            .ReturnsAsync((ApplicationDetailResult?)null);

        // Act
        var result = await tools.GetApplication("non-existent");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task CreateApplication_CreatesAndReturnsApp()
    {
        // Arrange
        var tools = CreateTools();
        var createdApp = new ApplicationDetail(
            Guid.NewGuid(),
            "new-app",
            "New App",
            "Description",
            DateTimeOffset.UtcNow,
            [],
            []);
        _applicationServiceMock
            .Setup(x => x.CreateAsync(It.Is<CreateApplicationRequest>(r => r.Code == "new-app"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApplicationDetailResult(createdApp));

        // Act
        var result = await tools.CreateApplication("new-app", "New App", "Description");

        // Assert
        result.Code.Should().Be("new-app");
        result.Name.Should().Be("New App");
    }

    // ==================== Role Management Tests ====================

    [Fact]
    public async Task ListRoles_ReturnsRoleList()
    {
        // Arrange
        var tools = CreateTools();
        var roles = new List<RoleDetail>
        {
            new(Guid.NewGuid(), "admin", "Admin", null, "app-1", null, true, []),
            new(Guid.NewGuid(), "editor", "Editor", null, "app-1", null, false, [])
        };
        _roleServiceMock
            .Setup(x => x.GetAllAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RoleListResult(roles));

        // Act
        var result = await tools.ListRoles();

        // Assert
        result.Should().HaveCount(2);
        result[0].Code.Should().Be("admin");
        result[0].IsSystem.Should().BeTrue();
    }

    [Fact]
    public async Task CreateRole_CreatesAndReturnsRole()
    {
        // Arrange
        var tools = CreateTools();
        var createdRole = new RoleDetail(
            Guid.NewGuid(),
            "new-role",
            "New Role",
            "Description",
            "app-1",
            null,
            false,
            []);
        _roleServiceMock
            .Setup(x => x.CreateAsync(It.Is<CreateRoleRequest>(r => r.Code == "new-role"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RoleDetailResult(createdRole));

        // Act
        var result = await tools.CreateRole("new-role", "New Role", "Description", "app-1");

        // Assert
        result.Code.Should().Be("new-role");
        result.Name.Should().Be("New Role");
        result.IsSystem.Should().BeFalse();
    }

    [Fact]
    public async Task AssignRoleToUser_CallsService()
    {
        // Arrange
        var tools = CreateTools();
        _roleServiceMock
            .Setup(x => x.AssignToSubjectAsync("user-123", "admin", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync("Successfully assigned role 'admin' to user-123");

        // Act
        var result = await tools.AssignRoleToUser("user-123", "admin");

        // Assert
        result.Should().Contain("Successfully assigned");
        _roleServiceMock.Verify(
            x => x.AssignToSubjectAsync("user-123", "admin", null, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RevokeRoleFromUser_CallsService()
    {
        // Arrange
        var tools = CreateTools();
        _roleServiceMock
            .Setup(x => x.RevokeFromSubjectAsync("user-123", "admin", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync("Successfully revoked role 'admin' from user-123");

        // Act
        var result = await tools.RevokeRoleFromUser("user-123", "admin");

        // Assert
        result.Should().Contain("Successfully revoked");
    }

    // ==================== Team Management Tests ====================

    [Fact]
    public async Task ListTeams_ReturnsTeamList()
    {
        // Arrange
        var tools = CreateTools();
        var teams = new List<TeamSummary>
        {
            new(Guid.NewGuid(), "team-1", "Team 1", "Description", null, null, 5, true),
            new(Guid.NewGuid(), "team-2", "Team 2", null, "team-1", null, 3, true)
        };
        _teamServiceMock
            .Setup(x => x.GetAllAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TeamListResult(teams));

        // Act
        var result = await tools.ListTeams();

        // Assert
        result.Should().HaveCount(2);
        result[0].Code.Should().Be("team-1");
        result[1].ParentTeamCode.Should().Be("team-1");
    }

    [Fact]
    public async Task CreateTeam_CreatesAndReturnsTeam()
    {
        // Arrange
        var tools = CreateTools();
        var createdTeam = new TeamDetail(
            Guid.NewGuid(),
            "new-team",
            "New Team",
            "Description",
            null,
            null,
            true,
            [],
            []);
        _teamServiceMock
            .Setup(x => x.CreateAsync(It.Is<CreateTeamRequest>(r => r.Code == "new-team"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TeamDetailResult(createdTeam));

        // Act
        var result = await tools.CreateTeam("new-team", "New Team", "Description");

        // Assert
        result.Code.Should().Be("new-team");
        result.Name.Should().Be("New Team");
    }

    [Fact]
    public async Task AddUserToTeam_WithMemberRole_CallsService()
    {
        // Arrange
        var tools = CreateTools();
        _teamServiceMock
            .Setup(x => x.AddMemberAsync("team-1", "user-123", TeamMembershipRole.Member, It.IsAny<CancellationToken>()))
            .ReturnsAsync("Successfully added user-123 to team-1 as Member");

        // Act
        var result = await tools.AddUserToTeam("team-1", "user-123", "Member");

        // Assert
        result.Should().Contain("Successfully added");
    }

    [Fact]
    public async Task AddUserToTeam_WithAdminRole_CallsServiceWithAdminRole()
    {
        // Arrange
        var tools = CreateTools();
        _teamServiceMock
            .Setup(x => x.AddMemberAsync("team-1", "user-123", TeamMembershipRole.Admin, It.IsAny<CancellationToken>()))
            .ReturnsAsync("Successfully added user-123 to team-1 as Admin");

        // Act
        var result = await tools.AddUserToTeam("team-1", "user-123", "Admin");

        // Assert
        _teamServiceMock.Verify(
            x => x.AddMemberAsync("team-1", "user-123", TeamMembershipRole.Admin, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task AddUserToTeam_WithInvalidRole_DefaultsToMember()
    {
        // Arrange
        var tools = CreateTools();
        _teamServiceMock
            .Setup(x => x.AddMemberAsync("team-1", "user-123", TeamMembershipRole.Member, It.IsAny<CancellationToken>()))
            .ReturnsAsync("Successfully added user-123 to team-1 as Member");

        // Act
        var result = await tools.AddUserToTeam("team-1", "user-123", "InvalidRole");

        // Assert
        _teamServiceMock.Verify(
            x => x.AddMemberAsync("team-1", "user-123", TeamMembershipRole.Member, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task AssignRoleToTeam_CallsService()
    {
        // Arrange
        var tools = CreateTools();
        _roleServiceMock
            .Setup(x => x.AssignToTeamAsync("team-1", "editor", It.IsAny<CancellationToken>()))
            .ReturnsAsync("Successfully assigned role 'editor' to team team-1");

        // Act
        var result = await tools.AssignRoleToTeam("team-1", "editor");

        // Assert
        result.Should().Contain("Successfully assigned");
    }

    // ==================== User Management Tests ====================

    [Fact]
    public async Task SearchUsers_ReturnsUserList()
    {
        // Arrange
        var tools = CreateTools();
        var users = new List<SubjectSummary>
        {
            new(Guid.NewGuid(), "user-1", "provider", "user1@test.com", "User 1", true),
            new(Guid.NewGuid(), "user-2", "provider", "user2@test.com", "User 2", true)
        };
        _subjectServiceMock
            .Setup(x => x.SearchAsync("test", 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SubjectListResult(users));

        // Act
        var result = await tools.SearchUsers("test");

        // Assert
        result.Should().HaveCount(2);
        result[0].ExternalId.Should().Be("user-1");
    }

    [Fact]
    public async Task GetUser_WithExistingUser_ReturnsDetail()
    {
        // Arrange
        var tools = CreateTools();
        var userDetail = new SubjectDetail(
            Guid.NewGuid(),
            "user-123",
            "provider",
            "user@test.com",
            "Test User",
            true,
            DateTimeOffset.UtcNow,
            [new SubjectRoleInfo("admin", "app-1", null)],
            [new SubjectTeamInfo("team-1", "Team 1", "Member")]);
        _subjectServiceMock
            .Setup(x => x.GetByExternalIdAsync("user-123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SubjectDetailResult(userDetail));

        // Act
        var result = await tools.GetUser("user-123");

        // Assert
        result.Should().NotBeNull();
        result!.ExternalId.Should().Be("user-123");
        result.Roles.Should().HaveCount(1);
        result.Teams.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetUser_WithNonExistentUser_ReturnsNull()
    {
        // Arrange
        var tools = CreateTools();
        _subjectServiceMock
            .Setup(x => x.GetByExternalIdAsync("non-existent", It.IsAny<CancellationToken>()))
            .ReturnsAsync((SubjectDetailResult?)null);

        // Act
        var result = await tools.GetUser("non-existent");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task CreateUser_CreatesAndReturnsUser()
    {
        // Arrange
        var tools = CreateTools();
        var createdUser = new SubjectDetail(
            Guid.NewGuid(),
            "new-user",
            "provider",
            "new@test.com",
            "New User",
            true,
            DateTimeOffset.UtcNow,
            [],
            []);
        _subjectServiceMock
            .Setup(x => x.CreateAsync(It.Is<CreateSubjectRequest>(r => r.ExternalId == "new-user"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SubjectDetailResult(createdUser));

        // Act
        var result = await tools.CreateUser("new-user", "provider", "new@test.com", "New User");

        // Assert
        result.ExternalId.Should().Be("new-user");
        result.Email.Should().Be("new@test.com");
        result.IsActive.Should().BeTrue();
    }
}
