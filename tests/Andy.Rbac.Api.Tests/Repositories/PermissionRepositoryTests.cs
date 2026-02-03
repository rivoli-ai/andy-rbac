using Andy.Rbac.Infrastructure.Data;
using Andy.Rbac.Infrastructure.Repositories;
using Andy.Rbac.Models;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Andy.Rbac.Api.Tests.Repositories;

public class PermissionRepositoryTests
{
    // Helper to create context with instance permissions for testing
    private static async Task<RbacDbContext> CreateContextWithInstancePermissions()
    {
        var context = await TestDbContextFactory.CreateWithSeedDataAsync();

        // Create a resource instance
        var resourceInstance = new ResourceInstance
        {
            Id = Guid.Parse("88888888-8888-8888-8888-888888888888"),
            ResourceTypeId = Guid.Parse("22222222-2222-2222-2222-222222222222"), // document type
            ExternalId = "doc-123",
            DisplayName = "Test Document",
            OwnerSubjectId = Guid.Parse("66666666-6666-6666-6666-666666666666") // admin is owner
        };
        context.ResourceInstances.Add(resourceInstance);

        // Create instance permission for viewer to read this specific document
        var instancePermission = new InstancePermission
        {
            SubjectId = Guid.Parse("66666666-6666-6666-6666-666666666669"), // no-role user
            ResourceInstanceId = resourceInstance.Id,
            PermissionId = Guid.Parse("44444444-4444-4444-4444-444444444444") // read permission
        };
        context.InstancePermissions.Add(instancePermission);

        await context.SaveChangesAsync();
        return context;
    }

    [Fact]
    public async Task GetPermissionsForSubjectAsync_WithValidSubject_ReturnsPermissions()
    {
        // Arrange
        using var context = await TestDbContextFactory.CreateWithSeedDataAsync();
        var repository = new PermissionRepository(context);
        var adminId = Guid.Parse("66666666-6666-6666-6666-666666666666");

        // Act
        var permissions = await repository.GetPermissionsForSubjectAsync(adminId);

        // Assert
        permissions.Should().Contain("test-app:document:read");
        permissions.Should().Contain("test-app:document:write");
        permissions.Should().Contain("test-app:document:delete");
    }

    [Fact]
    public async Task GetPermissionsForSubjectAsync_WithEditorRole_ReturnsLimitedPermissions()
    {
        // Arrange
        using var context = await TestDbContextFactory.CreateWithSeedDataAsync();
        var repository = new PermissionRepository(context);
        var editorId = Guid.Parse("66666666-6666-6666-6666-666666666667");

        // Act
        var permissions = await repository.GetPermissionsForSubjectAsync(editorId);

        // Assert
        permissions.Should().Contain("test-app:document:read");
        permissions.Should().Contain("test-app:document:write");
        permissions.Should().NotContain("test-app:document:delete");
    }

    [Fact]
    public async Task GetPermissionsForSubjectAsync_WithViewerRole_ReturnsReadOnly()
    {
        // Arrange
        using var context = await TestDbContextFactory.CreateWithSeedDataAsync();
        var repository = new PermissionRepository(context);
        var viewerId = Guid.Parse("66666666-6666-6666-6666-666666666668");

        // Act
        var permissions = await repository.GetPermissionsForSubjectAsync(viewerId);

        // Assert
        permissions.Should().ContainSingle();
        permissions.Should().Contain("test-app:document:read");
    }

    [Fact]
    public async Task GetPermissionsForSubjectAsync_WithNoRoles_ReturnsEmpty()
    {
        // Arrange
        using var context = await TestDbContextFactory.CreateWithSeedDataAsync();
        var repository = new PermissionRepository(context);
        var noRoleId = Guid.Parse("66666666-6666-6666-6666-666666666669");

        // Act
        var permissions = await repository.GetPermissionsForSubjectAsync(noRoleId);

        // Assert
        permissions.Should().BeEmpty();
    }

