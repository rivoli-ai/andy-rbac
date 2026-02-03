using Andy.Rbac.Authorization;
using FluentAssertions;
using Xunit;

namespace Andy.Rbac.Api.Tests.Authorization;

public class PermissionRequirementTests
{
    [Fact]
    public void Constructor_WithPermissionOnly_SetsPermission()
    {
        // Act
        var requirement = new PermissionRequirement("document:read");

        // Assert
        requirement.Permission.Should().Be("document:read");
        requirement.ResourceIdParameter.Should().BeNull();
    }

    [Fact]
    public void Constructor_WithResourceIdParameter_SetsResourceIdParameter()
    {
        // Act
        var requirement = new PermissionRequirement("document:read", "documentId");

        // Assert
        requirement.Permission.Should().Be("document:read");
        requirement.ResourceIdParameter.Should().Be("documentId");
    }

    [Fact]
    public void Constructor_WithNullResourceIdParameter_SetsNull()
    {
        // Act
        var requirement = new PermissionRequirement("document:read", null);

        // Assert
        requirement.ResourceIdParameter.Should().BeNull();
    }
}

public class AnyPermissionRequirementTests
{
    [Fact]
    public void Constructor_WithPermissions_SetsPermissions()
    {
        // Arrange
        var permissions = new[] { "document:read", "document:write" };

        // Act
        var requirement = new AnyPermissionRequirement(permissions);

        // Assert
        requirement.Permissions.Should().HaveCount(2);
        requirement.Permissions.Should().Contain("document:read");
        requirement.Permissions.Should().Contain("document:write");
    }

    [Fact]
    public void Permissions_IsReadOnly()
    {
        // Arrange
        var permissions = new List<string> { "document:read" };
        var requirement = new AnyPermissionRequirement(permissions);

        // Act - modifying original list
        permissions.Add("document:write");

        // Assert - requirement should not be affected
        requirement.Permissions.Should().ContainSingle();
    }

    [Fact]
    public void Constructor_WithEmptyList_CreatesEmptyPermissions()
    {
        // Act
        var requirement = new AnyPermissionRequirement(Array.Empty<string>());

        // Assert
        requirement.Permissions.Should().BeEmpty();
    }
}

public class RoleRequirementTests
{
    [Fact]
    public void Constructor_WithRole_SetsRole()
    {
        // Act
        var requirement = new RoleRequirement("admin");

        // Assert
        requirement.Role.Should().Be("admin");
    }

    [Fact]
    public void Constructor_PreservesRoleCase()
    {
        // Act
        var requirement = new RoleRequirement("AdminRole");

        // Assert
        requirement.Role.Should().Be("AdminRole");
    }
}
