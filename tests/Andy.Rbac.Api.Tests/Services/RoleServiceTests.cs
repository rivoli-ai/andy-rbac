using Andy.Rbac.Api.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Andy.Rbac.Api.Tests.Services;

public class RoleServiceTests
{
    private readonly Mock<ILogger<RoleService>> _loggerMock = new();

    [Fact]
    public async Task GetAllAsync_ReturnsAllRoles()
    {
        // Arrange
        using var context = await TestDbContextFactory.CreateWithSeedDataAsync();
        var service = new RoleService(context, _loggerMock.Object, new Andy.Rbac.Infrastructure.Messaging.RbacEventPublisher(context));

        // Act
        var result = await service.GetAllAsync();

        // Assert
        result.Roles.Should().NotBeEmpty();
        result.Roles.Should().Contain(r => r.Code == "admin");
        result.Roles.Should().Contain(r => r.Code == "editor");
        result.Roles.Should().Contain(r => r.Code == "viewer");
    }

    [Fact]
    public async Task GetAllAsync_WithApplicationFilter_ReturnsFilteredRoles()
    {
        // Arrange
        using var context = await TestDbContextFactory.CreateWithSeedDataAsync();
        var service = new RoleService(context, _loggerMock.Object, new Andy.Rbac.Infrastructure.Messaging.RbacEventPublisher(context));

        // Act
        var result = await service.GetAllAsync("test-app");

        // Assert
        result.Roles.Should().NotBeEmpty();
        result.Roles.All(r => r.ApplicationCode == "test-app" || r.ApplicationCode == null).Should().BeTrue();
    }

    [Fact]
    public async Task GetByIdAsync_WithValidId_ReturnsRole()
    {
        // Arrange
        using var context = await TestDbContextFactory.CreateWithSeedDataAsync();
        var service = new RoleService(context, _loggerMock.Object, new Andy.Rbac.Infrastructure.Messaging.RbacEventPublisher(context));
        var roleId = Guid.Parse("55555555-5555-5555-5555-555555555555");

        // Act
        var result = await service.GetByIdAsync(roleId);

        // Assert
        result.Should().NotBeNull();
        result!.Role.Code.Should().Be("admin");
        result.Role.IsSystem.Should().BeTrue();
    }

    [Fact]
    public async Task GetByIdAsync_WithInvalidId_ReturnsNull()
    {
        // Arrange
        using var context = await TestDbContextFactory.CreateWithSeedDataAsync();
        var service = new RoleService(context, _loggerMock.Object, new Andy.Rbac.Infrastructure.Messaging.RbacEventPublisher(context));

        // Act
        var result = await service.GetByIdAsync(Guid.NewGuid());

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task CreateAsync_WithValidData_CreatesRole()
    {
        // Arrange
        using var context = await TestDbContextFactory.CreateWithSeedDataAsync();
        var service = new RoleService(context, _loggerMock.Object, new Andy.Rbac.Infrastructure.Messaging.RbacEventPublisher(context));
        var request = new CreateRoleRequest("new-role", "New Role", "Description", "test-app");

        // Act
        var result = await service.CreateAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.Role.Code.Should().Be("new-role");
        result.Role.Name.Should().Be("New Role");
        result.Role.ApplicationCode.Should().Be("test-app");
        result.Role.IsSystem.Should().BeFalse();
    }

    [Fact]
    public async Task CreateAsync_WithInvalidApplication_ThrowsException()
    {
        // Arrange
        using var context = await TestDbContextFactory.CreateWithSeedDataAsync();
        var service = new RoleService(context, _loggerMock.Object, new Andy.Rbac.Infrastructure.Messaging.RbacEventPublisher(context));
        var request = new CreateRoleRequest("new-role", "New Role", null, "non-existent-app");

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.CreateAsync(request));
    }

    [Fact]
    public async Task CreateAsync_WithValidParentChain_Succeeds()
    {
        // Issue #46 — cycle detection should not reject acyclic chains.
        using var context = await TestDbContextFactory.CreateWithSeedDataAsync();
        var service = new RoleService(context, _loggerMock.Object, new Andy.Rbac.Infrastructure.Messaging.RbacEventPublisher(context));

        var result = await service.CreateAsync(
            new CreateRoleRequest("inheritor", "Inheritor", null, "test-app", ParentRoleCode: "admin"));

        result.Role.Code.Should().Be("inheritor");
        result.Role.ParentRoleCode.Should().Be("admin");
    }

