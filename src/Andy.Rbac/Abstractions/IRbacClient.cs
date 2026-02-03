namespace Andy.Rbac.Abstractions;

/// <summary>
/// Client interface for communicating with the RBAC API.
/// Implemented by HTTP and gRPC clients.
/// </summary>
public interface IRbacClient : IPermissionService
{
    /// <summary>
    /// Provisions or updates a subject in the RBAC system.
    /// Called automatically on first authentication.
    /// </summary>
    Task<SubjectInfo> ProvisionSubjectAsync(
        string externalId,
        string provider,
        string? email = null,
        string? displayName = null,
        Dictionary<string, object>? metadata = null,
        CancellationToken ct = default);

    /// <summary>
    /// Assigns a role to a subject.
    /// </summary>
    Task AssignRoleAsync(
        string subjectId,
        string roleCode,
        string? resourceInstanceId = null,
        DateTimeOffset? expiresAt = null,
        CancellationToken ct = default);

    /// <summary>
    /// Revokes a role from a subject.
    /// </summary>
    Task RevokeRoleAsync(
        string subjectId,
        string roleCode,
        string? resourceInstanceId = null,
        CancellationToken ct = default);

    /// <summary>
    /// Grants a direct permission on a resource instance (e.g., sharing).
    /// </summary>
    Task GrantInstancePermissionAsync(
        string subjectId,
        string resourceTypeCode,
        string resourceInstanceId,
        string action,
        DateTimeOffset? expiresAt = null,
        CancellationToken ct = default);

    /// <summary>
    /// Revokes a direct permission on a resource instance.
    /// </summary>
    Task RevokeInstancePermissionAsync(
        string subjectId,
        string resourceTypeCode,
        string resourceInstanceId,
        string action,
        CancellationToken ct = default);

    /// <summary>
    /// Registers a resource instance for instance-level permissions.
    /// </summary>
    Task RegisterResourceInstanceAsync(
        string resourceTypeCode,
        string resourceInstanceId,
        string? ownerSubjectId = null,
        string? displayName = null,
        Dictionary<string, object>? metadata = null,
        CancellationToken ct = default);

    /// <summary>
    /// Removes a resource instance and all associated permissions.
    /// </summary>
    Task RemoveResourceInstanceAsync(
        string resourceTypeCode,
        string resourceInstanceId,
        CancellationToken ct = default);
}

/// <summary>
/// Information about a subject.
/// </summary>
public record SubjectInfo(
    Guid Id,
    string ExternalId,
    string Provider,
    string? Email,
    string? DisplayName,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastSeenAt);
