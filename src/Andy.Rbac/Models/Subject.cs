namespace Andy.Rbac.Models;

/// <summary>
/// Represents an identity that can be granted permissions (user, service account, or group).
/// Subjects are synchronized from external identity providers.
/// </summary>
public class Subject
{
    public Guid Id { get; set; }

    /// <summary>
    /// External identifier from the identity provider (e.g., OAuth 'sub' claim).
    /// </summary>
    public required string ExternalId { get; set; }

    /// <summary>
    /// Identity provider this subject came from (e.g., "andy-auth", "azure-ad", "ldap").
    /// </summary>
    public required string Provider { get; set; }

    /// <summary>
    /// Type of subject.
    /// </summary>
    public SubjectType Type { get; set; } = SubjectType.User;

    /// <summary>
    /// Email address if available.
    /// </summary>
    public string? Email { get; set; }

    /// <summary>
    /// Display name for UI.
    /// </summary>
    public string? DisplayName { get; set; }

    /// <summary>
    /// Additional metadata from the identity provider (claims, LDAP attributes, etc.).
    /// </summary>
    public Dictionary<string, object>? Metadata { get; set; }

    /// <summary>
    /// Whether this subject is active. Inactive subjects cannot authenticate.
    /// </summary>
    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastSeenAt { get; set; }

    // Navigation properties
    public ICollection<SubjectRole> SubjectRoles { get; set; } = [];
    public ICollection<InstancePermission> InstancePermissions { get; set; } = [];
    public ICollection<ResourceInstance> OwnedResources { get; set; } = [];
}

public enum SubjectType
{
    User,
    Service,
    Group
}
