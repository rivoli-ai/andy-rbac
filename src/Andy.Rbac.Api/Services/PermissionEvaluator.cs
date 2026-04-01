using Andy.Rbac.Infrastructure.Data;
using Andy.Rbac.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Andy.Rbac.Api.Services;

public class PermissionEvaluator : IPermissionEvaluator
{
    private readonly RbacDbContext _db;
    private readonly IPermissionRepository _permissionRepository;
    private readonly ILogger<PermissionEvaluator> _logger;

    public PermissionEvaluator(
        RbacDbContext db,
        IPermissionRepository permissionRepository,
        ILogger<PermissionEvaluator> logger)
    {
        _db = db;
        _permissionRepository = permissionRepository;
        _logger = logger;
    }

    public async Task<PermissionCheckResult> CheckPermissionAsync(
        string subjectExternalId,
        string permission,
        IEnumerable<string>? groups = null,
        string? resourceInstanceId = null,
        CancellationToken ct = default)
    {
        // Find subject by external ID
        var subject = await _db.Subjects
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.ExternalId == subjectExternalId, ct);

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
            var roleIds = await GetRoleIdsForGroupsAsync(groups, ct);
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

    public async Task<IReadOnlyList<string>> GetPermissionsAsync(
        string subjectExternalId,
        IEnumerable<string>? groups = null,
        string? applicationCode = null,
        CancellationToken ct = default)
    {
        var permissions = new HashSet<string>();

        var subject = await _db.Subjects
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.ExternalId == subjectExternalId, ct);

        if (subject != null && subject.IsActive)
        {
            var subjectPermissions = await _permissionRepository.GetPermissionsForSubjectAsync(
                subject.Id,
                applicationCode,
                ct);
            foreach (var p in subjectPermissions)
                permissions.Add(p);
        }

        // Add permissions from groups
        if (groups != null)
        {
            var roleIds = await GetRoleIdsForGroupsAsync(groups, ct);
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
    {
        var roles = new HashSet<string>();

        var subject = await _db.Subjects
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.ExternalId == subjectExternalId, ct);

        if (subject != null && subject.IsActive)
        {
            var subjectRoles = await _permissionRepository.GetRolesForSubjectAsync(
                subject.Id,
                applicationCode,
                ct);
            foreach (var r in subjectRoles)
                roles.Add(r);
        }

        // Add roles from groups
        if (groups != null)
        {
            var groupRoles = await _db.ExternalGroupMappings
                .AsNoTracking()
                .Where(m => groups.Contains(m.ExternalGroupId) || groups.Contains(m.ExternalGroupName))
                .Where(m => m.SyncEnabled)
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
    private async Task<List<Guid>> GetRoleIdsForGroupsAsync(IEnumerable<string> groups, CancellationToken ct)
    {
        var groupList = groups.ToList();
        if (!groupList.Any())
            return [];

        return await _db.ExternalGroupMappings
            .AsNoTracking()
            .Where(m => groupList.Contains(m.ExternalGroupId) || (m.ExternalGroupName != null && groupList.Contains(m.ExternalGroupName)))
            .Where(m => m.SyncEnabled)
            .Select(m => m.RoleId)
            .Distinct()
            .ToListAsync(ct);
    }
}
