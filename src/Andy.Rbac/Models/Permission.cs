namespace Andy.Rbac.Models;

/// <summary>
/// Represents a permission combining a resource type and action.
/// The permission code follows the format: "{app-code}:{resource-type}:{action}"
/// Example: "andy-docs:document:read"
/// </summary>
public class Permission
{
    public Guid Id { get; set; }

    public Guid ResourceTypeId { get; set; }

    public Guid ActionId { get; set; }

    /// <summary>
    /// Full permission code in the format "{app-code}:{resource-type}:{action}".
    /// This is computed from the related entities.
    /// </summary>
    public string Code => $"{ResourceType?.Application?.Code}:{ResourceType?.Code}:{Action?.Code}";

    /// <summary>
    /// Optional description of what this specific permission allows.
    /// </summary>
    public string? Description { get; set; }

    // Navigation properties
    public ResourceType ResourceType { get; set; } = null!;
    public Action Action { get; set; } = null!;
    public ICollection<RolePermission> RolePermissions { get; set; } = [];
    public ICollection<InstancePermission> InstancePermissions { get; set; } = [];
}
