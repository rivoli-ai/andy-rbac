namespace Andy.Rbac.Models;

/// <summary>
/// Represents an action that can be performed on resources (e.g., "read", "write", "delete", "share").
/// Actions are global and reused across all applications.
/// </summary>
public class Action
{
    public Guid Id { get; set; }

    /// <summary>
    /// Unique code identifier (e.g., "read", "write", "delete", "share", "admin").
    /// </summary>
    public required string Code { get; set; }

    /// <summary>
    /// Display name of the action.
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// Optional description explaining what this action allows.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Sort order for UI display.
    /// </summary>
    public int SortOrder { get; set; }

    // Navigation properties
    public ICollection<Permission> Permissions { get; set; } = [];
}
