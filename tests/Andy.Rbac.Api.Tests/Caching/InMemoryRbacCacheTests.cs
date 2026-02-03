using Andy.Rbac.Caching;
using Andy.Rbac.Configuration;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Xunit;

namespace Andy.Rbac.Api.Tests.Caching;

public class InMemoryRbacCacheTests
{
    private readonly IMemoryCache _memoryCache;
    private readonly InMemoryRbacCache _cache;

    public InMemoryRbacCacheTests()
    {
        _memoryCache = new MemoryCache(new MemoryCacheOptions());
        var options = Options.Create(new RbacOptions
        {
            ApplicationCode = "test-app",
            Cache = new RbacCacheOptions
            {
                Enabled = true,
                Expiration = TimeSpan.FromMinutes(5)
            }
        });
        _cache = new InMemoryRbacCache(_memoryCache, options);
    }

    [Fact]
    public async Task GetPermissionsAsync_WithNoCache_ReturnsNull()
    {
        // Act
        var result = await _cache.GetPermissionsAsync("user-123");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task SetPermissionsAsync_ThenGet_ReturnsCachedPermissions()
    {
        // Arrange
        var permissions = new List<string> { "test-app:document:read", "test-app:document:write" };

        // Act
        await _cache.SetPermissionsAsync("user-123", permissions);
        var result = await _cache.GetPermissionsAsync("user-123");

        // Assert
        result.Should().NotBeNull();
        result.Should().BeEquivalentTo(permissions);
    }

    [Fact]
    public async Task GetRolesAsync_WithNoCache_ReturnsNull()
    {
        // Act
        var result = await _cache.GetRolesAsync("user-123");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task SetRolesAsync_ThenGet_ReturnsCachedRoles()
    {
        // Arrange
        var roles = new List<string> { "admin", "editor" };

        // Act
        await _cache.SetRolesAsync("user-123", roles);
        var result = await _cache.GetRolesAsync("user-123");

        // Assert
        result.Should().NotBeNull();
        result.Should().BeEquivalentTo(roles);
    }

    [Fact]
    public async Task InvalidateAsync_ClearsPermissionsAndRoles()
    {
        // Arrange
        await _cache.SetPermissionsAsync("user-123", new List<string> { "permission" });
        await _cache.SetRolesAsync("user-123", new List<string> { "role" });

        // Act
        await _cache.InvalidateAsync("user-123");

        // Assert
        var permissions = await _cache.GetPermissionsAsync("user-123");
        var roles = await _cache.GetRolesAsync("user-123");
        permissions.Should().BeNull();
        roles.Should().BeNull();
    }

    [Fact]
    public async Task InvalidateAllAsync_CompletesWithoutError()
    {
        // Arrange
        await _cache.SetPermissionsAsync("user-1", new List<string> { "permission" });
        await _cache.SetPermissionsAsync("user-2", new List<string> { "permission" });

        // Act & Assert - should not throw
        await _cache.InvalidateAllAsync();
    }

    [Fact]
    public async Task SetPermissionsAsync_MultipleUsers_KeepsSeparateCache()
    {
        // Arrange
        var permissions1 = new List<string> { "perm1" };
        var permissions2 = new List<string> { "perm2" };

        // Act
        await _cache.SetPermissionsAsync("user-1", permissions1);
        await _cache.SetPermissionsAsync("user-2", permissions2);

        // Assert
        var result1 = await _cache.GetPermissionsAsync("user-1");
        var result2 = await _cache.GetPermissionsAsync("user-2");
        result1.Should().BeEquivalentTo(permissions1);
        result2.Should().BeEquivalentTo(permissions2);
    }

    [Fact]
    public async Task InvalidateAsync_OnlyAffectsSpecifiedUser()
    {
        // Arrange
        await _cache.SetPermissionsAsync("user-1", new List<string> { "perm1" });
        await _cache.SetPermissionsAsync("user-2", new List<string> { "perm2" });

        // Act
        await _cache.InvalidateAsync("user-1");

        // Assert
        var result1 = await _cache.GetPermissionsAsync("user-1");
        var result2 = await _cache.GetPermissionsAsync("user-2");
        result1.Should().BeNull();
        result2.Should().NotBeNull();
    }

    [Fact]
    public async Task SetPermissionsAsync_OverwritesExisting()
    {
        // Arrange
        await _cache.SetPermissionsAsync("user-123", new List<string> { "old" });

        // Act
        await _cache.SetPermissionsAsync("user-123", new List<string> { "new" });

        // Assert
        var result = await _cache.GetPermissionsAsync("user-123");
        result.Should().BeEquivalentTo(new[] { "new" });
    }

    [Fact]
    public async Task SetRolesAsync_OverwritesExisting()
    {
        // Arrange
        await _cache.SetRolesAsync("user-123", new List<string> { "old-role" });

        // Act
        await _cache.SetRolesAsync("user-123", new List<string> { "new-role" });

        // Assert
        var result = await _cache.GetRolesAsync("user-123");
        result.Should().BeEquivalentTo(new[] { "new-role" });
    }

    [Fact]
    public async Task SetPermissionsAsync_EmptyList_StoresEmptyList()
    {
        // Act
        await _cache.SetPermissionsAsync("user-123", new List<string>());
        var result = await _cache.GetPermissionsAsync("user-123");

        // Assert
        result.Should().NotBeNull();
        result.Should().BeEmpty();
    }
}
