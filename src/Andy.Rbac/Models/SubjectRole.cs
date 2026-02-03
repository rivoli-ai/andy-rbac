namespace Andy.Rbac.Models;

/// <summary>
/// Assignment of a role to a subject, optionally scoped to a specific resource instance.
/// </summary>
public class SubjectRole
{
    public Guid Id { get; set; }

    public Guid SubjectId { get; set; }

    public Guid RoleId { get; set; }

    /// <summary>
    /// Optional scope to a specific resource instance.
    /// When null, the role applies globally within its application scope.
    /// Example: Grant "editor" role only for document "doc-123".
    /// </summary>
    public string? ResourceInstanceId { get; set; }

    /// <summary>
    /// Subject who granted this role assignment.
    /// </summary>
    public Guid? GrantedById { get; set; }

    public DateTimeOffset GrantedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Optional expiration for temporary role assignments.
    /// </summary>
    public DateTimeOffset? ExpiresAt { get; set; }

    // Navigation properties
    public Subject Subject { get; set; } = null!;
    public Role Role { get; set; } = null!;
    public Subject? GrantedBy { get; set; }
}