    [Fact]
    public async Task CreateAsync_WithCyclicParentChain_Throws()
    {
        // Issue #46 — if the parent chain has been corrupted such that the
        // proposed parent ultimately loops back on itself, reject the create.
        using var context = await TestDbContextFactory.CreateWithSeedDataAsync();

        // Manually build a cyclic chain a → b → a in the DB.
        var appId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var roleA = new Andy.Rbac.Models.Role
        {
            Id = Guid.NewGuid(),
            ApplicationId = appId,
            Code = "cycle-a",
            Name = "Cycle A",
        };
        var roleB = new Andy.Rbac.Models.Role
        {
            Id = Guid.NewGuid(),
            ApplicationId = appId,
            Code = "cycle-b",
            Name = "Cycle B",
            ParentRoleId = roleA.Id,
        };
        context.Roles.AddRange(roleA, roleB);
        await context.SaveChangesAsync();
        roleA.ParentRoleId = roleB.Id;
        await context.SaveChangesAsync();

        var service = new RoleService(context, _loggerMock.Object, new Andy.Rbac.Infrastructure.Messaging.RbacEventPublisher(context));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.CreateAsync(new CreateRoleRequest(
                "leaf", "Leaf", null, "test-app", ParentRoleCode: "cycle-a")));
    }

    [Fact]
    public async Task DeleteAsync_WithValidNonSystemRole_DeletesRole()
    {
        // Arrange
        using var context = await TestDbContextFactory.CreateWithSeedDataAsync();
        var service = new RoleService(context, _loggerMock.Object, new Andy.Rbac.Infrastructure.Messaging.RbacEventPublisher(context));

        // Create a non-system role to delete
        var created = await service.CreateAsync(new CreateRoleRequest("to-delete", "To Delete", null, "test-app"));

        // Act
        var result = await service.DeleteAsync(created.Role.Id);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task DeleteAsync_WithSystemRole_ThrowsException()
    {
        // Arrange
        using var context = await TestDbContextFactory.CreateWithSeedDataAsync();
        var service = new RoleService(context, _loggerMock.Object, new Andy.Rbac.Infrastructure.Messaging.RbacEventPublisher(context));
        var adminRoleId = Guid.Parse("55555555-5555-5555-5555-555555555555");

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.DeleteAsync(adminRoleId));
    }

    [Fact]
    public async Task AssignToSubjectAsync_WithValidData_AssignsRole()
    {
        // Arrange
        using var context = await TestDbContextFactory.CreateWithSeedDataAsync();
        var service = new RoleService(context, _loggerMock.Object, new Andy.Rbac.Infrastructure.Messaging.RbacEventPublisher(context));

        // Act - assign editor role to no-role user
        var result = await service.AssignToSubjectAsync("no-role-user", "editor");

        // Assert
        result.Message.Should().Contain("Successfully assigned");
    }

    [Fact]
    public async Task AssignToSubjectAsync_WithDuplicateAssignment_ReturnsAlreadyAssigned()
    {
        // Arrange
        using var context = await TestDbContextFactory.CreateWithSeedDataAsync();
        var service = new RoleService(context, _loggerMock.Object, new Andy.Rbac.Infrastructure.Messaging.RbacEventPublisher(context));

        // Act - try to assign admin role to admin user again
        var result = await service.AssignToSubjectAsync("admin-user", "admin");

        // Assert
        result.Message.Should().Contain("already assigned");
    }

    [Fact]
    public async Task AssignToSubjectWithExpiryAsync_ExpiredAssignment_RenewsExistingRow()
    {
        using var context = await TestDbContextFactory.CreateWithSeedDataAsync();
        var service = new RoleService(context, _loggerMock.Object,
            new Andy.Rbac.Infrastructure.Messaging.RbacEventPublisher(context));
        var assignment = context.SubjectRoles.Single(value =>
            value.SubjectId == Guid.Parse("66666666-6666-6666-6666-666666666668") &&
            value.RoleId == Guid.Parse("55555555-5555-5555-5555-555555555557"));
        assignment.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        await context.SaveChangesAsync();
        var renewedUntil = DateTimeOffset.UtcNow.AddHours(1);

        var result = await service.AssignToSubjectWithExpiryAsync(
            "viewer-user", "viewer", null, "test-app", renewedUntil);

        result.Message.Should().Contain("Successfully assigned");
        context.SubjectRoles.Count(value => value.SubjectId == assignment.SubjectId &&
            value.RoleId == assignment.RoleId).Should().Be(1);
        assignment.ExpiresAt.Should().Be(renewedUntil);
    }

    [Fact]
    public async Task AssignToSubjectAsync_WithInvalidSubject_ReturnsError()
    {
        // Arrange
        using var context = await TestDbContextFactory.CreateWithSeedDataAsync();
        var service = new RoleService(context, _loggerMock.Object, new Andy.Rbac.Infrastructure.Messaging.RbacEventPublisher(context));

        // Act
        var result = await service.AssignToSubjectAsync("non-existent-user", "admin");

        // Assert
        result.Succeeded.Should().BeFalse();
        result.Message.Should().Contain("not found");
    }

    [Fact]
    public async Task AssignToSubjectAsync_WithInvalidRole_ReturnsError()
    {
        // Arrange
        using var context = await TestDbContextFactory.CreateWithSeedDataAsync();
        var service = new RoleService(context, _loggerMock.Object, new Andy.Rbac.Infrastructure.Messaging.RbacEventPublisher(context));

        // Act
        var result = await service.AssignToSubjectAsync("admin-user", "non-existent-role");

        // Assert
        result.Succeeded.Should().BeFalse();
        result.Message.Should().Contain("not found");
    }

    [Fact]
    public async Task RevokeFromSubjectAsync_WithValidData_RevokesRole()
    {
        // Arrange
        using var context = await TestDbContextFactory.CreateWithSeedDataAsync();
        var service = new RoleService(context, _loggerMock.Object, new Andy.Rbac.Infrastructure.Messaging.RbacEventPublisher(context));

        // Act - revoke admin role from admin user
        var result = await service.RevokeFromSubjectAsync("admin-user", "admin");

        // Assert
        result.Message.Should().Contain("Successfully revoked");
    }

    [Fact]
    public async Task RevokeFromSubjectAsync_WithNoAssignment_ReturnsNotAssigned()
    {
        // Arrange
        using var context = await TestDbContextFactory.CreateWithSeedDataAsync();
        var service = new RoleService(context, _loggerMock.Object, new Andy.Rbac.Infrastructure.Messaging.RbacEventPublisher(context));

        // Act - try to revoke role that isn't assigned
        var result = await service.RevokeFromSubjectAsync("viewer-user", "admin");

        // Assert
        result.Message.Should().Contain("not assigned");
    }

    [Fact]
    public async Task AssignToTeamAsync_WithValidData_AssignsRoleToTeam()
    {
        // Arrange
        using var context = await TestDbContextFactory.CreateWithSeedDataAsync();
        var service = new RoleService(context, _loggerMock.Object, new Andy.Rbac.Infrastructure.Messaging.RbacEventPublisher(context));

        // Act
        var result = await service.AssignToTeamAsync("test-team", "viewer");

        // Assert
        result.Message.Should().Contain("Successfully assigned");
    }

    [Fact]
    public async Task AssignToTeamAsync_WithDuplicateAssignment_ReturnsAlreadyAssigned()
    {
        // Arrange
        using var context = await TestDbContextFactory.CreateWithSeedDataAsync();
        var service = new RoleService(context, _loggerMock.Object, new Andy.Rbac.Infrastructure.Messaging.RbacEventPublisher(context));

        // Assign once
        await service.AssignToTeamAsync("test-team", "viewer");

        // Act - try to assign again
        var result = await service.AssignToTeamAsync("test-team", "viewer");

        // Assert
        result.Message.Should().Contain("already assigned");
    }
}
