namespace Andy.Rbac.Models;

/// <summary>
/// Direct permission grant on a specific resource instance (e.g., sharing a document).
/// </summary>
public class InstancePermission
{
    public Guid Id { get; set; }

    public Guid ResourceInstanceId { get; set; }

    public Guid SubjectId { get; set; }

    public Guid PermissionId { get; set; }

    /// <summary>
    /// Subject who granted this permission.
    /// </summary>
    public Guid? GrantedById { get; set; }

    public DateTimeOffset GrantedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Optional expiration for temporary access.
    /// </summary>
    public DateTimeOffset? ExpiresAt { get; set; }

    // Navigation properties
    public ResourceInstance ResourceInstance { get; set; } = null!;
    public Subject Subject { get; set; } = null!;
    public Permission Permission { get; set; } = null!;
    public Subject? GrantedBy { get; set; }
}
