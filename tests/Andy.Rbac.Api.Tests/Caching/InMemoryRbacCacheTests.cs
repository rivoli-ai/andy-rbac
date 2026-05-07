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

    // --- Issue #47: cache key includes applicationCode + groups ---

    [Fact]
    public async Task SetPermissions_DifferentApplicationCode_KeepsSeparateBuckets()
    {
        // Without applicationCode in the key, a cache hit for "all apps"
        // would incorrectly satisfy a later request for "app-X only" (or
        // vice versa). Each tuple gets its own bucket.
        await _cache.SetPermissionsAsync("user-1", new List<string> { "x:y:read" }, applicationCode: "app-x");
        await _cache.SetPermissionsAsync("user-1", new List<string> { "y:y:read" }, applicationCode: "app-y");

        var x = await _cache.GetPermissionsAsync("user-1", applicationCode: "app-x");
        var y = await _cache.GetPermissionsAsync("user-1", applicationCode: "app-y");
        var unscoped = await _cache.GetPermissionsAsync("user-1", applicationCode: null);

        x.Should().BeEquivalentTo(new[] { "x:y:read" });
        y.Should().BeEquivalentTo(new[] { "y:y:read" });
        unscoped.Should().BeNull("the unscoped tuple is a separate cache key");
    }

    [Fact]
    public async Task SetPermissions_DifferentGroups_KeepsSeparateBuckets()
    {
        await _cache.SetPermissionsAsync("user-1", new List<string> { "from-engs" }, groups: new[] { "engineers" });
        await _cache.SetPermissionsAsync("user-1", new List<string> { "from-admins" }, groups: new[] { "admins" });

        var engs = await _cache.GetPermissionsAsync("user-1", groups: new[] { "engineers" });
        var admins = await _cache.GetPermissionsAsync("user-1", groups: new[] { "admins" });

        engs.Should().BeEquivalentTo(new[] { "from-engs" });
        admins.Should().BeEquivalentTo(new[] { "from-admins" });
    }

    [Fact]
    public async Task SetPermissions_GroupOrderDoesNotMatter()
    {
        // Same set, different ordering — must hit the same bucket.
        await _cache.SetPermissionsAsync("user-1", new List<string> { "p" }, groups: new[] { "a", "b" });
        var hit = await _cache.GetPermissionsAsync("user-1", groups: new[] { "b", "a" });

        hit.Should().BeEquivalentTo(new[] { "p" });
    }

    [Fact]
    public async Task InvalidateAsync_ClearsAllVariantsForSubject()
    {
        // Subject has cached entries for several (app, groups) tuples.
        // InvalidateAsync should drop every one of them.
        await _cache.SetPermissionsAsync("user-1", new List<string> { "p" }, applicationCode: "app-a");
        await _cache.SetPermissionsAsync("user-1", new List<string> { "p" }, applicationCode: "app-b");
        await _cache.SetPermissionsAsync("user-1", new List<string> { "p" }, groups: new[] { "g1" });
        await _cache.SetRolesAsync("user-1", new List<string> { "r" }, applicationCode: "app-a");

        await _cache.InvalidateAsync("user-1");

        (await _cache.GetPermissionsAsync("user-1", applicationCode: "app-a")).Should().BeNull();
        (await _cache.GetPermissionsAsync("user-1", applicationCode: "app-b")).Should().BeNull();
        (await _cache.GetPermissionsAsync("user-1", groups: new[] { "g1" })).Should().BeNull();
        (await _cache.GetRolesAsync("user-1", applicationCode: "app-a")).Should().BeNull();
    }

    [Fact]
    public async Task InvalidateAllAsync_MakesPriorEntriesUnreadable()
    {
        // Generation bump: stored entries become unreachable from the public
        // Get path even though they may still occupy IMemoryCache until TTL.
        await _cache.SetPermissionsAsync("user-1", new List<string> { "p" }, applicationCode: "app-a");
        await _cache.SetRolesAsync("user-2", new List<string> { "r" });

        await _cache.InvalidateAllAsync();

        (await _cache.GetPermissionsAsync("user-1", applicationCode: "app-a")).Should().BeNull();
        (await _cache.GetRolesAsync("user-2")).Should().BeNull();
    }

    [Fact]
    public async Task InvalidateAllAsync_NewWritesAfterBumpAreReadable()
    {
        // Sanity: after invalidation, the cache still works for fresh writes.
        await _cache.SetPermissionsAsync("user-1", new List<string> { "old" });
        await _cache.InvalidateAllAsync();
        await _cache.SetPermissionsAsync("user-1", new List<string> { "new" });

        var result = await _cache.GetPermissionsAsync("user-1");
        result.Should().BeEquivalentTo(new[] { "new" });
    }
}
