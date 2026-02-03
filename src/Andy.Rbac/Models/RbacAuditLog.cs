namespace Andy.Rbac.Models;

/// <summary>
/// Audit log for RBAC operations and permission checks.
/// </summary>
public class RbacAuditLog
{
    public Guid Id { get; set; }

    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Subject who performed or was checked for the action.
    /// </summary>
    public Guid? SubjectId { get; set; }

    /// <summary>
    /// Type of audit event.
    /// </summary>
    public required string EventType { get; set; }

    /// <summary>
    /// Resource type involved (if applicable).
    /// </summary>
    public string? ResourceType { get; set; }

    /// <summary>
    /// Resource instance ID involved (if applicable).
    /// </summary>
    public string? ResourceInstanceId { get; set; }

    /// <summary>
    /// Permission code that was checked or modified.
    /// </summary>
    public string? PermissionCode { get; set; }

    /// <summary>
    /// Result of the operation (e.g., "allowed", "denied", "success", "error").
    /// </summary>
    public string? Result { get; set; }

    /// <summary>
    /// Additional context about the event.
    /// </summary>
    public Dictionary<string, object>? Context { get; set; }

    /// <summary>
    /// IP address of the requester.
    /// </summary>
    public string? IpAddress { get; set; }

    /// <summary>
    /// User agent of the requester.
    /// </summary>
    public string? UserAgent { get; set; }
}

public static class AuditEventTypes
{
    public const string PermissionCheck = "permission_check";
    public const string RoleAssigned = "role_assigned";
    public const string RoleRevoked = "role_revoked";
    public const string PermissionGranted = "permission_granted";
    public const string PermissionRevoked = "permission_revoked";
    public const string RoleCreated = "role_created";
    public const string RoleUpdated = "role_updated";
    public const string RoleDeleted = "role_deleted";
    public const string SubjectCreated = "subject_created";
    public const string SubjectUpdated = "subject_updated";
    public const string ExternalGroupSynced = "external_group_synced";
}
