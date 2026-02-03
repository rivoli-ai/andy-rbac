using Andy.Rbac.Api.Services;
using Andy.Rbac.Infrastructure.Repositories;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Andy.Rbac.Api.Tests.Services;

public class PermissionEvaluatorTests
{
    private readonly Mock<IPermissionRepository> _permissionRepoMock = new();
    private readonly Mock<ILogger<PermissionEvaluator>> _loggerMock = new();

    private PermissionEvaluator CreateEvaluator(string? dbName = null)
    {
        var context = dbName != null
            ? TestDbContextFactory.Create(dbName)
            : TestDbContextFactory.Create();
        return new PermissionEvaluator(context, _permissionRepoMock.Object, _loggerMock.Object);
    }

    [Fact]
    public async Task CheckPermissionAsync_WithNonExistentSubject_ReturnsDenied()
    {
        // Arrange
        using var context = TestDbContextFactory.Create();
        var evaluator = new PermissionEvaluator(context, _permissionRepoMock.Object, _loggerMock.Object);

        // Act
        var result = await evaluator.CheckPermissionAsync("non-existent", "document:read");

        // Assert
        result.Allowed.Should().BeFalse();
        result.Reason.Should().Be("Subject not found");
    }

    [Fact]
    public async Task CheckPermissionAsync_WithInactiveSubject_ReturnsDenied()
    {
        // Arrange
        using var context = await TestDbContextFactory.CreateWithSeedDataAsync();
        var evaluator = new PermissionEvaluator(context, _permissionRepoMock.Object, _loggerMock.Object);

        // Deactivate the admin user
        var admin = context.Subjects.First(s => s.ExternalId == "admin-user");
        admin.IsActive = false;
        await context.SaveChangesAsync();

        // Act
        var result = await evaluator.CheckPermissionAsync("admin-user", "document:read");

        // Assert
        result.Allowed.Should().BeFalse();
        result.Reason.Should().Be("Subject is inactive");
    }

