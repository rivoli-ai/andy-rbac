namespace Andy.Rbac.Models;

/// <summary>
/// Represents an application that uses the RBAC system.
/// </summary>
public class Application
{
    public Guid Id { get; set; }

    /// <summary>
    /// Unique code identifier (e.g., "andy-docs", "andy-cli").
    /// </summary>
    public required string Code { get; set; }

    /// <summary>
    /// Display name of the application.
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// Optional description of the application.
    /// </summary>
    public string? Description { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    // Navigation properties
    public ICollection<ResourceType> ResourceTypes { get; set; } = [];
    public ICollection<Role> Roles { get; set; } = [];
}
