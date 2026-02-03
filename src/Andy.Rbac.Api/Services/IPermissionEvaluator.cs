namespace Andy.Rbac.Api.Services;

/// <summary>
/// Service for evaluating permissions with full context.
/// </summary>
public interface IPermissionEvaluator
{
    /// <summary>
    /// Checks if a subject has a permission, optionally on a specific resource instance.
    /// </summary>
    Task<PermissionCheckResult> CheckPermissionAsync(
        string subjectExternalId,
        string permission,
        string? resourceInstanceId = null,
        CancellationToken ct = default);

    /// <summary>
    /// Checks if a subject has any of the specified permissions.
    /// </summary>
    Task<PermissionCheckResult> CheckAnyPermissionAsync(
        string subjectExternalId,
        IEnumerable<string> permissions,
        string? resourceInstanceId = null,
        CancellationToken ct = default);

    /// <summary>
    /// Gets all permissions for a subject.
    /// </summary>
    Task<IReadOnlyList<string>> GetPermissionsAsync(
        string subjectExternalId,
        string? applicationCode = null,
        CancellationToken ct = default);

    /// <summary>
    /// Gets all roles for a subject.
    /// </summary>
    Task<IReadOnlyList<string>> GetRolesAsync(
        string subjectExternalId,
        string? applicationCode = null,
        CancellationToken ct = default);
}

public record PermissionCheckResult(bool Allowed, string? Reason = null);
