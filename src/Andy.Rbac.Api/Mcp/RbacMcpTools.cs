using System.ComponentModel;
using Andy.Rbac.Api.Services;
using Andy.Rbac.Infrastructure.Data;
using Andy.Rbac.Models;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Server;

namespace Andy.Rbac.Api.Mcp;

/// <summary>
/// MCP tools for AI assistants to query and manage RBAC.
/// </summary>
[McpServerToolType]
public class RbacMcpTools
{
    private readonly RbacDbContext _db;
    private readonly IPermissionEvaluator _evaluator;
    private readonly ILogger<RbacMcpTools> _logger;

    public RbacMcpTools(RbacDbContext db, IPermissionEvaluator evaluator, ILogger<RbacMcpTools> logger)
    {
        _db = db;
        _evaluator = evaluator;
        _logger = logger;
    }

    // ==================== Permission Checking ====================

    [McpServerTool]
    [Description("Check if a user has a specific permission. Returns true/false with reason.")]
    public async Task<PermissionCheckResult> CheckPermission(
        [Description("External ID of the user (e.g., OAuth sub claim)")] string subjectId,
        [Description("Permission code (e.g., 'andy-docs:document:read')")] string permission,
        [Description("Optional resource instance ID for instance-level checks")] string? resourceInstanceId = null)
    {
        var result = await _evaluator.CheckPermissionAsync(subjectId, permission, resourceInstanceId);
        return new PermissionCheckResult(result.Allowed, result.Reason ?? (result.Allowed ? "Permission granted" : "Permission denied"));
    }

    [McpServerTool]
    [Description("Get all permissions for a user, optionally filtered by application.")]
    public async Task<List<string>> GetUserPermissions(
        [Description("External ID of the user")] string subjectId,
        [Description("Optional application code to filter (e.g., 'andy-docs')")] string? applicationCode = null)
    {
        var permissions = await _evaluator.GetPermissionsAsync(subjectId, applicationCode);
        return permissions.ToList();
    }

    [McpServerTool]
    [Description("Get all roles assigned to a user, optionally filtered by application.")]
    public async Task<List<string>> GetUserRoles(
        [Description("External ID of the user")] string subjectId,
        [Description("Optional application code to filter")] string? applicationCode = null)
    {
        var roles = await _evaluator.GetRolesAsync(subjectId, applicationCode);
        return roles.ToList();
    }

    // ==================== Application Management ====================

    [McpServerTool]
    [Description("List all registered applications in the RBAC system.")]
    public async Task<List<ApplicationInfo>> ListApplications()
    {
        var apps = await _db.Applications
            .AsNoTracking()
            .OrderBy(a => a.Name)
            .Select(a => new ApplicationInfo(
                a.Id,
                a.Code,
                a.Name,
                a.Description,
                a.ResourceTypes.Count,
                a.Roles.Count))
            .ToListAsync();

        return apps;
    }

    [McpServerTool]
    [Description("Get detailed information about an application including its resource types and roles.")]
    public async Task<ApplicationDetail?> GetApplication(
        [Description("Application code (e.g., 'andy-docs')")] string applicationCode)
    {
        var app = await _db.Applications
            .Include(a => a.ResourceTypes)
            .Include(a => a.Roles)
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Code == applicationCode);

        if (app == null) return null;

