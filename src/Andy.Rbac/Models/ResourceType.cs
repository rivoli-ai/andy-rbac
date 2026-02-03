namespace Andy.Rbac.Models;

/// <summary>
/// Represents a type of resource within an application (e.g., "document", "collection", "config").
/// </summary>
public class ResourceType
{
    public Guid Id { get; set; }

    public Guid ApplicationId { get; set; }

    /// <summary>
    /// Unique code within the application (e.g., "document", "collection").
    /// </summary>
    public required string Code { get; set; }

    /// <summary>
    /// Display name of the resource type.
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// Optional description.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Whether this resource type can have specific instances with instance-level permissions.
    /// Set to false for singleton resources like "config" or "settings".
    /// </summary>
    public bool SupportsInstances { get; set; } = true;

    // Navigation properties
    public Application Application { get; set; } = null!;
    public ICollection<Permission> Permissions { get; set; } = [];
    public ICollection<ResourceInstance> Instances { get; set; } = [];
}
