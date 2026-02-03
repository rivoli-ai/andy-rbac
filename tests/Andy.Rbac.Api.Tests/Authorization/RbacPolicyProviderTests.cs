using Andy.Rbac.Authorization;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Xunit;

namespace Andy.Rbac.Api.Tests.Authorization;

public class RbacPolicyProviderTests
{
    private readonly RbacPolicyProvider _provider;

    public RbacPolicyProviderTests()
    {
        var options = Options.Create(new AuthorizationOptions());
        _provider = new RbacPolicyProvider(options);
    }

    [Fact]
    public async Task GetPolicyAsync_WithPermissionPrefix_ReturnsPermissionPolicy()
    {
        // Act
        var policy = await _provider.GetPolicyAsync("Permission:document:read");

        // Assert
        policy.Should().NotBeNull();
        policy!.Requirements.Should().ContainSingle();
        policy.Requirements[0].Should().BeOfType<PermissionRequirement>();

        var requirement = (PermissionRequirement)policy.Requirements[0];
        requirement.Permission.Should().Be("document:read");
    }

    [Fact]
    public async Task GetPolicyAsync_WithAnyPermissionPrefix_ReturnsAnyPermissionPolicy()
    {
        // Act
        var policy = await _provider.GetPolicyAsync("AnyPermission:document:read,document:write");

        // Assert
        policy.Should().NotBeNull();
        policy!.Requirements.Should().ContainSingle();
        policy.Requirements[0].Should().BeOfType<AnyPermissionRequirement>();

        var requirement = (AnyPermissionRequirement)policy.Requirements[0];
        requirement.Permissions.Should().HaveCount(2);
        requirement.Permissions.Should().Contain("document:read");
        requirement.Permissions.Should().Contain("document:write");
    }

    [Fact]
    public async Task GetPolicyAsync_WithRolePrefix_ReturnsRolePolicy()
    {
        // Act
        var policy = await _provider.GetPolicyAsync("Role:admin");

        // Assert
        policy.Should().NotBeNull();
        policy!.Requirements.Should().ContainSingle();
        policy.Requirements[0].Should().BeOfType<RoleRequirement>();

        var requirement = (RoleRequirement)policy.Requirements[0];
        requirement.Role.Should().Be("admin");
    }

    [Fact]
    public async Task GetPolicyAsync_WithUnknownPrefix_FallsBackToDefault()
    {
        // Act
        var policy = await _provider.GetPolicyAsync("UnknownPolicy");

        // Assert
        policy.Should().BeNull(); // Default provider returns null for unknown policies
    }

    [Fact]
    public async Task GetDefaultPolicyAsync_ReturnsDefaultPolicy()
    {
        // Act
        var policy = await _provider.GetDefaultPolicyAsync();

        // Assert
        policy.Should().NotBeNull();
    }

    [Fact]
    public async Task GetFallbackPolicyAsync_ReturnsFallbackPolicy()
    {
        // Act
        var policy = await _provider.GetFallbackPolicyAsync();

        // Assert - fallback is null by default
        policy.Should().BeNull();
    }

    [Fact]
    public async Task GetPolicyAsync_WithFullPermission_PreservesFullFormat()
    {
        // Act
        var policy = await _provider.GetPolicyAsync("Permission:test-app:document:read");

        // Assert
        policy.Should().NotBeNull();
        var requirement = (PermissionRequirement)policy!.Requirements[0];
        requirement.Permission.Should().Be("test-app:document:read");
    }

    [Fact]
    public async Task GetPolicyAsync_WithMultipleAnyPermissions_SplitsCorrectly()
    {
        // Act
        var policy = await _provider.GetPolicyAsync("AnyPermission:app:doc:read,app:doc:write,app:doc:delete");

        // Assert
        policy.Should().NotBeNull();
        var requirement = (AnyPermissionRequirement)policy!.Requirements[0];
        requirement.Permissions.Should().HaveCount(3);
        requirement.Permissions.Should().Contain("app:doc:read");
        requirement.Permissions.Should().Contain("app:doc:write");
        requirement.Permissions.Should().Contain("app:doc:delete");
    }
}
