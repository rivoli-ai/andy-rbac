namespace Andy.Rbac.Models;

/// <summary>
/// Represents a role that can be assigned to subjects.
/// Roles can be global (ApplicationId = null) or scoped to a specific application.
/// </summary>
public class Role
{
    public Guid Id { get; set; }

    /// <summary>
    /// Application this role is scoped to. Null for global roles.
    /// </summary>
    public Guid? ApplicationId { get; set; }

    /// <summary>
    /// Unique code within the application scope (e.g., "admin", "editor", "viewer").
    /// </summary>
    public required string Code { get; set; }

    /// <summary>
    /// Display name of the role.
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// Optional description of the role and its purpose.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// System roles cannot be deleted or modified by users.
    /// </summary>
    public bool IsSystem { get; set; }

    /// <summary>
    /// Parent role for inheritance. This role inherits all permissions from its parent.
    /// </summary>
    public Guid? ParentRoleId { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    // Navigation properties
    public Application? Application { get; set; }
    public Role? ParentRole { get; set; }
    public ICollection<Role> ChildRoles { get; set; } = [];
    public ICollection<RolePermission> RolePermissions { get; set; } = [];
    public ICollection<SubjectRole> SubjectRoles { get; set; } = [];
    public ICollection<ExternalGroupMapping> ExternalGroupMappings { get; set; } = [];
}
