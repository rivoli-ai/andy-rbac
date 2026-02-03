namespace Andy.Rbac.Abstractions;

/// <summary>
/// Core service for checking permissions.
/// </summary>
public interface IPermissionService
{
    /// <summary>
    /// Checks if a subject has a specific permission, optionally scoped to a resource instance.
    /// </summary>
    /// <param name="subjectId">The external ID of the subject (e.g., OAuth sub claim).</param>
    /// <param name="permission">Permission code in format "app:resource:action" (e.g., "andy-docs:document:read").</param>
    /// <param name="resourceInstanceId">Optional resource instance ID for instance-level checks.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if the subject has the permission.</returns>
    Task<bool> HasPermissionAsync(
        string subjectId,
        string permission,
        string? resourceInstanceId = null,
        CancellationToken ct = default);

    /// <summary>
    /// Checks if a subject has any of the specified permissions.
    /// </summary>
    Task<bool> HasAnyPermissionAsync(
        string subjectId,
        IEnumerable<string> permissions,
        string? resourceInstanceId = null,
        CancellationToken ct = default);

    /// <summary>
    /// Checks if a subject has all of the specified permissions.
    /// </summary>
    Task<bool> HasAllPermissionsAsync(
        string subjectId,
        IEnumerable<string> permissions,
        string? resourceInstanceId = null,
        CancellationToken ct = default);

    /// <summary>
    /// Gets all permissions for a subject, optionally filtered by application.
    /// </summary>
    /// <param name="subjectId">The external ID of the subject.</param>
    /// <param name="applicationCode">Optional application code to filter permissions.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of permission codes the subject has.</returns>
    Task<IReadOnlyList<string>> GetPermissionsAsync(
        string subjectId,
        string? applicationCode = null,
        CancellationToken ct = default);

    /// <summary>
    /// Gets all roles for a subject, optionally filtered by application.
    /// </summary>
    Task<IReadOnlyList<string>> GetRolesAsync(
        string subjectId,
        string? applicationCode = null,
        CancellationToken ct = default);
}
