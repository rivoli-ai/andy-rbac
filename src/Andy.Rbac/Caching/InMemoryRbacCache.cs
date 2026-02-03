using Andy.Rbac.Abstractions;
using Andy.Rbac.Configuration;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Andy.Rbac.Caching;

/// <summary>
/// In-memory implementation of RBAC cache.
/// </summary>
public class InMemoryRbacCache : IRbacCache
{
    private readonly IMemoryCache _cache;
    private readonly RbacCacheOptions _options;

    private const string PermissionsCacheKeyPrefix = "rbac:permissions:";
    private const string RolesCacheKeyPrefix = "rbac:roles:";

    public InMemoryRbacCache(IMemoryCache cache, IOptions<RbacOptions> options)
    {
        _cache = cache;
        _options = options.Value.Cache;
    }

    public Task<IReadOnlyList<string>?> GetPermissionsAsync(string subjectId, CancellationToken ct = default)
    {
        var key = $"{PermissionsCacheKeyPrefix}{subjectId}";
        var result = _cache.TryGetValue(key, out IReadOnlyList<string>? permissions)
            ? permissions
            : null;
        return Task.FromResult(result);
    }

    public Task SetPermissionsAsync(string subjectId, IReadOnlyList<string> permissions, CancellationToken ct = default)
    {
        var key = $"{PermissionsCacheKeyPrefix}{subjectId}";
        _cache.Set(key, permissions, _options.Expiration);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<string>?> GetRolesAsync(string subjectId, CancellationToken ct = default)
    {
        var key = $"{RolesCacheKeyPrefix}{subjectId}";
        var result = _cache.TryGetValue(key, out IReadOnlyList<string>? roles)
            ? roles
            : null;
        return Task.FromResult(result);
    }

    public Task SetRolesAsync(string subjectId, IReadOnlyList<string> roles, CancellationToken ct = default)
    {
        var key = $"{RolesCacheKeyPrefix}{subjectId}";
        _cache.Set(key, roles, _options.Expiration);
        return Task.CompletedTask;
    }

    public Task InvalidateAsync(string subjectId, CancellationToken ct = default)
    {
        _cache.Remove($"{PermissionsCacheKeyPrefix}{subjectId}");
        _cache.Remove($"{RolesCacheKeyPrefix}{subjectId}");
        return Task.CompletedTask;
    }

    public Task InvalidateAllAsync(CancellationToken ct = default)
    {
        // IMemoryCache doesn't support clearing all entries
        // In production, use a distributed cache with proper invalidation
        // For in-memory, this is a no-op (entries will expire naturally)
        return Task.CompletedTask;
    }
}
