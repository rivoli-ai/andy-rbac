namespace Andy.Rbac.Models;

/// <summary>
/// Maps external groups (LDAP, Azure AD) to RBAC roles.
/// When a user belongs to an external group, they automatically receive the mapped role.
/// </summary>
public class ExternalGroupMapping
{
    public Guid Id { get; set; }

    /// <summary>
    /// Identity provider (e.g., "ldap", "azure-ad").
    /// </summary>
    public required string Provider { get; set; }

    /// <summary>
    /// External group identifier (e.g., LDAP DN or Azure AD group ID).
    /// </summary>
    public required string ExternalGroupId { get; set; }

    /// <summary>
    /// Display name of the external group.
    /// </summary>
    public string? ExternalGroupName { get; set; }

    /// <summary>
    /// RBAC role to assign to members of this group.
    /// </summary>
    public Guid RoleId { get; set; }

    /// <summary>
    /// Whether automatic synchronization is enabled.
    /// </summary>
    public bool SyncEnabled { get; set; } = true;

    public DateTimeOffset? LastSyncedAt { get; set; }

    // Navigation properties
    public Role Role { get; set; } = null!;
}