    [Fact]
    public async Task GetPermissionsForSubjectAsync_WithApplicationFilter_ReturnsFilteredPermissions()
    {
        // Arrange
        using var context = await TestDbContextFactory.CreateWithSeedDataAsync();
        var repository = new PermissionRepository(context);
        var adminId = Guid.Parse("66666666-6666-6666-6666-666666666666");

        // Act
        var permissions = await repository.GetPermissionsForSubjectAsync(adminId, "test-app");

        // Assert
        permissions.Should().NotBeEmpty();
        permissions.Should().OnlyContain(p => p.StartsWith("test-app:"));
    }

    [Fact]
    public async Task GetPermissionsForSubjectAsync_WithNonExistentApplication_ReturnsEmpty()
    {
        // Arrange
        using var context = await TestDbContextFactory.CreateWithSeedDataAsync();
        var repository = new PermissionRepository(context);
        var adminId = Guid.Parse("66666666-6666-6666-6666-666666666666");

        // Act
        var permissions = await repository.GetPermissionsForSubjectAsync(adminId, "non-existent-app");

        // Assert
        permissions.Should().BeEmpty();
    }

    [Fact]
    public async Task GetRolesForSubjectAsync_WithValidSubject_ReturnsRoles()
    {
        // Arrange
        using var context = await TestDbContextFactory.CreateWithSeedDataAsync();
        var repository = new PermissionRepository(context);
        var adminId = Guid.Parse("66666666-6666-6666-6666-666666666666");

        // Act
        var roles = await repository.GetRolesForSubjectAsync(adminId);

        // Assert
        roles.Should().ContainSingle();
        roles.Should().Contain("admin");
    }

    [Fact]
    public async Task GetRolesForSubjectAsync_WithNoRoles_ReturnsEmpty()
    {
        // Arrange
        using var context = await TestDbContextFactory.CreateWithSeedDataAsync();
        var repository = new PermissionRepository(context);
        var noRoleId = Guid.Parse("66666666-6666-6666-6666-666666666669");

        // Act
        var roles = await repository.GetRolesForSubjectAsync(noRoleId);

        // Assert
        roles.Should().BeEmpty();
    }

    [Fact]
    public async Task HasPermissionAsync_WithValidPermission_ReturnsTrue()
    {
        // Arrange
        using var context = await TestDbContextFactory.CreateWithSeedDataAsync();
        var repository = new PermissionRepository(context);
        var adminId = Guid.Parse("66666666-6666-6666-6666-666666666666");

        // Act
        var hasPermission = await repository.HasPermissionAsync(adminId, "test-app:document:read");

        // Assert
        hasPermission.Should().BeTrue();
    }

    [Fact]
    public async Task HasPermissionAsync_WithoutPermission_ReturnsFalse()
    {
        // Arrange
        using var context = await TestDbContextFactory.CreateWithSeedDataAsync();
        var repository = new PermissionRepository(context);
        var viewerId = Guid.Parse("66666666-6666-6666-6666-666666666668");

        // Act
        var hasPermission = await repository.HasPermissionAsync(viewerId, "test-app:document:delete");

        // Assert
        hasPermission.Should().BeFalse();
    }

    [Fact]
    public async Task HasPermissionAsync_WithInvalidPermissionFormat_ReturnsFalse()
    {
        // Arrange
        using var context = await TestDbContextFactory.CreateWithSeedDataAsync();
        var repository = new PermissionRepository(context);
        var adminId = Guid.Parse("66666666-6666-6666-6666-666666666666");

        // Act
        var hasPermission = await repository.HasPermissionAsync(adminId, "invalid-format");

        // Assert
        hasPermission.Should().BeFalse();
    }

    [Fact]
    public async Task HasPermissionAsync_WithNoRoles_ReturnsFalse()
    {
        // Arrange
        using var context = await TestDbContextFactory.CreateWithSeedDataAsync();
        var repository = new PermissionRepository(context);
        var noRoleId = Guid.Parse("66666666-6666-6666-6666-666666666669");

        // Act
        var hasPermission = await repository.HasPermissionAsync(noRoleId, "test-app:document:read");

        // Assert
        hasPermission.Should().BeFalse();
    }

