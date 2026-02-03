using System.ComponentModel;
using Andy.Rbac.Api.Services;
using Andy.Rbac.Models;
using ModelContextProtocol.Server;

namespace Andy.Rbac.Api.Mcp;

/// <summary>
/// MCP tools for AI assistants to query and manage RBAC.
/// All operations delegate to shared services (same code as REST API).
/// </summary>
[McpServerToolType]
public class RbacMcpTools
{
    private readonly IPermissionEvaluator _evaluator;
    private readonly IApplicationService _applicationService;
    private readonly IRoleService _roleService;
    private readonly ITeamService _teamService;
    private readonly ISubjectService _subjectService;
    private readonly ILogger<RbacMcpTools> _logger;

    public RbacMcpTools(
        IPermissionEvaluator evaluator,
        IApplicationService applicationService,
        IRoleService roleService,
        ITeamService teamService,
        ISubjectService subjectService,
        ILogger<RbacMcpTools> logger)
    {
        _evaluator = evaluator;
        _applicationService = applicationService;
        _roleService = roleService;
        _teamService = teamService;
        _subjectService = subjectService;
        _logger = logger;
    }

    // ==================== Permission Checking ====================

    [McpServerTool]
    [Description("Check if a user has a specific permission. Returns true/false with reason.")]
    public async Task<McpPermissionCheckResult> CheckPermission(
        [Description("External ID of the user (e.g., OAuth sub claim)")] string subjectId,
        [Description("Permission code (e.g., 'andy-docs:document:read')")] string permission,
        [Description("Optional resource instance ID for instance-level checks")] string? resourceInstanceId = null)
    {
        var result = await _evaluator.CheckPermissionAsync(subjectId, permission, resourceInstanceId);
        return new McpPermissionCheckResult(result.Allowed, result.Reason ?? (result.Allowed ? "Permission granted" : "Permission denied"));
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
    public async Task<List<McpApplicationInfo>> ListApplications()
    {
        var result = await _applicationService.GetAllAsync();
        return result.Applications.Select(a => new McpApplicationInfo(
            a.Id, a.Code, a.Name, a.Description, a.ResourceTypeCount, a.RoleCount)).ToList();
    }

    [McpServerTool]
    [Description("Get detailed information about an application including its resource types and roles.")]
    public async Task<McpApplicationDetail?> GetApplication(
        [Description("Application code (e.g., 'andy-docs')")] string applicationCode)
    {
        var result = await _applicationService.GetByCodeAsync(applicationCode);
        if (result == null) return null;

        var app = result.Application;
        return new McpApplicationDetail(
            app.Id, app.Code, app.Name, app.Description,
            app.ResourceTypes.Select(rt => new McpResourceTypeInfo(rt.Code, rt.Name, rt.SupportsInstances)).ToList(),
            app.Roles.Select(r => new McpRoleInfo(r.Code, r.Name, r.IsSystem, null)).ToList());
    }

    [McpServerTool]
    [Description("Create a new application in the RBAC system.")]
    public async Task<McpApplicationInfo> CreateApplication(
        [Description("Unique code (e.g., 'my-app')")] string code,
        [Description("Display name")] string name,
        [Description("Optional description")] string? description = null)
    {
        var result = await _applicationService.CreateAsync(new CreateApplicationRequest(code, name, description));
        var app = result.Application;
        _logger.LogInformation("MCP: Created application {AppCode}", code);
        return new McpApplicationInfo(app.Id, app.Code, app.Name, app.Description, 0, 0);
    }

    // ==================== Role Management ====================

    [McpServerTool]
    [Description("List all roles, optionally filtered by application.")]
    public async Task<List<McpRoleInfo>> ListRoles(
        [Description("Optional application code to filter roles")] string? applicationCode = null)
    {
        var result = await _roleService.GetAllAsync(applicationCode);
        return result.Roles.Select(r => new McpRoleInfo(r.Code, r.Name, r.IsSystem, r.ApplicationCode)).ToList();
    }

    [McpServerTool]
    [Description("Create a new role in the RBAC system.")]
    public async Task<McpRoleInfo> CreateRole(
        [Description("Unique role code (e.g., 'editor')")] string code,
        [Description("Display name")] string name,
        [Description("Optional description")] string? description = null,
        [Description("Optional application code to scope the role")] string? applicationCode = null,
        [Description("Optional parent role code for inheritance")] string? parentRoleCode = null)
    {
        var result = await _roleService.CreateAsync(new CreateRoleRequest(code, name, description, applicationCode, parentRoleCode));
        var role = result.Role;
        _logger.LogInformation("MCP: Created role {RoleCode}", code);
        return new McpRoleInfo(role.Code, role.Name, role.IsSystem, role.ApplicationCode);
    }

    [McpServerTool]
    [Description("Assign a role to a user.")]
    public async Task<string> AssignRoleToUser(
        [Description("External ID of the user")] string subjectExternalId,
        [Description("Role code to assign")] string roleCode,
        [Description("Optional resource instance ID to scope the assignment")] string? resourceInstanceId = null)
    {
        var message = await _roleService.AssignToSubjectAsync(subjectExternalId, roleCode, resourceInstanceId);
        _logger.LogInformation("MCP: {Message}", message);
        return message;
    }

    [McpServerTool]
    [Description("Revoke a role from a user.")]
    public async Task<string> RevokeRoleFromUser(
        [Description("External ID of the user")] string subjectExternalId,
        [Description("Role code to revoke")] string roleCode,
        [Description("Optional resource instance ID")] string? resourceInstanceId = null)
    {
        var message = await _roleService.RevokeFromSubjectAsync(subjectExternalId, roleCode, resourceInstanceId);
        _logger.LogInformation("MCP: {Message}", message);
        return message;
    }

    // ==================== Team Management ====================

    [McpServerTool]
    [Description("List all teams in the RBAC system.")]
    public async Task<List<McpTeamInfo>> ListTeams(
        [Description("Optional application code to filter")] string? applicationCode = null)
    {
        var result = await _teamService.GetAllAsync(applicationCode);
        return result.Teams.Select(t => new McpTeamInfo(
            t.Id, t.Code, t.Name, t.Description, t.ParentTeamCode, t.ApplicationCode, t.MemberCount, t.IsActive)).ToList();
    }

    [McpServerTool]
    [Description("Create a new team.")]
    public async Task<McpTeamInfo> CreateTeam(
        [Description("Unique team code (e.g., 'engineering')")] string code,
        [Description("Display name")] string name,
        [Description("Optional description")] string? description = null,
        [Description("Optional parent team code for hierarchy")] string? parentTeamCode = null,
        [Description("Optional application code to scope the team")] string? applicationCode = null)
    {
        var result = await _teamService.CreateAsync(new CreateTeamRequest(code, name, description, parentTeamCode, applicationCode));
        var team = result.Team;
        _logger.LogInformation("MCP: Created team {TeamCode}", code);
        return new McpTeamInfo(team.Id, team.Code, team.Name, team.Description, team.ParentTeamCode, team.ApplicationCode, 0, true);
    }

    [McpServerTool]
    [Description("Add a user to a team.")]
    public async Task<string> AddUserToTeam(
        [Description("Team code")] string teamCode,
        [Description("External ID of the user")] string subjectExternalId,
        [Description("Membership role: Member, Admin, or Owner")] string membershipRole = "Member")
    {
        if (!Enum.TryParse<TeamMembershipRole>(membershipRole, true, out var role))
            role = TeamMembershipRole.Member;

        var message = await _teamService.AddMemberAsync(teamCode, subjectExternalId, role);
        _logger.LogInformation("MCP: {Message}", message);
        return message;
    }

    [McpServerTool]
    [Description("Assign a role to a team (all members inherit this role).")]
    public async Task<string> AssignRoleToTeam(
        [Description("Team code")] string teamCode,
        [Description("Role code to assign")] string roleCode)
    {
        var message = await _roleService.AssignToTeamAsync(teamCode, roleCode);
        _logger.LogInformation("MCP: {Message}", message);
        return message;
    }

    // ==================== User Management ====================

    [McpServerTool]
    [Description("Search for users by email, name, or external ID.")]
    public async Task<List<McpUserInfo>> SearchUsers(
        [Description("Search query (email, name, or external ID)")] string query,
        [Description("Maximum results to return")] int limit = 20)
    {
        var result = await _subjectService.SearchAsync(query, limit);
        return result.Subjects.Select(s => new McpUserInfo(
            s.Id, s.ExternalId, s.Provider, s.Email, s.DisplayName, s.IsActive)).ToList();
    }

    [McpServerTool]
    [Description("Get detailed information about a user including their roles and team memberships.")]
    public async Task<McpUserDetail?> GetUser(
        [Description("External ID of the user")] string subjectExternalId)
    {
        var result = await _subjectService.GetByExternalIdAsync(subjectExternalId);
        if (result == null) return null;

        var subject = result.Subject;
        return new McpUserDetail(
            subject.Id,
            subject.ExternalId,
            subject.Provider,
            subject.Email,
            subject.DisplayName,
            subject.IsActive,
            subject.Roles.Select(r => new McpUserRoleInfo(r.RoleCode, r.ApplicationCode, r.ResourceInstanceId)).ToList(),
            subject.Teams.Select(t => new McpTeamMembershipInfo(t.TeamCode, t.TeamName, t.MembershipRole)).ToList());
    }

    [McpServerTool]
    [Description("Create a new user/subject in the RBAC system.")]
    public async Task<McpUserInfo> CreateUser(
        [Description("External ID (e.g., OAuth sub claim)")] string externalId,
        [Description("Provider (e.g., 'andy-auth', 'azure-ad')")] string provider,
        [Description("Optional email address")] string? email = null,
        [Description("Optional display name")] string? displayName = null)
    {
        var result = await _subjectService.CreateAsync(new CreateSubjectRequest(externalId, provider, email, displayName));
        var subject = result.Subject;
        _logger.LogInformation("MCP: Created user {ExternalId}", externalId);
        return new McpUserInfo(subject.Id, subject.ExternalId, subject.Provider, subject.Email, subject.DisplayName, subject.IsActive);
    }
}

// ==================== MCP DTOs ====================

public record McpPermissionCheckResult(bool Allowed, string Reason);
public record McpApplicationInfo(Guid Id, string Code, string Name, string? Description, int ResourceTypeCount, int RoleCount);
public record McpApplicationDetail(Guid Id, string Code, string Name, string? Description, List<McpResourceTypeInfo> ResourceTypes, List<McpRoleInfo> Roles);
public record McpResourceTypeInfo(string Code, string Name, bool SupportsInstances);
public record McpRoleInfo(string Code, string Name, bool IsSystem, string? ApplicationCode = null);
public record McpTeamInfo(Guid Id, string Code, string Name, string? Description, string? ParentTeamCode, string? ApplicationCode, int MemberCount, bool IsActive);
public record McpUserInfo(Guid Id, string ExternalId, string Provider, string? Email, string? DisplayName, bool IsActive);
public record McpUserDetail(Guid Id, string ExternalId, string Provider, string? Email, string? DisplayName, bool IsActive, List<McpUserRoleInfo> Roles, List<McpTeamMembershipInfo> Teams);
public record McpUserRoleInfo(string RoleCode, string? ApplicationCode, string? ResourceInstanceId);
public record McpTeamMembershipInfo(string TeamCode, string TeamName, string MembershipRole);
