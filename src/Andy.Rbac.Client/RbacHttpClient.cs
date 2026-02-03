using System.Net.Http.Json;
using Andy.Rbac.Abstractions;
using Andy.Rbac.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Andy.Rbac.Client;

/// <summary>
/// HTTP client implementation for the RBAC API.
/// </summary>
public class RbacHttpClient : IRbacClient
{
    private readonly HttpClient _httpClient;
    private readonly IRbacCache _cache;
    private readonly RbacOptions _options;
    private readonly ILogger<RbacHttpClient> _logger;

    public RbacHttpClient(
        HttpClient httpClient,
        IRbacCache cache,
        IOptions<RbacOptions> options,
        ILogger<RbacHttpClient> logger)
    {
        _httpClient = httpClient;
        _cache = cache;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<bool> HasPermissionAsync(
        string subjectId,
        string permission,
        string? resourceInstanceId = null,
        CancellationToken ct = default)
    {
        // Check cache first
        if (_options.Cache.Enabled && resourceInstanceId == null)
        {
            var cachedPermissions = await _cache.GetPermissionsAsync(subjectId, ct);
            if (cachedPermissions != null)
            {
                var normalizedPermission = NormalizePermission(permission);
                return cachedPermissions.Contains(normalizedPermission);
            }
        }

        // Call API
        var request = new { SubjectId = subjectId, Permission = NormalizePermission(permission), ResourceInstanceId = resourceInstanceId };
        var response = await _httpClient.PostAsJsonAsync("api/check", request, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<CheckPermissionResponse>(ct);
        return result?.Allowed ?? false;
    }

    public async Task<bool> HasAnyPermissionAsync(
        string subjectId,
        IEnumerable<string> permissions,
        string? resourceInstanceId = null,
        CancellationToken ct = default)
    {
        var normalizedPermissions = permissions.Select(NormalizePermission).ToList();

        // Check cache first
        if (_options.Cache.Enabled && resourceInstanceId == null)
        {
            var cachedPermissions = await _cache.GetPermissionsAsync(subjectId, ct);
            if (cachedPermissions != null)
            {
                return normalizedPermissions.Any(p => cachedPermissions.Contains(p));
            }
        }

        var request = new { SubjectId = subjectId, Permissions = normalizedPermissions, ResourceInstanceId = resourceInstanceId };
        var response = await _httpClient.PostAsJsonAsync("api/check/any", request, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<CheckPermissionResponse>(ct);
        return result?.Allowed ?? false;
    }

    public async Task<bool> HasAllPermissionsAsync(
        string subjectId,
        IEnumerable<string> permissions,
        string? resourceInstanceId = null,
        CancellationToken ct = default)
    {
        var normalizedPermissions = permissions.Select(NormalizePermission).ToList();

        // Check cache first
        if (_options.Cache.Enabled && resourceInstanceId == null)
        {
            var cachedPermissions = await _cache.GetPermissionsAsync(subjectId, ct);
            if (cachedPermissions != null)
            {
                return normalizedPermissions.All(p => cachedPermissions.Contains(p));
            }
        }

        // No bulk "all" endpoint, check each
        foreach (var permission in normalizedPermissions)
        {
            if (!await HasPermissionAsync(subjectId, permission, resourceInstanceId, ct))
                return false;
        }
        return true;
    }

    public async Task<IReadOnlyList<string>> GetPermissionsAsync(
        string subjectId,
        string? applicationCode = null,
        CancellationToken ct = default)
    {
        // Check cache
        if (_options.Cache.Enabled && applicationCode == null)
        {
            var cached = await _cache.GetPermissionsAsync(subjectId, ct);
            if (cached != null)
                return cached;
        }

        var url = $"api/check/permissions/{Uri.EscapeDataString(subjectId)}";
        if (!string.IsNullOrEmpty(applicationCode))
            url += $"?applicationCode={Uri.EscapeDataString(applicationCode)}";

        var response = await _httpClient.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<GetPermissionsResponse>(ct);
        var permissions = result?.Permissions ?? [];

        // Cache results
        if (_options.Cache.Enabled && applicationCode == null)
        {
            await _cache.SetPermissionsAsync(subjectId, permissions, ct);
        }

        return permissions;
    }

    public async Task<IReadOnlyList<string>> GetRolesAsync(
        string subjectId,
        string? applicationCode = null,
        CancellationToken ct = default)
    {
        // Check cache
        if (_options.Cache.Enabled && applicationCode == null)
        {
            var cached = await _cache.GetRolesAsync(subjectId, ct);
            if (cached != null)
                return cached;
        }

        var url = $"api/check/roles/{Uri.EscapeDataString(subjectId)}";
        if (!string.IsNullOrEmpty(applicationCode))
            url += $"?applicationCode={Uri.EscapeDataString(applicationCode)}";

        var response = await _httpClient.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<GetRolesResponse>(ct);
        var roles = result?.Roles ?? [];

        // Cache results
        if (_options.Cache.Enabled && applicationCode == null)
        {
            await _cache.SetRolesAsync(subjectId, roles, ct);
        }

        return roles;
    }

    public async Task<SubjectInfo> ProvisionSubjectAsync(
        string externalId,
        string provider,
        string? email = null,
        string? displayName = null,
        Dictionary<string, object>? metadata = null,
        CancellationToken ct = default)
    {
        var request = new { ExternalId = externalId, Provider = provider, Email = email, DisplayName = displayName, Metadata = metadata };
        var response = await _httpClient.PostAsJsonAsync("api/subjects", request, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<SubjectInfoDto>(ct);
        return new SubjectInfo(
            result!.Id,
            result.ExternalId,
            result.Provider,
            result.Email,
            result.DisplayName,
            result.IsActive,
            result.CreatedAt,
            result.LastSeenAt);
    }

    public async Task AssignRoleAsync(
        string subjectId,
        string roleCode,
        string? resourceInstanceId = null,
        DateTimeOffset? expiresAt = null,
        CancellationToken ct = default)
    {
        var request = new { SubjectId = subjectId, RoleCode = roleCode, ResourceInstanceId = resourceInstanceId, ExpiresAt = expiresAt };
        var response = await _httpClient.PostAsJsonAsync("api/subjects/roles", request, ct);
        response.EnsureSuccessStatusCode();

        // Invalidate cache
        await _cache.InvalidateAsync(subjectId, ct);
    }

    public async Task RevokeRoleAsync(
        string subjectId,
        string roleCode,
        string? resourceInstanceId = null,
        CancellationToken ct = default)
    {
        var url = $"api/subjects/{Uri.EscapeDataString(subjectId)}/roles/{Uri.EscapeDataString(roleCode)}";
        if (!string.IsNullOrEmpty(resourceInstanceId))
            url += $"?resourceInstanceId={Uri.EscapeDataString(resourceInstanceId)}";

        var response = await _httpClient.DeleteAsync(url, ct);
        response.EnsureSuccessStatusCode();

        // Invalidate cache
        await _cache.InvalidateAsync(subjectId, ct);
    }

    public async Task GrantInstancePermissionAsync(
        string subjectId,
        string resourceTypeCode,
        string resourceInstanceId,
        string action,
        DateTimeOffset? expiresAt = null,
        CancellationToken ct = default)
    {
        var request = new { SubjectId = subjectId, ResourceTypeCode = resourceTypeCode, ResourceInstanceId = resourceInstanceId, Action = action, ExpiresAt = expiresAt };
        var response = await _httpClient.PostAsJsonAsync("api/instances/permissions", request, ct);
        response.EnsureSuccessStatusCode();

        await _cache.InvalidateAsync(subjectId, ct);
    }

    public async Task RevokeInstancePermissionAsync(
        string subjectId,
        string resourceTypeCode,
        string resourceInstanceId,
        string action,
        CancellationToken ct = default)
    {
        var url = $"api/instances/{Uri.EscapeDataString(resourceTypeCode)}/{Uri.EscapeDataString(resourceInstanceId)}/permissions/{Uri.EscapeDataString(subjectId)}/{Uri.EscapeDataString(action)}";
        var response = await _httpClient.DeleteAsync(url, ct);
        response.EnsureSuccessStatusCode();

        await _cache.InvalidateAsync(subjectId, ct);
    }

    public async Task RegisterResourceInstanceAsync(
        string resourceTypeCode,
        string resourceInstanceId,
        string? ownerSubjectId = null,
        string? displayName = null,
        Dictionary<string, object>? metadata = null,
        CancellationToken ct = default)
    {
        var request = new { ResourceTypeCode = resourceTypeCode, ResourceInstanceId = resourceInstanceId, OwnerSubjectId = ownerSubjectId, DisplayName = displayName, Metadata = metadata };
        var response = await _httpClient.PostAsJsonAsync("api/instances", request, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task RemoveResourceInstanceAsync(
        string resourceTypeCode,
        string resourceInstanceId,
        CancellationToken ct = default)
    {
        var url = $"api/instances/{Uri.EscapeDataString(resourceTypeCode)}/{Uri.EscapeDataString(resourceInstanceId)}";
        var response = await _httpClient.DeleteAsync(url, ct);
        response.EnsureSuccessStatusCode();
    }

    private string NormalizePermission(string permission)
    {
        if (!permission.Contains(':') || permission.Split(':').Length < 3)
        {
            return $"{_options.ApplicationCode}:{permission}";
        }
        return permission;
    }

    private record CheckPermissionResponse(bool Allowed, string? Reason);
    private record GetPermissionsResponse(List<string> Permissions);
    private record GetRolesResponse(List<string> Roles);
    private record SubjectInfoDto(Guid Id, string ExternalId, string Provider, string? Email, string? DisplayName, bool IsActive, DateTimeOffset CreatedAt, DateTimeOffset? LastSeenAt);
}