    [Fact]
    public async Task HasPermissionAsync_WithNonExistentSubject_ReturnsFalse()
    {
        // Arrange
        using var context = await TestDbContextFactory.CreateWithSeedDataAsync();
        var repository = new PermissionRepository(context);

        // Act
        var hasPermission = await repository.HasPermissionAsync(Guid.NewGuid(), "test-app:document:read");

        // Assert
        hasPermission.Should().BeFalse();
    }

    [Fact]
    public async Task HasPermissionAsync_WithExpiredRole_ReturnsFalse()
    {
        // Arrange
        using var context = await TestDbContextFactory.CreateWithSeedDataAsync();
        var repository = new PermissionRepository(context);

        // Expire the admin role assignment
        var adminRoleAssignment = context.SubjectRoles.First(sr =>
            sr.SubjectId == Guid.Parse("66666666-6666-6666-6666-666666666666"));
        adminRoleAssignment.ExpiresAt = DateTimeOffset.UtcNow.AddDays(-1);
        await context.SaveChangesAsync();

        // Act
        var hasPermission = await repository.HasPermissionAsync(
            Guid.Parse("66666666-6666-6666-6666-666666666666"),
            "test-app:document:read");

        // Assert
        hasPermission.Should().BeFalse();
    }

    [Fact]
    public async Task HasPermissionAsync_WithFutureExpiryRole_ReturnsTrue()
    {
        // Arrange
        using var context = await TestDbContextFactory.CreateWithSeedDataAsync();
        var repository = new PermissionRepository(context);

        // Set expiry in the future
        var adminRoleAssignment = context.SubjectRoles.First(sr =>
            sr.SubjectId == Guid.Parse("66666666-6666-6666-6666-666666666666"));
        adminRoleAssignment.ExpiresAt = DateTimeOffset.UtcNow.AddDays(7);
        await context.SaveChangesAsync();

        // Act
        var hasPermission = await repository.HasPermissionAsync(
            Guid.Parse("66666666-6666-6666-6666-666666666666"),
            "test-app:document:read");

        // Assert
        hasPermission.Should().BeTrue();
    }

    [Fact]
    public async Task HasPermissionAsync_WithInstancePermission_ReturnsTrue()
    {
        // Arrange
        using var context = await CreateContextWithInstancePermissions();
        var repository = new PermissionRepository(context);
        var noRoleId = Guid.Parse("66666666-6666-6666-6666-666666666669");

        // Act - no-role user has instance permission for doc-123
        var hasPermission = await repository.HasPermissionAsync(
            noRoleId,
            "test-app:document:read",
            "doc-123");

        // Assert
        hasPermission.Should().BeTrue();
    }

    [Fact]
    public async Task HasPermissionAsync_WithInstancePermission_DifferentInstance_ReturnsFalse()
    {
        // Arrange
        using var context = await CreateContextWithInstancePermissions();
        var repository = new PermissionRepository(context);
        var noRoleId = Guid.Parse("66666666-6666-6666-6666-666666666669");

        // Act - no-role user doesn't have permission for different doc
        var hasPermission = await repository.HasPermissionAsync(
            noRoleId,
            "test-app:document:read",
            "doc-999");

        // Assert
        hasPermission.Should().BeFalse();
    }

    [Fact]
    public async Task HasPermissionAsync_WithOwnership_ReturnsTrue()
    {
        // Arrange
        using var context = await CreateContextWithInstancePermissions();
        var repository = new PermissionRepository(context);
        var adminId = Guid.Parse("66666666-6666-6666-6666-666666666666");

        // Act - admin is owner of doc-123, should have permission even without explicit grant
        // First, remove admin's roles temporarily for testing ownership
        var adminRoles = await context.SubjectRoles.Where(sr => sr.SubjectId == adminId).ToListAsync();
        context.SubjectRoles.RemoveRange(adminRoles);
        await context.SaveChangesAsync();

        var hasPermission = await repository.HasPermissionAsync(
            adminId,
            "test-app:document:read",
            "doc-123");

        // Assert
        hasPermission.Should().BeTrue(); // Owner has access
    }

