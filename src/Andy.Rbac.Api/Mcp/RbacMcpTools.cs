using System.ComponentModel;
using Andy.Rbac.Api.Services;
using Andy.Rbac.Models;
using Andy.Rbac.Api.Authorization;
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
    private readonly IPolicyService _policyService;
    private readonly ILogger<RbacMcpTools> _logger;
    private readonly IHttpContextAccessor? _httpContextAccessor;
    private readonly IAdministratorAuthority? _administratorAuthority;

    public RbacMcpTools(
        IPermissionEvaluator evaluator,
        IApplicationService applicationService,
        IRoleService roleService,
        ITeamService teamService,
        ISubjectService subjectService,
        IPolicyService policyService,
        ILogger<RbacMcpTools> logger,
        IHttpContextAccessor? httpContextAccessor = null,
        IAdministratorAuthority? administratorAuthority = null)
    {
        _administratorAuthority = administratorAuthority;
        _evaluator = evaluator;
        _applicationService = applicationService;
        _roleService = roleService;
        _teamService = teamService;
        _subjectService = subjectService;
        _policyService = policyService;
        _logger = logger;
        _httpContextAccessor = httpContextAccessor;
    }

    /// <summary>
    /// Gates the mutating MCP tools on administrator status, using the same
    /// store-backed authority as the REST and gRPC surfaces (#114).
    ///
    /// Fails closed: a missing accessor or authority denies rather than
    /// allows. It previously returned early "to keep unit construction
    /// lightweight", which meant the failure mode of a DI mistake was full
    /// administrative access through the MCP surface rather than an outage.
    /// </summary>
    private async Task EnsureAdministratorAsync()
    {
        var user = _httpContextAccessor?.HttpContext?.User;
        if (user is null
            || _administratorAuthority is null
            || !await _administratorAuthority.IsAdministratorAsync(user))
        {
            throw new UnauthorizedAccessException("RBAC administrator privileges are required for this MCP tool.");
        }
    }

    // ==================== Permission Checking ====================

    [McpServerTool]
    [Description("Check if a user has a specific permission. Returns true/false with reason.")]
    public async Task<McpPermissionCheckResult> CheckPermission(
        [Description("External ID of the user (e.g., OAuth sub claim)")] string subjectId,
        [Description("Permission code (e.g., 'andy-docs:document:read')")] string permission,
        [Description("Deprecated; caller-supplied groups are ignored")] string? groups = null,
        [Description("Optional resource instance ID for instance-level checks")] string? resourceInstanceId = null,
        [Description("Identity provider used to disambiguate the subject")] string? subjectProvider = null)
    {
        var user = _httpContextAccessor?.HttpContext?.User;
        var provider = user is null
            ? subjectProvider
            : TrustedCallerIdentity.EffectiveProvider(user, subjectId, subjectProvider);
        var groupList = user is null ? null : TrustedCallerIdentity.GroupsFor(user, subjectId, provider);
        var result = string.IsNullOrWhiteSpace(provider)
            ? await _evaluator.CheckPermissionAsync(subjectId, permission, groupList, resourceInstanceId)
            : await _evaluator.CheckPermissionForProviderAsync(
                subjectId, provider, permission, groupList, resourceInstanceId);
        return new McpPermissionCheckResult(result.Allowed, result.Reason ?? (result.Allowed ? "Permission granted" : "Permission denied"));
    }

    [McpServerTool]
    [Description("Get all permissions for a user, optionally filtered by application.")]
    public async Task<List<string>> GetUserPermissions(
        [Description("External ID of the user")] string subjectId,
        [Description("Deprecated; caller-supplied groups are ignored")] string? groups = null,
        [Description("Optional application code to filter (e.g., 'andy-docs')")] string? applicationCode = null,
        [Description("Identity provider used to disambiguate the subject")] string? subjectProvider = null)
    {
        var user = _httpContextAccessor?.HttpContext?.User;
        var provider = user is null
            ? subjectProvider
            : TrustedCallerIdentity.EffectiveProvider(user, subjectId, subjectProvider);
        var groupList = user is null ? null : TrustedCallerIdentity.GroupsFor(user, subjectId, provider);
        var permissions = string.IsNullOrWhiteSpace(provider)
            ? await _evaluator.GetPermissionsAsync(subjectId, groupList, applicationCode)
            : await _evaluator.GetPermissionsForProviderAsync(subjectId, provider, groupList, applicationCode);
        return permissions.ToList();
    }

    [McpServerTool]
    [Description("Get all roles assigned to a user, optionally filtered by application.")]
    public async Task<List<string>> GetUserRoles(
        [Description("External ID of the user")] string subjectId,
        [Description("Deprecated; caller-supplied groups are ignored")] string? groups = null,
        [Description("Optional application code to filter")] string? applicationCode = null,
        [Description("Identity provider used to disambiguate the subject")] string? subjectProvider = null)
    {
        var user = _httpContextAccessor?.HttpContext?.User;
        var provider = user is null
            ? subjectProvider
            : TrustedCallerIdentity.EffectiveProvider(user, subjectId, subjectProvider);
        var groupList = user is null ? null : TrustedCallerIdentity.GroupsFor(user, subjectId, provider);
        var roles = string.IsNullOrWhiteSpace(provider)
            ? await _evaluator.GetRolesAsync(subjectId, groupList, applicationCode)
            : await _evaluator.GetRolesForProviderAsync(subjectId, provider, groupList, applicationCode);
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
        await EnsureAdministratorAsync();
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
        await EnsureAdministratorAsync();
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
        [Description("Optional resource instance ID to scope the assignment")] string? resourceInstanceId = null,
        [Description("Application code the role belongs to (required when the role code exists in multiple applications)")] string? applicationCode = null,
        [Description("Identity provider (required when the external ID exists in multiple providers)")] string? subjectProvider = null)
    {
        await EnsureAdministratorAsync();
        var message = string.IsNullOrWhiteSpace(subjectProvider)
            ? await _roleService.AssignToSubjectAsync(subjectExternalId, roleCode, resourceInstanceId, applicationCode)
            : await _roleService.AssignToSubjectForProviderWithExpiryAsync(
                subjectExternalId, subjectProvider, roleCode, resourceInstanceId, applicationCode, expiresAt: null);
        _logger.LogInformation("MCP: {Message}", message);
        return message;
    }

    [McpServerTool]
    [Description("Revoke a role from a user.")]
    public async Task<string> RevokeRoleFromUser(
        [Description("External ID of the user")] string subjectExternalId,
        [Description("Role code to revoke")] string roleCode,
        [Description("Optional resource instance ID")] string? resourceInstanceId = null,
        [Description("Application code the role belongs to (required when the role code exists in multiple applications)")] string? applicationCode = null,
        [Description("Identity provider (required when the external ID exists in multiple providers)")] string? subjectProvider = null)
    {
        await EnsureAdministratorAsync();
        var message = string.IsNullOrWhiteSpace(subjectProvider)
            ? await _roleService.RevokeFromSubjectAsync(subjectExternalId, roleCode, resourceInstanceId, applicationCode)
            : await _roleService.RevokeFromSubjectForProviderAsync(
                subjectExternalId, subjectProvider, roleCode, resourceInstanceId, applicationCode);
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
        await EnsureAdministratorAsync();
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
        [Description("Membership role: Member, Admin, or Owner")] string membershipRole = "Member",
        [Description("Identity provider, required when the external ID is ambiguous")] string? subjectProvider = null)
    {
        await EnsureAdministratorAsync();
        if (!Enum.TryParse<TeamMembershipRole>(membershipRole, true, out var role))
            role = TeamMembershipRole.Member;

        var message = await _teamService.AddMemberAsync(teamCode, subjectExternalId, role, subjectProvider);
        _logger.LogInformation("MCP: {Message}", message);
        return message;
    }

    [McpServerTool]
    [Description("Assign a role to a team (all members inherit this role).")]
    public async Task<string> AssignRoleToTeam(
        [Description("Team code")] string teamCode,
        [Description("Role code to assign")] string roleCode,
        [Description("Application code the role belongs to (required when the role code exists in multiple applications)")] string? applicationCode = null)
    {
        await EnsureAdministratorAsync();
        var message = await _roleService.AssignToTeamAsync(teamCode, roleCode, applicationCode);
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
        await EnsureAdministratorAsync();
        var result = await _subjectService.CreateAsync(new CreateSubjectRequest(externalId, provider, email, displayName));
        var subject = result.Subject;
        _logger.LogInformation("MCP: Created user {ExternalId}", externalId);
        return new McpUserInfo(subject.Id, subject.ExternalId, subject.Provider, subject.Email, subject.DisplayName, subject.IsActive);
    }

    // ==================== Policy Catalog ====================
    // V7: read-only access to the policy catalog. Mutating operations stay
    // on the REST surface so they go through normal admin auth, not MCP.

    [McpServerTool]
    [Description("List policies in the RBAC catalog (read-only, write-branch, sandboxed, no-prod, high-risk, draft-only, plus any tenant-defined policies).")]
    public async Task<List<McpPolicyInfo>> ListPolicies()
    {
        var result = await _policyService.GetAllAsync();
        return result.Policies.Select(MapToMcp).ToList();
    }

    [McpServerTool]
    [Description("Get a policy by code (e.g., 'high-risk', 'no-prod') with full rule body.")]
    public async Task<McpPolicyInfo?> GetPolicy(
        [Description("Policy code (e.g., 'high-risk')")] string code)
    {
        var result = await _policyService.GetByCodeAsync(code);
        return result == null ? null : MapToMcp(result.Policy);
    }

    private static McpPolicyInfo MapToMcp(PolicyDetail p) => new(
        p.Id,
        p.Code,
        p.Name,
        p.Criticality.ToString(),
        p.Rules,
        p.Description,
        p.IsSystem);
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
public record McpPolicyInfo(Guid Id, string Code, string Name, string Criticality, Dictionary<string, object>? Rules, string? Description, bool IsSystem);
