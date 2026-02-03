namespace Andy.Rbac.Models;

/// <summary>
/// Represents a specific instance of a resource type for instance-level permissions.
/// Used for features like document sharing.
/// </summary>
public class ResourceInstance
{
    public Guid Id { get; set; }

    public Guid ResourceTypeId { get; set; }

    /// <summary>
    /// External identifier of the resource in its source system.
    /// </summary>
    public required string ExternalId { get; set; }

    /// <summary>
    /// Owner of this resource (typically the creator).
    /// </summary>
    public Guid? OwnerSubjectId { get; set; }

    /// <summary>
    /// Display name for UI.
    /// </summary>
    public string? DisplayName { get; set; }

    /// <summary>
    /// Additional metadata about the resource.
    /// </summary>
    public Dictionary<string, object>? Metadata { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    // Navigation properties
    public ResourceType ResourceType { get; set; } = null!;
    public Subject? Owner { get; set; }
    public ICollection<InstancePermission> InstancePermissions { get; set; } = [];
}