    [Fact]
    public async Task CheckPermissionAsync_WithPermission_ReturnsAllowed()
    {
        // Arrange
        using var context = await TestDbContextFactory.CreateWithSeedDataAsync();
        var evaluator = new PermissionEvaluator(context, _permissionRepoMock.Object, _loggerMock.Object);

        var adminId = Guid.Parse("66666666-6666-6666-6666-666666666666");
        _permissionRepoMock
            .Setup(x => x.HasPermissionAsync(adminId, "document:read", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await evaluator.CheckPermissionAsync("admin-user", "document:read");

        // Assert
        result.Allowed.Should().BeTrue();
        result.Reason.Should().BeNull();
    }

    [Fact]
    public async Task CheckPermissionAsync_WithoutPermission_ReturnsDenied()
    {
        // Arrange
        using var context = await TestDbContextFactory.CreateWithSeedDataAsync();
        var evaluator = new PermissionEvaluator(context, _permissionRepoMock.Object, _loggerMock.Object);

        var viewerId = Guid.Parse("66666666-6666-6666-6666-666666666668");
        _permissionRepoMock
            .Setup(x => x.HasPermissionAsync(viewerId, "document:delete", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var result = await evaluator.CheckPermissionAsync("viewer-user", "document:delete");

        // Assert
        result.Allowed.Should().BeFalse();
        result.Reason.Should().Be("Permission denied");
    }

    [Fact]
    public async Task CheckPermissionAsync_WithResourceInstance_PassesInstanceId()
    {
        // Arrange
        using var context = await TestDbContextFactory.CreateWithSeedDataAsync();
        var evaluator = new PermissionEvaluator(context, _permissionRepoMock.Object, _loggerMock.Object);

        var adminId = Guid.Parse("66666666-6666-6666-6666-666666666666");
        var instanceId = "doc-123";

        _permissionRepoMock
            .Setup(x => x.HasPermissionAsync(adminId, "document:read", instanceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await evaluator.CheckPermissionAsync("admin-user", "document:read", instanceId);

        // Assert
        result.Allowed.Should().BeTrue();
        _permissionRepoMock.Verify(
            x => x.HasPermissionAsync(adminId, "document:read", instanceId, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task CheckAnyPermissionAsync_WithOneMatchingPermission_ReturnsAllowed()
    {
        // Arrange
        using var context = await TestDbContextFactory.CreateWithSeedDataAsync();
        var evaluator = new PermissionEvaluator(context, _permissionRepoMock.Object, _loggerMock.Object);

        var editorId = Guid.Parse("66666666-6666-6666-6666-666666666667");
        _permissionRepoMock
            .Setup(x => x.HasPermissionAsync(editorId, "document:read", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await evaluator.CheckAnyPermissionAsync(
            "editor-user",
            ["document:delete", "document:read", "document:admin"]);

        // Assert
        result.Allowed.Should().BeTrue();
    }

    [Fact]
    public async Task CheckAnyPermissionAsync_WithNoMatchingPermissions_ReturnsDenied()
    {
        // Arrange
        using var context = await TestDbContextFactory.CreateWithSeedDataAsync();
        var evaluator = new PermissionEvaluator(context, _permissionRepoMock.Object, _loggerMock.Object);

        var viewerId = Guid.Parse("66666666-6666-6666-6666-666666666668");
        _permissionRepoMock
            .Setup(x => x.HasPermissionAsync(viewerId, It.IsAny<string>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var result = await evaluator.CheckAnyPermissionAsync(
            "viewer-user",
            ["document:delete", "document:admin"]);

        // Assert
        result.Allowed.Should().BeFalse();
        result.Reason.Should().Be("None of the required permissions found");
    }

    [Fact]
    public async Task CheckAnyPermissionAsync_WithNonExistentSubject_ReturnsDenied()
    {
        // Arrange
        using var context = TestDbContextFactory.Create();
        var evaluator = new PermissionEvaluator(context, _permissionRepoMock.Object, _loggerMock.Object);

        // Act
        var result = await evaluator.CheckAnyPermissionAsync(
            "non-existent",
            ["document:read", "document:write"]);

        // Assert
        result.Allowed.Should().BeFalse();
        // Note: CheckAnyPermissionAsync doesn't propagate "Subject not found" reason,
        // it returns the generic "None of the required permissions found" message
        result.Reason.Should().Be("None of the required permissions found");
    }

    [Fact]
    public async Task GetPermissionsAsync_WithValidSubject_ReturnsPermissions()
    {
        // Arrange
        using var context = await TestDbContextFactory.CreateWithSeedDataAsync();
        var evaluator = new PermissionEvaluator(context, _permissionRepoMock.Object, _loggerMock.Object);

        var adminId = Guid.Parse("66666666-6666-6666-6666-666666666666");
        var expectedPermissions = new List<string> { "document:read", "document:write", "document:delete" };

        _permissionRepoMock
            .Setup(x => x.GetPermissionsForSubjectAsync(adminId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedPermissions);

        // Act
        var result = await evaluator.GetPermissionsAsync("admin-user");

        // Assert
        result.Should().BeEquivalentTo(expectedPermissions);
    }

    [Fact]
    public async Task GetPermissionsAsync_WithApplicationFilter_PassesAppCode()
    {
        // Arrange
        using var context = await TestDbContextFactory.CreateWithSeedDataAsync();
        var evaluator = new PermissionEvaluator(context, _permissionRepoMock.Object, _loggerMock.Object);

        var adminId = Guid.Parse("66666666-6666-6666-6666-666666666666");

        _permissionRepoMock
            .Setup(x => x.GetPermissionsForSubjectAsync(adminId, "test-app", It.IsAny<CancellationToken>()))
            .ReturnsAsync(["document:read"]);

        // Act
        var result = await evaluator.GetPermissionsAsync("admin-user", "test-app");

        // Assert
        _permissionRepoMock.Verify(
            x => x.GetPermissionsForSubjectAsync(adminId, "test-app", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetPermissionsAsync_WithNonExistentSubject_ReturnsEmpty()
    {
        // Arrange
        using var context = TestDbContextFactory.Create();
        var evaluator = new PermissionEvaluator(context, _permissionRepoMock.Object, _loggerMock.Object);

        // Act
        var result = await evaluator.GetPermissionsAsync("non-existent");

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetPermissionsAsync_WithInactiveSubject_ReturnsEmpty()
    {
        // Arrange
        using var context = await TestDbContextFactory.CreateWithSeedDataAsync();
        var evaluator = new PermissionEvaluator(context, _permissionRepoMock.Object, _loggerMock.Object);

        // Deactivate admin
        var admin = context.Subjects.First(s => s.ExternalId == "admin-user");
        admin.IsActive = false;
        await context.SaveChangesAsync();

        // Act
        var result = await evaluator.GetPermissionsAsync("admin-user");

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetRolesAsync_WithValidSubject_ReturnsRoles()
    {
        // Arrange
        using var context = await TestDbContextFactory.CreateWithSeedDataAsync();
        var evaluator = new PermissionEvaluator(context, _permissionRepoMock.Object, _loggerMock.Object);

        var adminId = Guid.Parse("66666666-6666-6666-6666-666666666666");
        var expectedRoles = new List<string> { "admin" };

        _permissionRepoMock
            .Setup(x => x.GetRolesForSubjectAsync(adminId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedRoles);

        // Act
        var result = await evaluator.GetRolesAsync("admin-user");

        // Assert
        result.Should().BeEquivalentTo(expectedRoles);
    }

    [Fact]
    public async Task GetRolesAsync_WithNonExistentSubject_ReturnsEmpty()
    {
        // Arrange
        using var context = TestDbContextFactory.Create();
        var evaluator = new PermissionEvaluator(context, _permissionRepoMock.Object, _loggerMock.Object);

        // Act
        var result = await evaluator.GetRolesAsync("non-existent");

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetRolesAsync_WithInactiveSubject_ReturnsEmpty()
    {
        // Arrange
        using var context = await TestDbContextFactory.CreateWithSeedDataAsync();
        var evaluator = new PermissionEvaluator(context, _permissionRepoMock.Object, _loggerMock.Object);

        // Deactivate admin
        var admin = context.Subjects.First(s => s.ExternalId == "admin-user");
        admin.IsActive = false;
        await context.SaveChangesAsync();

        // Act
        var result = await evaluator.GetRolesAsync("admin-user");

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetRolesAsync_WithApplicationFilter_PassesAppCode()
    {
        // Arrange
        using var context = await TestDbContextFactory.CreateWithSeedDataAsync();
        var evaluator = new PermissionEvaluator(context, _permissionRepoMock.Object, _loggerMock.Object);

        var adminId = Guid.Parse("66666666-6666-6666-6666-666666666666");

        _permissionRepoMock
            .Setup(x => x.GetRolesForSubjectAsync(adminId, "test-app", It.IsAny<CancellationToken>()))
            .ReturnsAsync(["admin"]);

        // Act
        var result = await evaluator.GetRolesAsync("admin-user", "test-app");

        // Assert
        _permissionRepoMock.Verify(
            x => x.GetRolesForSubjectAsync(adminId, "test-app", It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
