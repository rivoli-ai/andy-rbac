using Andy.Rbac.Authorization;
using FluentAssertions;
using Xunit;

namespace Andy.Rbac.Tests;

public class AuthorizationHandlerTests
{
    [Fact]
    public void RequirePermissionAttribute_SetsCorrectPolicy()
    {
        // Arrange & Act
        var attribute = new RequirePermissionAttribute("document:read");

        // Assert
        attribute.Permission.Should().Be("document:read");
        attribute.Policy.Should().Be("Permission:document:read");
    }

    [Fact]
    public void RequirePermissionAttribute_WithResourceIdParameter_SetsProperty()
    {
        // Arrange & Act
        var attribute = new RequirePermissionAttribute("document:read")
        {
            ResourceIdParameter = "id"
        };

        // Assert
        attribute.ResourceIdParameter.Should().Be("id");
    }

    [Fact]
    public void RequireAnyPermissionAttribute_SetsCorrectPolicy()
    {
        // Arrange & Act
        var attribute = new RequireAnyPermissionAttribute("document:read", "document:write");

        // Assert
        attribute.Permissions.Should().BeEquivalentTo(["document:read", "document:write"]);
        attribute.Policy.Should().Be("AnyPermission:document:read,document:write");
    }

    [Fact]
    public void RequireRoleAttribute_SetsCorrectPolicy()
    {
        // Arrange & Act
        var attribute = new RequireRoleAttribute("admin");

        // Assert
        attribute.Role.Should().Be("admin");
        attribute.Policy.Should().Be("Role:admin");
    }

    [Fact]
    public void PermissionRequirement_StoresPermissionAndResourceId()
    {
        // Arrange & Act
        var requirement = new PermissionRequirement("andy-docs:document:read", "doc-123");

        // Assert
        requirement.Permission.Should().Be("andy-docs:document:read");
        requirement.ResourceIdParameter.Should().Be("doc-123");
    }
}
