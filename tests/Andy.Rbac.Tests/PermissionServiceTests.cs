using Andy.Rbac.Abstractions;
using Andy.Rbac.Caching;
using Andy.Rbac.Configuration;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Andy.Rbac.Tests;

public class PermissionServiceTests
{
    [Fact]
    public async Task HasPermissionAsync_WhenPermissionCached_ReturnsCachedResult()
    {
        // Arrange
        var cache = new MemoryCache(new MemoryCacheOptions());
        var options = Options.Create(new RbacOptions
        {
            ApplicationCode = "test-app",
            Cache = new RbacCacheOptions { Enabled = true, Expiration = TimeSpan.FromMinutes(5) }
        });

        var rbacCache = new InMemoryRbacCache(cache, options);

        var permissions = new List<string> { "test-app:document:read", "test-app:document:write" };
        await rbacCache.SetPermissionsAsync("user-123", permissions);

        // Act
        var cachedPermissions = await rbacCache.GetPermissionsAsync("user-123");

        // Assert
        cachedPermissions.Should().NotBeNull();
        cachedPermissions.Should().Contain("test-app:document:read");
        cachedPermissions.Should().Contain("test-app:document:write");
    }

    [Fact]
    public async Task InvalidateAsync_RemovesCachedPermissions()
    {
        // Arrange
        var cache = new MemoryCache(new MemoryCacheOptions());
        var options = Options.Create(new RbacOptions
        {
            ApplicationCode = "test-app",
            Cache = new RbacCacheOptions { Enabled = true }
        });

        var rbacCache = new InMemoryRbacCache(cache, options);
        await rbacCache.SetPermissionsAsync("user-123", ["test-app:document:read"]);

        // Act
        await rbacCache.InvalidateAsync("user-123");
        var cachedPermissions = await rbacCache.GetPermissionsAsync("user-123");

        // Assert
        cachedPermissions.Should().BeNull();
    }

    [Fact]
    public async Task GetRolesAsync_WhenRolesCached_ReturnsCachedRoles()
    {
        // Arrange
        var cache = new MemoryCache(new MemoryCacheOptions());
        var options = Options.Create(new RbacOptions
        {
            ApplicationCode = "test-app",
            Cache = new RbacCacheOptions { Enabled = true }
        });

        var rbacCache = new InMemoryRbacCache(cache, options);
        var roles = new List<string> { "admin", "editor" };
        await rbacCache.SetRolesAsync("user-123", roles);

        // Act
        var cachedRoles = await rbacCache.GetRolesAsync("user-123");

        // Assert
        cachedRoles.Should().NotBeNull();
        cachedRoles.Should().BeEquivalentTo(roles);
    }
}
