using Andy.Rbac.Authorization;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Xunit;

namespace Andy.Rbac.Api.Tests.Authorization;

public class RequirePermissionAttributeTests
{
    [Fact]
    public void Constructor_SetsPermission()
    {
        // Act
        var attribute = new RequirePermissionAttribute("document:read");

        // Assert
        attribute.Permission.Should().Be("document:read");
    }

    [Fact]
    public void Constructor_SetsPolicy()
    {
        // Act
        var attribute = new RequirePermissionAttribute("test-app:document:write");

        // Assert
        attribute.Policy.Should().Be("Permission:test-app:document:write");
    }

    [Fact]
    public void ResourceIdParameter_CanBeSet()
    {
        // Act
        var attribute = new RequirePermissionAttribute("document:read")
        {
            ResourceIdParameter = "documentId"
        };

        // Assert
        attribute.ResourceIdParameter.Should().Be("documentId");
    }

    [Fact]
    public void ResourceIdFromBody_DefaultsToFalse()
    {
        // Act
        var attribute = new RequirePermissionAttribute("document:read");

        // Assert
        attribute.ResourceIdFromBody.Should().BeFalse();
    }

    [Fact]
    public void ResourceIdFromBody_CanBeSet()
    {
        // Act
        var attribute = new RequirePermissionAttribute("document:read")
        {
            ResourceIdFromBody = true
        };

        // Assert
        attribute.ResourceIdFromBody.Should().BeTrue();
    }

    [Fact]
    public void ResourceIdBodyPath_CanBeSet()
    {
        // Act
        var attribute = new RequirePermissionAttribute("document:read")
        {
            ResourceIdFromBody = true,
            ResourceIdBodyPath = "data.id"
        };

        // Assert
        attribute.ResourceIdBodyPath.Should().Be("data.id");
    }

    [Fact]
    public void Implements_IAuthorizationRequirement()
    {
        // Act
        var attribute = new RequirePermissionAttribute("document:read");

        // Assert
        attribute.Should().BeAssignableTo<IAuthorizationRequirement>();
    }

    [Fact]
    public void Inherits_AuthorizeAttribute()
    {
        // Act
        var attribute = new RequirePermissionAttribute("document:read");

        // Assert
        attribute.Should().BeAssignableTo<AuthorizeAttribute>();
    }

    [Fact]
    public void AllowsMultiple_IsTrue()
    {
        // Act
        var attributeUsage = typeof(RequirePermissionAttribute)
            .GetCustomAttributes(typeof(AttributeUsageAttribute), false)
            .Cast<AttributeUsageAttribute>()
            .Single();

        // Assert
        attributeUsage.AllowMultiple.Should().BeTrue();
    }

    [Fact]
    public void ValidTargets_IncludeClassAndMethod()
    {
        // Act
        var attributeUsage = typeof(RequirePermissionAttribute)
            .GetCustomAttributes(typeof(AttributeUsageAttribute), false)
            .Cast<AttributeUsageAttribute>()
            .Single();

        // Assert
        attributeUsage.ValidOn.Should().HaveFlag(AttributeTargets.Class);
        attributeUsage.ValidOn.Should().HaveFlag(AttributeTargets.Method);
    }
}

public class RequireAnyPermissionAttributeTests
{
    [Fact]
    public void Constructor_SetsPermissions()
    {
        // Act
        var attribute = new RequireAnyPermissionAttribute("document:read", "document:write");

        // Assert
        attribute.Permissions.Should().HaveCount(2);
        attribute.Permissions.Should().Contain("document:read");
        attribute.Permissions.Should().Contain("document:write");
    }

    [Fact]
    public void Constructor_SetsPolicyWithCommaSeparatedPermissions()
    {
        // Act
        var attribute = new RequireAnyPermissionAttribute("document:read", "document:write");

        // Assert
        attribute.Policy.Should().Be("AnyPermission:document:read,document:write");
    }

    [Fact]
    public void Constructor_WithSinglePermission_SetsPolicy()
    {
        // Act
        var attribute = new RequireAnyPermissionAttribute("document:read");

        // Assert
        attribute.Policy.Should().Be("AnyPermission:document:read");
        attribute.Permissions.Should().ContainSingle();
    }

    [Fact]
    public void Implements_IAuthorizationRequirement()
    {
        // Act
        var attribute = new RequireAnyPermissionAttribute("document:read");

        // Assert
        attribute.Should().BeAssignableTo<IAuthorizationRequirement>();
    }

    [Fact]
    public void Inherits_AuthorizeAttribute()
    {
        // Act
        var attribute = new RequireAnyPermissionAttribute("document:read");

        // Assert
        attribute.Should().BeAssignableTo<AuthorizeAttribute>();
    }

    [Fact]
    public void AllowsMultiple_IsTrue()
    {
        // Act
        var attributeUsage = typeof(RequireAnyPermissionAttribute)
            .GetCustomAttributes(typeof(AttributeUsageAttribute), false)
            .Cast<AttributeUsageAttribute>()
            .Single();

        // Assert
        attributeUsage.AllowMultiple.Should().BeTrue();
    }
}

public class RequireRoleAttributeTests
{
    [Fact]
    public void Constructor_SetsRole()
    {
        // Act
        var attribute = new RequireRoleAttribute("admin");

        // Assert
        attribute.Role.Should().Be("admin");
    }

    [Fact]
    public void Constructor_SetsPolicy()
    {
        // Act
        var attribute = new RequireRoleAttribute("editor");

        // Assert
        attribute.Policy.Should().Be("Role:editor");
    }

    [Fact]
    public void Implements_IAuthorizationRequirement()
    {
        // Act
        var attribute = new RequireRoleAttribute("admin");

        // Assert
        attribute.Should().BeAssignableTo<IAuthorizationRequirement>();
    }

    [Fact]
    public void Inherits_AuthorizeAttribute()
    {
        // Act
        var attribute = new RequireRoleAttribute("admin");

        // Assert
        attribute.Should().BeAssignableTo<AuthorizeAttribute>();
    }

    [Fact]
    public void AllowsMultiple_IsTrue()
    {
        // Act
        var attributeUsage = typeof(RequireRoleAttribute)
            .GetCustomAttributes(typeof(AttributeUsageAttribute), false)
            .Cast<AttributeUsageAttribute>()
            .Single();

        // Assert
        attributeUsage.AllowMultiple.Should().BeTrue();
    }
}