    [Fact]
    public async Task HasPermissionAsync_WithExpiredInstancePermission_ReturnsFalse()
    {
        // Arrange
        using var context = await CreateContextWithInstancePermissions();
        var repository = new PermissionRepository(context);
        var noRoleId = Guid.Parse("66666666-6666-6666-6666-666666666669");

        // Expire the instance permission
        var instancePerm = await context.InstancePermissions.FirstAsync(ip => ip.SubjectId == noRoleId);
        instancePerm.ExpiresAt = DateTimeOffset.UtcNow.AddDays(-1);
        await context.SaveChangesAsync();

        // Act
        var hasPermission = await repository.HasPermissionAsync(
            noRoleId,
            "test-app:document:read",
            "doc-123");

        // Assert
        hasPermission.Should().BeFalse();
    }

    // Note: GetInstancePermissionsAsync tests are skipped because the repository method has an EF Core
    // limitation with Include after Select that doesn't work with in-memory provider.

    [Fact]
    public async Task GetRolesForSubjectAsync_WithApplicationFilter_ReturnsFilteredRoles()
    {
        // Arrange
        using var context = await TestDbContextFactory.CreateWithSeedDataAsync();
        var repository = new PermissionRepository(context);
        var adminId = Guid.Parse("66666666-6666-6666-6666-666666666666");

        // Act
        var roles = await repository.GetRolesForSubjectAsync(adminId, "test-app");

        // Assert
        roles.Should().ContainSingle();
        roles.Should().Contain("admin");
    }

    [Fact]
    public async Task GetRolesForSubjectAsync_WithExpiredRole_ExcludesExpired()
    {
        // Arrange
        using var context = await TestDbContextFactory.CreateWithSeedDataAsync();
        var repository = new PermissionRepository(context);
        var adminId = Guid.Parse("66666666-6666-6666-6666-666666666666");

        // Expire the admin role
        var adminRoleAssignment = await context.SubjectRoles.FirstAsync(sr => sr.SubjectId == adminId);
        adminRoleAssignment.ExpiresAt = DateTimeOffset.UtcNow.AddDays(-1);
        await context.SaveChangesAsync();

        // Act
        var roles = await repository.GetRolesForSubjectAsync(adminId);

        // Assert
        roles.Should().BeEmpty();
    }

    [Fact]
    public async Task HasPermissionAsync_WithResourceInstanceRoleScope_ReturnsTrue()
    {
        // Arrange
        using var context = await CreateContextWithInstancePermissions();
        var repository = new PermissionRepository(context);
        var noRoleId = Guid.Parse("66666666-6666-6666-6666-666666666669");

        // Create a scoped role assignment for this instance
        var scopedRoleAssignment = new SubjectRole
        {
            SubjectId = noRoleId,
            RoleId = Guid.Parse("55555555-5555-5555-5555-555555555556"), // editor role
            ResourceInstanceId = "doc-123"
        };
        context.SubjectRoles.Add(scopedRoleAssignment);
        await context.SaveChangesAsync();

        // Act - should have editor permissions on this specific instance
        var hasPermission = await repository.HasPermissionAsync(
            noRoleId,
            "test-app:document:write",
            "doc-123");

        // Assert
        hasPermission.Should().BeTrue();
    }

    [Fact]
    public async Task GetPermissionsForSubjectAsync_ExcludesResourceInstanceScopedRoles()
    {
        // Arrange
        using var context = await TestDbContextFactory.CreateWithSeedDataAsync();
        var repository = new PermissionRepository(context);
        var noRoleId = Guid.Parse("66666666-6666-6666-6666-666666666669");

        // Add a scoped role assignment (should not appear in global permissions)
        var scopedRoleAssignment = new SubjectRole
        {
            SubjectId = noRoleId,
            RoleId = Guid.Parse("55555555-5555-5555-5555-555555555556"), // editor role
            ResourceInstanceId = "doc-123" // Scoped to specific instance
        };
        context.SubjectRoles.Add(scopedRoleAssignment);
        await context.SaveChangesAsync();

        // Act
        var permissions = await repository.GetPermissionsForSubjectAsync(noRoleId);

        // Assert - should be empty because role is scoped to instance
        permissions.Should().BeEmpty();
    }
}