        return new ApplicationDetail(
            app.Id,
            app.Code,
            app.Name,
            app.Description,
            app.ResourceTypes.Select(rt => new ResourceTypeInfo(rt.Code, rt.Name, rt.SupportsInstances)).ToList(),
            app.Roles.Select(r => new RoleInfo(r.Code, r.Name, r.IsSystem)).ToList());
    }

    [McpServerTool]
    [Description("Create a new application in the RBAC system.")]
    public async Task<ApplicationInfo> CreateApplication(
        [Description("Unique code (e.g., 'my-app')")] string code,
        [Description("Display name")] string name,
        [Description("Optional description")] string? description = null)
    {
        var app = new Application { Code = code, Name = name, Description = description };
        _db.Applications.Add(app);
        await _db.SaveChangesAsync();

        _logger.LogInformation("MCP: Created application {AppCode}", code);

        return new ApplicationInfo(app.Id, app.Code, app.Name, app.Description, 0, 0);
    }

    // ==================== Role Management ====================

    [McpServerTool]
    [Description("List all roles, optionally filtered by application.")]
    public async Task<List<RoleInfo>> ListRoles(
        [Description("Optional application code to filter roles")] string? applicationCode = null)
    {
        var query = _db.Roles.Include(r => r.Application).AsNoTracking();

        if (!string.IsNullOrEmpty(applicationCode))
            query = query.Where(r => r.ApplicationId == null || r.Application!.Code == applicationCode);

        return await query
            .OrderBy(r => r.Name)
            .Select(r => new RoleInfo(r.Code, r.Name, r.IsSystem, r.Application != null ? r.Application.Code : null))
            .ToListAsync();
    }

    [McpServerTool]
    [Description("Create a new role in the RBAC system.")]
    public async Task<RoleInfo> CreateRole(
        [Description("Unique role code (e.g., 'editor')")] string code,
        [Description("Display name")] string name,
        [Description("Optional description")] string? description = null,
        [Description("Optional application code to scope the role")] string? applicationCode = null,
        [Description("Optional parent role code for inheritance")] string? parentRoleCode = null)
    {
        Guid? appId = null;
        if (!string.IsNullOrEmpty(applicationCode))
        {
            var app = await _db.Applications.FirstOrDefaultAsync(a => a.Code == applicationCode);
            appId = app?.Id;
        }

        Guid? parentId = null;
        if (!string.IsNullOrEmpty(parentRoleCode))
        {
            var parent = await _db.Roles.FirstOrDefaultAsync(r => r.Code == parentRoleCode);
            parentId = parent?.Id;
        }

        var role = new Role
        {
            Code = code,
            Name = name,
            Description = description,
            ApplicationId = appId,
            ParentRoleId = parentId,
            IsSystem = false
        };

        _db.Roles.Add(role);
        await _db.SaveChangesAsync();

        _logger.LogInformation("MCP: Created role {RoleCode}", code);

        return new RoleInfo(role.Code, role.Name, role.IsSystem, applicationCode);
    }

    [McpServerTool]
    [Description("Assign a role to a user.")]
    public async Task<string> AssignRoleToUser(
        [Description("External ID of the user")] string subjectExternalId,
        [Description("Role code to assign")] string roleCode,
        [Description("Optional resource instance ID to scope the assignment")] string? resourceInstanceId = null)
    {
        var subject = await _db.Subjects.FirstOrDefaultAsync(s => s.ExternalId == subjectExternalId);
        if (subject == null)
            return $"Error: Subject '{subjectExternalId}' not found";

        var role = await _db.Roles.FirstOrDefaultAsync(r => r.Code == roleCode);
        if (role == null)
            return $"Error: Role '{roleCode}' not found";

        var existing = await _db.SubjectRoles
            .AnyAsync(sr => sr.SubjectId == subject.Id && sr.RoleId == role.Id && sr.ResourceInstanceId == resourceInstanceId);

        if (existing)
            return $"Role '{roleCode}' is already assigned to user";

        _db.SubjectRoles.Add(new SubjectRole
        {
            SubjectId = subject.Id,
            RoleId = role.Id,
            ResourceInstanceId = resourceInstanceId
        });

        await _db.SaveChangesAsync();

        _logger.LogInformation("MCP: Assigned role {RoleCode} to {SubjectId}", roleCode, subjectExternalId);

        return $"Successfully assigned role '{roleCode}' to user '{subjectExternalId}'";
    }

    [McpServerTool]
    [Description("Revoke a role from a user.")]
    public async Task<string> RevokeRoleFromUser(
        [Description("External ID of the user")] string subjectExternalId,
        [Description("Role code to revoke")] string roleCode,
        [Description("Optional resource instance ID")] string? resourceInstanceId = null)
    {
        var subject = await _db.Subjects.FirstOrDefaultAsync(s => s.ExternalId == subjectExternalId);
        if (subject == null)
            return $"Error: Subject '{subjectExternalId}' not found";

        var role = await _db.Roles.FirstOrDefaultAsync(r => r.Code == roleCode);
        if (role == null)
            return $"Error: Role '{roleCode}' not found";

        var assignment = await _db.SubjectRoles
            .FirstOrDefaultAsync(sr => sr.SubjectId == subject.Id && sr.RoleId == role.Id && sr.ResourceInstanceId == resourceInstanceId);

        if (assignment == null)
            return $"Role '{roleCode}' is not assigned to user";

        _db.SubjectRoles.Remove(assignment);
        await _db.SaveChangesAsync();

        _logger.LogInformation("MCP: Revoked role {RoleCode} from {SubjectId}", roleCode, subjectExternalId);

        return $"Successfully revoked role '{roleCode}' from user '{subjectExternalId}'";
    }

    // ==================== Team Management ====================

    [McpServerTool]
    [Description("List all teams in the RBAC system.")]
    public async Task<List<TeamInfo>> ListTeams(
        [Description("Optional application code to filter")] string? applicationCode = null)
    {
        var query = _db.Teams
            .Include(t => t.Application)
            .Include(t => t.ParentTeam)
            .AsNoTracking();

        if (!string.IsNullOrEmpty(applicationCode))
            query = query.Where(t => t.ApplicationId == null || t.Application!.Code == applicationCode);

        return await query
            .OrderBy(t => t.Name)
            .Select(t => new TeamInfo(
                t.Id,
                t.Code,
                t.Name,
                t.Description,
                t.ParentTeam != null ? t.ParentTeam.Code : null,
                t.Application != null ? t.Application.Code : null,
                t.Members.Count,
                t.IsActive))
            .ToListAsync();
    }

    [McpServerTool]
    [Description("Create a new team.")]
    public async Task<TeamInfo> CreateTeam(
        [Description("Unique team code (e.g., 'engineering')")] string code,
        [Description("Display name")] string name,
        [Description("Optional description")] string? description = null,
        [Description("Optional parent team code for hierarchy")] string? parentTeamCode = null,
        [Description("Optional application code to scope the team")] string? applicationCode = null)
    {
        Guid? parentId = null;
        if (!string.IsNullOrEmpty(parentTeamCode))
        {
            var parent = await _db.Teams.FirstOrDefaultAsync(t => t.Code == parentTeamCode);
            parentId = parent?.Id;
        }

        Guid? appId = null;
        if (!string.IsNullOrEmpty(applicationCode))
        {
            var app = await _db.Applications.FirstOrDefaultAsync(a => a.Code == applicationCode);
            appId = app?.Id;
        }

        var team = new Team
        {
            Code = code,
            Name = name,
            Description = description,
            ParentTeamId = parentId,
            ApplicationId = appId
        };

        _db.Teams.Add(team);
        await _db.SaveChangesAsync();

        _logger.LogInformation("MCP: Created team {TeamCode}", code);

        return new TeamInfo(team.Id, team.Code, team.Name, team.Description, parentTeamCode, applicationCode, 0, true);
    }

    [McpServerTool]
    [Description("Add a user to a team.")]
    public async Task<string> AddUserToTeam(
        [Description("Team code")] string teamCode,
        [Description("External ID of the user")] string subjectExternalId,
        [Description("Membership role: Member, Admin, or Owner")] string membershipRole = "Member")
    {
        var team = await _db.Teams.FirstOrDefaultAsync(t => t.Code == teamCode);
        if (team == null)
            return $"Error: Team '{teamCode}' not found";

        var subject = await _db.Subjects.FirstOrDefaultAsync(s => s.ExternalId == subjectExternalId);
        if (subject == null)
            return $"Error: Subject '{subjectExternalId}' not found";

        if (await _db.TeamMembers.AnyAsync(tm => tm.TeamId == team.Id && tm.SubjectId == subject.Id))
            return $"User is already a member of team '{teamCode}'";

        if (!Enum.TryParse<TeamMembershipRole>(membershipRole, true, out var role))
            role = TeamMembershipRole.Member;

        _db.TeamMembers.Add(new TeamMember
        {
            TeamId = team.Id,
            SubjectId = subject.Id,
            MembershipRole = role
        });

        await _db.SaveChangesAsync();

        _logger.LogInformation("MCP: Added {SubjectId} to team {TeamCode}", subjectExternalId, teamCode);

        return $"Successfully added user '{subjectExternalId}' to team '{teamCode}' as {role}";
    }

    [McpServerTool]
    [Description("Assign a role to a team (all members inherit this role).")]
    public async Task<string> AssignRoleToTeam(
        [Description("Team code")] string teamCode,
        [Description("Role code to assign")] string roleCode)
    {
        var team = await _db.Teams.FirstOrDefaultAsync(t => t.Code == teamCode);
        if (team == null)
            return $"Error: Team '{teamCode}' not found";

        var role = await _db.Roles.FirstOrDefaultAsync(r => r.Code == roleCode);
        if (role == null)
            return $"Error: Role '{roleCode}' not found";

        if (await _db.TeamRoles.AnyAsync(tr => tr.TeamId == team.Id && tr.RoleId == role.Id))
            return $"Role '{roleCode}' is already assigned to team";

        _db.TeamRoles.Add(new TeamRole { TeamId = team.Id, RoleId = role.Id });
        await _db.SaveChangesAsync();

        _logger.LogInformation("MCP: Assigned role {RoleCode} to team {TeamCode}", roleCode, teamCode);

        return $"Successfully assigned role '{roleCode}' to team '{teamCode}'";
    }

    // ==================== User Management ====================

    [McpServerTool]
    [Description("Search for users by email, name, or external ID.")]
    public async Task<List<UserInfo>> SearchUsers(
        [Description("Search query (email, name, or external ID)")] string query,
        [Description("Maximum results to return")] int limit = 20)
    {
        return await _db.Subjects
            .AsNoTracking()
            .Where(s =>
                (s.Email != null && s.Email.Contains(query)) ||
                (s.DisplayName != null && s.DisplayName.Contains(query)) ||
                s.ExternalId.Contains(query))
            .Take(limit)
            .Select(s => new UserInfo(
                s.Id,
                s.ExternalId,
                s.Provider,
                s.Email,
                s.DisplayName,
                s.IsActive))
            .ToListAsync();
    }

    [McpServerTool]
    [Description("Get detailed information about a user including their roles and team memberships.")]
    public async Task<UserDetail?> GetUser(
        [Description("External ID of the user")] string subjectExternalId)
    {
        var subject = await _db.Subjects
            .Include(s => s.SubjectRoles)
            .ThenInclude(sr => sr.Role)
            .ThenInclude(r => r.Application)
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.ExternalId == subjectExternalId);

        if (subject == null) return null;

        var teamMemberships = await _db.TeamMembers
            .Include(tm => tm.Team)
            .Where(tm => tm.SubjectId == subject.Id)
            .Select(tm => new TeamMembershipInfo(tm.Team.Code, tm.Team.Name, tm.MembershipRole.ToString()))
            .ToListAsync();

        return new UserDetail(
            subject.Id,
            subject.ExternalId,
            subject.Provider,
            subject.Email,
            subject.DisplayName,
            subject.IsActive,
            subject.SubjectRoles.Select(sr => new UserRoleInfo(
                sr.Role.Code,
                sr.Role.Application?.Code,
                sr.ResourceInstanceId)).ToList(),
            teamMemberships);
    }

    // ==================== Audit ====================

    [McpServerTool]
    [Description("Get recent RBAC audit events.")]
    public async Task<List<AuditEventInfo>> GetRecentAuditEvents(
        [Description("Maximum events to return")] int limit = 50,
        [Description("Optional event type filter")] string? eventType = null,
        [Description("Optional subject ID filter")] string? subjectId = null)
    {
        var query = _db.AuditLogs.AsNoTracking();

        if (!string.IsNullOrEmpty(eventType))
            query = query.Where(a => a.EventType == eventType);

        if (!string.IsNullOrEmpty(subjectId))
        {
            var subject = await _db.Subjects.FirstOrDefaultAsync(s => s.ExternalId == subjectId);
            if (subject != null)
                query = query.Where(a => a.SubjectId == subject.Id);
        }

        return await query
            .OrderByDescending(a => a.Timestamp)
            .Take(limit)
            .Select(a => new AuditEventInfo(
                a.Timestamp,
                a.EventType,
                a.ResourceType,
                a.PermissionCode,
                a.Result))
            .ToListAsync();
    }
}

