namespace Andy.Rbac.Models;

/// <summary>
/// Represents a team or organization that groups subjects together.
/// Teams can have roles assigned, which are inherited by all members.
/// </summary>
public class Team
{
    public Guid Id { get; set; }

    /// <summary>
    /// Unique code identifier (e.g., "engineering", "sales").
    /// </summary>
    public required string Code { get; set; }

    /// <summary>
    /// Display name of the team.
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// Optional description.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Parent team for hierarchical organization.
    /// Members inherit roles from parent teams.
    /// </summary>
    public Guid? ParentTeamId { get; set; }

    /// <summary>
    /// Optional application scope. Null means the team spans all applications.
    /// </summary>
    public Guid? ApplicationId { get; set; }

    /// <summary>
    /// External identifier for LDAP/AD sync.
    /// </summary>
    public string? ExternalId { get; set; }

    /// <summary>
    /// Provider for external sync (e.g., "ldap", "azure-ad").
    /// </summary>
    public string? ExternalProvider { get; set; }

    /// <summary>
    /// Whether this team is active.
    /// </summary>
    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }

    // Navigation properties
    public Team? ParentTeam { get; set; }
    public Application? Application { get; set; }
    public ICollection<Team> ChildTeams { get; set; } = [];
    public ICollection<TeamMember> Members { get; set; } = [];
    public ICollection<TeamRole> TeamRoles { get; set; } = [];
}

/// <summary>
/// Membership of a subject in a team.
/// </summary>
public class TeamMember
{
    public Guid Id { get; set; }

    public Guid TeamId { get; set; }
    public Guid SubjectId { get; set; }

    /// <summary>
    /// Role within the team (e.g., "owner", "admin", "member").
    /// This is the team membership role, not an RBAC role.
    /// </summary>
    public TeamMembershipRole MembershipRole { get; set; } = TeamMembershipRole.Member;

    public Guid? AddedById { get; set; }
    public DateTimeOffset AddedAt { get; set; } = DateTimeOffset.UtcNow;

    // Navigation properties
    public Team Team { get; set; } = null!;
    public Subject Subject { get; set; } = null!;
    public Subject? AddedBy { get; set; }
}

public enum TeamMembershipRole
{
    Member,
    Admin,
    Owner
}

/// <summary>
/// RBAC role assigned to a team. All team members inherit this role.
/// </summary>
public class TeamRole
{
    public Guid Id { get; set; }

    public Guid TeamId { get; set; }
    public Guid RoleId { get; set; }

    /// <summary>
    /// Optional scope to a specific resource instance.
    /// </summary>
    public string? ResourceInstanceId { get; set; }

    public Guid? GrantedById { get; set; }
    public DateTimeOffset GrantedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ExpiresAt { get; set; }

    // Navigation properties
    public Team Team { get; set; } = null!;
    public Role Role { get; set; } = null!;
    public Subject? GrantedBy { get; set; }
}
