namespace Andy.Rbac.Api.Services;

/// <summary>
/// Service for evaluating permissions with full context.
/// </summary>
public interface IPermissionEvaluator
{
    /// <summary>
    /// Checks if a subject has a permission, optionally on a specific resource instance.
    /// </summary>
    /// <param name="subjectExternalId">The external ID of the subject.</param>
    /// <param name="permission">The permission to check.</param>
    /// <param name="groups">Optional group codes from token claims. Permissions are checked for subject + all groups.</param>
    /// <param name="resourceInstanceId">Optional resource instance ID.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<PermissionCheckResult> CheckPermissionAsync(
        string subjectExternalId,
        string permission,
        IEnumerable<string>? groups = null,
        string? resourceInstanceId = null,
        CancellationToken ct = default);

    /// <summary>
    /// Checks if a subject has any of the specified permissions.
    /// </summary>
    Task<PermissionCheckResult> CheckAnyPermissionAsync(
        string subjectExternalId,
        IEnumerable<string> permissions,
        IEnumerable<string>? groups = null,
        string? resourceInstanceId = null,
        CancellationToken ct = default);

    /// <summary>
    /// Gets all permissions for a subject.
    /// </summary>
    Task<IReadOnlyList<string>> GetPermissionsAsync(
        string subjectExternalId,
        IEnumerable<string>? groups = null,
        string? applicationCode = null,
        CancellationToken ct = default);

    /// <summary>
    /// Gets all roles for a subject.
    /// </summary>
    Task<IReadOnlyList<string>> GetRolesAsync(
        string subjectExternalId,
        IEnumerable<string>? groups = null,
        string? applicationCode = null,
        CancellationToken ct = default);
}

public record PermissionCheckResult(bool Allowed, string? Reason = null);