// ==================== DTOs ====================

public record PermissionCheckResult(bool Allowed, string Reason);
public record ApplicationInfo(Guid Id, string Code, string Name, string? Description, int ResourceTypeCount, int RoleCount);
public record ApplicationDetail(Guid Id, string Code, string Name, string? Description, List<ResourceTypeInfo> ResourceTypes, List<RoleInfo> Roles);
public record ResourceTypeInfo(string Code, string Name, bool SupportsInstances);
public record RoleInfo(string Code, string Name, bool IsSystem, string? ApplicationCode = null);
public record TeamInfo(Guid Id, string Code, string Name, string? Description, string? ParentTeamCode, string? ApplicationCode, int MemberCount, bool IsActive);
public record UserInfo(Guid Id, string ExternalId, string Provider, string? Email, string? DisplayName, bool IsActive);
public record UserDetail(Guid Id, string ExternalId, string Provider, string? Email, string? DisplayName, bool IsActive, List<UserRoleInfo> Roles, List<TeamMembershipInfo> Teams);
public record UserRoleInfo(string RoleCode, string? ApplicationCode, string? ResourceInstanceId);
public record TeamMembershipInfo(string TeamCode, string TeamName, string MembershipRole);
public record AuditEventInfo(DateTimeOffset Timestamp, string EventType, string? ResourceType, string? PermissionCode, string? Result);
