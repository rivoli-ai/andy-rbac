using System.Diagnostics;
using Andy.Rbac.Api.Telemetry;
using Andy.Rbac.Infrastructure.Data;
using Andy.Rbac.Infrastructure.Repositories;
using Andy.Rbac.Models;
using Microsoft.EntityFrameworkCore;

namespace Andy.Rbac.Api.Services;

public class PermissionEvaluator : IPermissionEvaluator
{
    private readonly RbacDbContext _db;
    private readonly IPermissionRepository _permissionRepository;
    private readonly ILogger<PermissionEvaluator> _logger;
    private readonly IHttpContextAccessor? _httpContextAccessor;

    public PermissionEvaluator(
        RbacDbContext db,
        IPermissionRepository permissionRepository,
        ILogger<PermissionEvaluator> logger,
        IHttpContextAccessor? httpContextAccessor = null)
    {
        _db = db;
        _permissionRepository = permissionRepository;
        _logger = logger;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task<PermissionCheckResult> CheckPermissionAsync(
        string subjectExternalId,
        string permission,
        IEnumerable<string>? groups = null,
        string? resourceInstanceId = null,
        CancellationToken ct = default)
        => await CheckPermissionCoreAsync(subjectExternalId, subjectProvider: null, permission, groups, resourceInstanceId, ct);

    public async Task<PermissionCheckResult> CheckPermissionForProviderAsync(
        string subjectExternalId,
        string subjectProvider,
        string permission,
        IEnumerable<string>? groups = null,
        string? resourceInstanceId = null,
        CancellationToken ct = default)
        => await CheckPermissionCoreAsync(subjectExternalId, subjectProvider, permission, groups, resourceInstanceId, ct);

    private async Task<PermissionCheckResult> CheckPermissionCoreAsync(
        string subjectExternalId,
        string? subjectProvider,
        string permission,
        IEnumerable<string>? groups,
        string? resourceInstanceId,
        CancellationToken ct)
    {
        // OT4 (rivoli-ai/conductor#1262). Permission checks are the
        // operation Conductor most wants to attribute when a panel
        // mysteriously 403s — wrap the whole check in a span and emit
        // the rbac.check.count counter on every exit so both the APM
        // waterfall and the dashboards line up on the same data.
        using var activity = RbacTelemetry.ActivitySource.StartActivity(
            "PermissionCheck", ActivityKind.Internal);
        // OT7 (rivoli-ai/conductor#1265). Attributes renamed under the
        // `andy.rbac.*` namespace per docs/semconv-compliance.md. The
        // legacy `rbac.*` names emit alongside for one release per the
        // OT1 dual-emit precedent so existing dashboards keep working.
        activity?.SetTag("andy.rbac.permission", permission);
        activity?.SetTag("andy.rbac.subject_external_id", subjectExternalId);
        activity?.SetTag("rbac.permission", permission);                  // deprecated; removed in 0.3.0
        activity?.SetTag("rbac.subject_external_id", subjectExternalId);  // deprecated; removed in 0.3.0
        if (!string.IsNullOrEmpty(resourceInstanceId))
        {
            activity?.SetTag("andy.rbac.resource_instance_id", resourceInstanceId);
            activity?.SetTag("rbac.resource_instance_id", resourceInstanceId); // deprecated
        }

        Guid? resolvedSubjectId = null;
        var result = await EvaluateAsync();

        var outcome = result.Allowed ? "granted" : "denied";
        activity?.SetTag("andy.rbac.outcome", outcome);
        activity?.SetTag("rbac.outcome", outcome); // deprecated; removed in 0.3.0
        if (!result.Allowed && !string.IsNullOrEmpty(result.Reason))
        {
            activity?.SetTag("andy.rbac.reason", result.Reason);
            activity?.SetTag("rbac.reason", result.Reason); // deprecated
        }
        RbacTelemetry.ChecksTotal.Add(
            1,
            new KeyValuePair<string, object?>("andy.rbac.outcome", outcome),
            new KeyValuePair<string, object?>("andy.rbac.permission", permission));
        await WriteAuditLogAsync(resolvedSubjectId, permission, resourceInstanceId, result, ct);
        return result;

        async Task<PermissionCheckResult> EvaluateAsync()
        {
            // Find subject by external ID
            var resolution = await SubjectResolver.ResolveAsync(
                _db, subjectExternalId, subjectProvider, tracking: false, ct);
            var subject = resolution.Subject;
            resolvedSubjectId = subject?.Id;

            if (resolution.IsAmbiguous)
            {
                _logger.LogWarning("Ambiguous subject external ID: {SubjectExternalId}", subjectExternalId);
                return new PermissionCheckResult(false, "Subject provider is required for an ambiguous external ID");
            }

            if (subject == null)
            {
                _logger.LogDebug("Subject not found: {SubjectExternalId}", subjectExternalId);
                return new PermissionCheckResult(false, "Subject not found");
            }

            if (!subject.IsActive)
            {
                _logger.LogDebug("Subject is inactive: {SubjectExternalId}", subjectExternalId);
                return new PermissionCheckResult(false, "Subject is inactive");
            }

            // Check subject's direct permissions
            var hasPermission = await _permissionRepository.HasPermissionAsync(
                subject.Id,
                permission,
                resourceInstanceId,
                ct);

            if (hasPermission)
            {
                return new PermissionCheckResult(true);
            }

            // Check group-based permissions via ExternalGroupMapping
            if (groups != null)
            {
                var roleIds = await GetRoleIdsForGroupsAsync(groups, subjectProvider ?? subject.Provider, ct);
                if (roleIds.Any())
                {
                    var hasGroupPermission = await _permissionRepository.HasPermissionForRolesAsync(
                        roleIds,
                        permission,
                        resourceInstanceId,
                        ct);

                    if (hasGroupPermission)
                    {
                        _logger.LogDebug("Permission granted via group for subject {SubjectExternalId}", subjectExternalId);
                        return new PermissionCheckResult(true);
                    }
                }
            }

            return new PermissionCheckResult(false, "Permission denied");
        }
    }

    private async Task WriteAuditLogAsync(
        Guid? subjectId,
        string permission,
        string? resourceInstanceId,
        PermissionCheckResult result,
        CancellationToken ct)
    {
        try
        {
            var permissionParts = permission.Split(':', 3);
            var httpContext = _httpContextAccessor?.HttpContext;
            _db.AuditLogs.Add(new RbacAuditLog
            {
                Id = Guid.NewGuid(),
                SubjectId = subjectId,
                EventType = AuditEventTypes.PermissionCheck,
                ResourceType = permissionParts.Length == 3 ? permissionParts[1] : null,
                ResourceInstanceId = resourceInstanceId,
                PermissionCode = permission,
                Result = result.Allowed ? "allowed" : "denied",
                Context = string.IsNullOrWhiteSpace(result.Reason)
                    ? null
                    : new Dictionary<string, object> { ["reason"] = result.Reason },
                IpAddress = httpContext?.Connection.RemoteIpAddress?.ToString(),
                UserAgent = httpContext?.Request.Headers.UserAgent.ToString()
            });
            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            // Audit persistence must never change the authorization decision.
            _logger.LogError(ex, "Failed to persist RBAC permission-check audit event");
        }
    }

    public async Task<PermissionCheckResult> CheckAnyPermissionAsync(
        string subjectExternalId,
        IEnumerable<string> permissions,
        IEnumerable<string>? groups = null,
        string? resourceInstanceId = null,
        CancellationToken ct = default)
    {
        foreach (var permission in permissions)
        {
            var result = await CheckPermissionAsync(subjectExternalId, permission, groups, resourceInstanceId, ct);
            if (result.Allowed)
                return result;
        }

        return new PermissionCheckResult(false, "None of the required permissions found");
    }

    public async Task<PermissionCheckResult> CheckAnyPermissionForProviderAsync(
        string subjectExternalId,
        string subjectProvider,
        IEnumerable<string> permissions,
        IEnumerable<string>? groups = null,
        string? resourceInstanceId = null,
        CancellationToken ct = default)
    {
        foreach (var permission in permissions)
        {
            var result = await CheckPermissionForProviderAsync(
                subjectExternalId, subjectProvider, permission, groups, resourceInstanceId, ct);
            if (result.Allowed)
                return result;
        }

        return new PermissionCheckResult(false, "None of the required permissions found");
    }

    public async Task<IReadOnlyList<string>> GetPermissionsAsync(
        string subjectExternalId,
        IEnumerable<string>? groups = null,
        string? applicationCode = null,
        CancellationToken ct = default)
        => await GetPermissionsCoreAsync(subjectExternalId, subjectProvider: null, groups, applicationCode, ct);

    public async Task<IReadOnlyList<string>> GetPermissionsForProviderAsync(
        string subjectExternalId,
        string subjectProvider,
        IEnumerable<string>? groups = null,
        string? applicationCode = null,
        CancellationToken ct = default)
        => await GetPermissionsCoreAsync(subjectExternalId, subjectProvider, groups, applicationCode, ct);

    private async Task<IReadOnlyList<string>> GetPermissionsCoreAsync(
        string subjectExternalId,
        string? subjectProvider,
        IEnumerable<string>? groups,
        string? applicationCode,
        CancellationToken ct)
    {
        var permissions = new HashSet<string>();

        var resolution = await SubjectResolver.ResolveAsync(
            _db, subjectExternalId, subjectProvider, tracking: false, ct);
        var subject = resolution.Subject;

        if (resolution.IsAmbiguous || subject is null || !subject.IsActive)
            return [];

        var subjectPermissions = await _permissionRepository.GetPermissionsForSubjectAsync(
            subject.Id,
            applicationCode,
            ct);
        foreach (var p in subjectPermissions)
            permissions.Add(p);

        // Add permissions from groups
        if (groups != null)
        {
            var roleIds = await GetRoleIdsForGroupsAsync(groups, subjectProvider ?? subject.Provider, ct);
            if (roleIds.Any())
            {
                var groupPermissions = await _permissionRepository.GetPermissionsForRolesAsync(
                    roleIds,
                    applicationCode,
                    ct);
                foreach (var p in groupPermissions)
                    permissions.Add(p);
            }
        }

        return permissions.ToList();
    }

    public async Task<IReadOnlyList<string>> GetRolesAsync(
        string subjectExternalId,
        IEnumerable<string>? groups = null,
        string? applicationCode = null,
        CancellationToken ct = default)
        => await GetRolesCoreAsync(subjectExternalId, subjectProvider: null, groups, applicationCode, ct);

    public async Task<IReadOnlyList<string>> GetRolesForProviderAsync(
        string subjectExternalId,
        string subjectProvider,
        IEnumerable<string>? groups = null,
        string? applicationCode = null,
        CancellationToken ct = default)
        => await GetRolesCoreAsync(subjectExternalId, subjectProvider, groups, applicationCode, ct);

    private async Task<IReadOnlyList<string>> GetRolesCoreAsync(
        string subjectExternalId,
        string? subjectProvider,
        IEnumerable<string>? groups,
        string? applicationCode,
        CancellationToken ct)
    {
        var roles = new HashSet<string>();

        var resolution = await SubjectResolver.ResolveAsync(
            _db, subjectExternalId, subjectProvider, tracking: false, ct);
        var subject = resolution.Subject;

        if (resolution.IsAmbiguous || subject is null || !subject.IsActive)
            return [];

        var subjectRoles = await _permissionRepository.GetRolesForSubjectAsync(
            subject.Id,
            applicationCode,
            ct);
        foreach (var r in subjectRoles)
            roles.Add(r);

        // Add roles from groups
        if (groups != null)
        {
            var groupRoles = await _db.ExternalGroupMappings
                .AsNoTracking()
                .Where(m => groups.Contains(m.ExternalGroupId) || groups.Contains(m.ExternalGroupName))
                .Where(m => m.SyncEnabled)
                .Where(m => m.Provider == (subjectProvider ?? subject.Provider))
                .Include(m => m.Role)
                .Where(m => applicationCode == null || m.Role.ApplicationId == null || m.Role.Application!.Code == applicationCode)
                .Select(m => m.Role.Code)
                .ToListAsync(ct);

            foreach (var r in groupRoles)
                roles.Add(r);
        }

        return roles.ToList();
    }

    /// <summary>
    /// Gets role IDs from ExternalGroupMapping for the given group codes.
    /// Groups can be matched by ExternalGroupId or ExternalGroupName.
    /// </summary>
    private async Task<List<Guid>> GetRoleIdsForGroupsAsync(
        IEnumerable<string> groups,
        string? provider,
        CancellationToken ct)
    {
        var groupList = groups.ToList();
        if (!groupList.Any())
            return [];

        return await _db.ExternalGroupMappings
            .AsNoTracking()
            .Where(m => groupList.Contains(m.ExternalGroupId) || (m.ExternalGroupName != null && groupList.Contains(m.ExternalGroupName)))
            .Where(m => m.SyncEnabled)
            .Where(m => provider == null || m.Provider == provider)
            .Select(m => m.RoleId)
            .Distinct()
            .ToListAsync(ct);
    }
}
