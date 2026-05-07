using System.Security.Cryptography;
using System.Text;
using Andy.Rbac.Abstractions;
using Andy.Rbac.Configuration;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Andy.Rbac.Caching;

/// <summary>
/// In-memory implementation of RBAC cache.
///
/// Issue #47: cache entries are keyed by the full
/// <c>(subjectId, applicationCode, groups)</c> tuple — a request for app A
/// no longer collides with a request for app B for the same subject. A
/// monotonic generation counter is bumped by <see cref="InvalidateAllAsync"/>;
/// every read/write composes the current generation into the key, so old
/// entries become unreachable immediately (and expire on their own TTL).
/// </summary>
public class InMemoryRbacCache : IRbacCache
{
    private readonly IMemoryCache _cache;
    private readonly RbacCacheOptions _options;

    private const string PermissionsCacheKeyPrefix = "rbac:perms";
    private const string RolesCacheKeyPrefix = "rbac:roles";
    private const string SubjectIndexPrefix = "rbac:idx:sub:";
    private long _generation;

    public InMemoryRbacCache(IMemoryCache cache, IOptions<RbacOptions> options)
    {
        _cache = cache;
        _options = options.Value.Cache;
    }

    public Task<IReadOnlyList<string>?> GetPermissionsAsync(
        string subjectId,
        string? applicationCode = null,
        IEnumerable<string>? groups = null,
        CancellationToken ct = default)
    {
        var key = BuildKey(PermissionsCacheKeyPrefix, subjectId, applicationCode, groups);
        var result = _cache.TryGetValue(key, out IReadOnlyList<string>? permissions)
            ? permissions
            : null;
        return Task.FromResult(result);
    }

    public Task SetPermissionsAsync(
        string subjectId,
        IReadOnlyList<string> permissions,
        string? applicationCode = null,
        IEnumerable<string>? groups = null,
        CancellationToken ct = default)
    {
        var key = BuildKey(PermissionsCacheKeyPrefix, subjectId, applicationCode, groups);
        _cache.Set(key, permissions, _options.Expiration);
        TrackKeyForSubject(subjectId, key);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<string>?> GetRolesAsync(
        string subjectId,
        string? applicationCode = null,
        IEnumerable<string>? groups = null,
        CancellationToken ct = default)
    {
        var key = BuildKey(RolesCacheKeyPrefix, subjectId, applicationCode, groups);
        var result = _cache.TryGetValue(key, out IReadOnlyList<string>? roles)
            ? roles
            : null;
        return Task.FromResult(result);
    }

    public Task SetRolesAsync(
        string subjectId,
        IReadOnlyList<string> roles,
        string? applicationCode = null,
        IEnumerable<string>? groups = null,
        CancellationToken ct = default)
    {
        var key = BuildKey(RolesCacheKeyPrefix, subjectId, applicationCode, groups);
        _cache.Set(key, roles, _options.Expiration);
        TrackKeyForSubject(subjectId, key);
        return Task.CompletedTask;
    }

    public Task InvalidateAsync(string subjectId, CancellationToken ct = default)
    {
        // Walk the per-subject index of keys we've emitted at the current
        // generation and remove them. Old-generation keys are already
        // unreachable from the current Get/Set path; they expire naturally.
        var indexKey = SubjectIndexKey(subjectId);
        if (_cache.TryGetValue(indexKey, out HashSet<string>? keys) && keys is not null)
        {
            foreach (var k in keys) _cache.Remove(k);
            _cache.Remove(indexKey);
        }
        return Task.CompletedTask;
    }

    public Task InvalidateAllAsync(CancellationToken ct = default)
    {
        // Bump generation — every existing key composed with the previous
        // generation is now unreachable from the public API. The IMemoryCache
        // entries linger until their TTL expires (memory bound by the
        // existing Expiration setting).
        Interlocked.Increment(ref _generation);
        return Task.CompletedTask;
    }

    private string BuildKey(string prefix, string subjectId, string? applicationCode, IEnumerable<string>? groups)
    {
        var gen = Interlocked.Read(ref _generation);
        var groupHash = HashGroups(groups);
        var app = string.IsNullOrEmpty(applicationCode) ? "*" : applicationCode;
        return $"{prefix}:gen{gen}:sub={subjectId}|app={app}|groups={groupHash}";
    }

    private string SubjectIndexKey(string subjectId)
    {
        var gen = Interlocked.Read(ref _generation);
        return $"{SubjectIndexPrefix}gen{gen}:{subjectId}";
    }

    private void TrackKeyForSubject(string subjectId, string cacheKey)
    {
        // Maintain a per-subject set of cache keys so InvalidateAsync can
        // sweep every (applicationCode, groups) variant without scanning
        // the whole IMemoryCache.
        var indexKey = SubjectIndexKey(subjectId);
        if (!_cache.TryGetValue(indexKey, out HashSet<string>? keys) || keys is null)
        {
            keys = new HashSet<string>();
            _cache.Set(indexKey, keys, _options.Expiration);
        }
        keys.Add(cacheKey);
    }

    private static string HashGroups(IEnumerable<string>? groups)
    {
        if (groups is null) return "none";
        var sorted = groups.Where(g => !string.IsNullOrEmpty(g)).OrderBy(g => g, StringComparer.Ordinal).ToList();
        if (sorted.Count == 0) return "none";

        var joined = string.Join(",", sorted);
        var bytes = Encoding.UTF8.GetBytes(joined);
        var hash = SHA256.HashData(bytes);
        // 16 hex chars is enough cardinality to keep collision risk negligible
        // while keeping cache keys readable.
        return Convert.ToHexString(hash, 0, 8).ToLowerInvariant();
    }
}
