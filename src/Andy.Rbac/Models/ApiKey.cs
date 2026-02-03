namespace Andy.Rbac.Models;

/// <summary>
/// API key for programmatic access to RBAC management.
/// Used by CLI tools and automation scripts.
/// </summary>
public class ApiKey
{
    public Guid Id { get; set; }

    /// <summary>
    /// Display name for the API key.
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// The hashed API key. The actual key is only shown once at creation.
    /// </summary>
    public required string KeyHash { get; set; }

    /// <summary>
    /// Key prefix for identification (e.g., "rbac_live_abc123").
    /// </summary>
    public required string KeyPrefix { get; set; }

    /// <summary>
    /// Subject this API key belongs to.
    /// </summary>
    public Guid SubjectId { get; set; }

    /// <summary>
    /// Optional application scope. Null means access to all applications the subject can access.
    /// </summary>
    public Guid? ApplicationId { get; set; }

    /// <summary>
    /// Scopes/permissions this key is limited to.
    /// Empty means all permissions the subject has.
    /// </summary>
    public List<string> Scopes { get; set; } = [];

    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset? LastUsedAt { get; set; }
    public string? LastUsedIp { get; set; }

    // Navigation properties
    public Subject Subject { get; set; } = null!;
    public Application? Application { get; set; }
}
