using Andy.Rbac.Infrastructure.Data;
using Andy.Rbac.Models;
using Microsoft.EntityFrameworkCore;

namespace Andy.Rbac.Infrastructure.Repositories;

public class PermissionRepository : IPermissionRepository
{
    private readonly RbacDbContext _db;

    public PermissionRepository(RbacDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Returns every role currently effective for a subject. In addition to
    /// direct SubjectRole assignments this includes roles assigned to active
    /// teams the subject belongs to and to their active ancestor teams.
    /// </summary>
    private async Task<HashSet<Guid>> GetEffectiveRoleIdsAsync(
        Guid subjectId,
        string? resourceInstanceId,
        bool globalOnly,
        CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;

        var directQuery = _db.SubjectRoles
            .Where(sr => sr.SubjectId == subjectId)
            .Where(sr => sr.ExpiresAt == null || sr.ExpiresAt > now);

        directQuery = globalOnly
            ? directQuery.Where(sr => sr.ResourceInstanceId == null)
            : directQuery.Where(sr => sr.ResourceInstanceId == null || sr.ResourceInstanceId == resourceInstanceId);

        var roleIds = (await directQuery.Select(sr => sr.RoleId).ToListAsync(ct)).ToHashSet();

        var memberTeamIds = await _db.TeamMembers
            .Where(tm => tm.SubjectId == subjectId)
            .Select(tm => tm.TeamId)
            .ToListAsync(ct);

        if (memberTeamIds.Count == 0)
            return roleIds;

        // Load the small team graph once and walk towards each parent. Stop at
        // an inactive team: disabling a child team disables both its own grant
        // source and inheritance through that membership path.
        var teams = await _db.Teams
            .AsNoTracking()
            .Select(t => new { t.Id, t.ParentTeamId, t.IsActive })
            .ToDictionaryAsync(t => t.Id, ct);

        var effectiveTeamIds = new HashSet<Guid>();
        foreach (var memberTeamId in memberTeamIds)
        {
            Guid? current = memberTeamId;
            var path = new HashSet<Guid>();
            while (current.HasValue && path.Add(current.Value) && teams.TryGetValue(current.Value, out var team))
            {
                if (!team.IsActive)
                    break;

                effectiveTeamIds.Add(team.Id);
                current = team.ParentTeamId;
            }
        }

        if (effectiveTeamIds.Count == 0)
            return roleIds;

        var teamRoleQuery = _db.TeamRoles
            .Where(tr => effectiveTeamIds.Contains(tr.TeamId))
            .Where(tr => tr.ExpiresAt == null || tr.ExpiresAt > now);

        teamRoleQuery = globalOnly
            ? teamRoleQuery.Where(tr => tr.ResourceInstanceId == null)
            : teamRoleQuery.Where(tr => tr.ResourceInstanceId == null || tr.ResourceInstanceId == resourceInstanceId);

        roleIds.UnionWith(await teamRoleQuery.Select(tr => tr.RoleId).ToListAsync(ct));
        return roleIds;
    }

    /// <summary>
    /// Expand a starting set of role IDs to include all of their ancestors
    /// (parents, grandparents, ...) walking the <c>ParentRoleId</c> chain.
    /// Cycle-safe — the loop terminates as soon as a frontier yields no new
    /// IDs. Bounded depth defends against accidentally-deep hierarchies.
    /// </summary>
    private async Task<HashSet<Guid>> ExpandToAncestorsAsync(
        IEnumerable<Guid> roleIds,
        CancellationToken ct)
    {
        const int maxDepth = 32;
        var closure = new HashSet<Guid>(roleIds);
        var frontier = new HashSet<Guid>(closure);
        for (int d = 0; d < maxDepth && frontier.Count > 0; d++)
        {
            var parents = await _db.Roles
                .Where(r => frontier.Contains(r.Id) && r.ParentRoleId != null)
                .Select(r => r.ParentRoleId!.Value)
                .Distinct()
                .ToListAsync(ct);

            var newOnes = parents.Where(p => !closure.Contains(p)).ToList();
            if (newOnes.Count == 0) break;
            foreach (var p in newOnes) closure.Add(p);
            frontier = new HashSet<Guid>(newOnes);
        }
        return closure;
    }

    public async Task<IReadOnlyList<string>> GetPermissionsForSubjectAsync(
        Guid subjectId,
        string? applicationCode = null,
        CancellationToken ct = default)
    {
        var roleIds = await GetEffectiveRoleIdsAsync(subjectId, resourceInstanceId: null, globalOnly: true, ct);

        if (!roleIds.Any())
            return [];

        // Inheritance: a subject with role R holds the permissions of R
        // and of every ancestor of R (via ParentRoleId). Expand the assigned
        // set to include all ancestors, then match RolePermissions against
        // that expanded set.
        var effectiveRoleIds = await ExpandToAncestorsAsync(roleIds, ct);

        var query = _db.RolePermissions
            .Where(rp => effectiveRoleIds.Contains(rp.RoleId))
            .Include(rp => rp.Permission)
            .ThenInclude(p => p.ResourceType)
            .ThenInclude(rt => rt.Application)
            .Include(rp => rp.Permission)
            .ThenInclude(p => p.Action)
            .Select(rp => rp.Permission);

        if (!string.IsNullOrEmpty(applicationCode))
        {
            query = query.Where(p => p.ResourceType.Application != null && p.ResourceType.Application.Code == applicationCode);
        }

        var permissions = await query
            .Select(p => p.ResourceType.Application!.Code + ":" + p.ResourceType.Code + ":" + p.Action.Code)
            .Distinct()
            .ToListAsync(ct);

        return permissions;
    }

    public async Task<IReadOnlyList<string>> GetRolesForSubjectAsync(
        Guid subjectId,
        string? applicationCode = null,
        CancellationToken ct = default)
    {
        var roleIds = await GetEffectiveRoleIdsAsync(subjectId, resourceInstanceId: null, globalOnly: true, ct);
        var query = _db.Roles.Where(r => roleIds.Contains(r.Id));

        if (!string.IsNullOrEmpty(applicationCode))
        {
            query = query.Where(r => r.ApplicationId == null || r.Application!.Code == applicationCode);
        }

        var roles = await query
            .Select(r => r.Code)
            .Distinct()
            .ToListAsync(ct);

        return roles;
    }

    public async Task<bool> HasPermissionAsync(
        Guid subjectId,
        string permissionCode,
        string? resourceInstanceId = null,
        CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var parts = permissionCode.Split(':');

        if (parts.Length != 3)
            return false;

        var appCode = parts[0];
        var resourceCode = parts[1];
        var actionCode = parts[2];

        var roleIds = await GetEffectiveRoleIdsAsync(subjectId, resourceInstanceId, globalOnly: false, ct);

        if (!roleIds.Any())
        {
            // Check instance-level permissions and ownership below
        }
        else
        {
            // Inherit ancestors (see ExpandToAncestorsAsync).
            var effectiveRoleIds = await ExpandToAncestorsAsync(roleIds, ct);
            var hasRolePermission = await _db.RolePermissions
                .Where(rp => effectiveRoleIds.Contains(rp.RoleId))
                .AnyAsync(rp =>
                    rp.Permission.ResourceType.Application != null &&
                    rp.Permission.ResourceType.Application.Code == appCode &&
                    rp.Permission.ResourceType.Code == resourceCode &&
                    rp.Permission.Action.Code == actionCode, ct);

            if (hasRolePermission)
                return true;
        }

        // Check instance-level permissions if resource instance is specified
        if (!string.IsNullOrEmpty(resourceInstanceId))
        {
            var hasInstancePermission = await _db.InstancePermissions
                .Where(ip => ip.SubjectId == subjectId)
                .Where(ip => ip.ExpiresAt == null || ip.ExpiresAt > now)
                .Where(ip => ip.ResourceInstance.ExternalId == resourceInstanceId)
                .AnyAsync(ip =>
                    ip.Permission.ResourceType.Application.Code == appCode &&
                    ip.Permission.ResourceType.Code == resourceCode &&
                    ip.Permission.Action.Code == actionCode, ct);

            if (hasInstancePermission)
                return true;

            // Check if user is owner
            var isOwner = await _db.ResourceInstances
                .Where(ri => ri.ExternalId == resourceInstanceId)
                .Where(ri => ri.ResourceType.Application.Code == appCode)
                .Where(ri => ri.ResourceType.Code == resourceCode)
                .AnyAsync(ri => ri.Owner != null && ri.Owner.Id == subjectId, ct);

            if (isOwner)
                return true;
        }

        return false;
    }

    public async Task<IReadOnlyList<Permission>> GetInstancePermissionsAsync(
        Guid subjectId,
        Guid resourceInstanceId,
        CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;

        return await _db.InstancePermissions
            .Where(ip => ip.SubjectId == subjectId)
            .Where(ip => ip.ResourceInstanceId == resourceInstanceId)
            .Where(ip => ip.ExpiresAt == null || ip.ExpiresAt > now)
            .Select(ip => ip.Permission)
            .Include(p => p.Action)
            .Include(p => p.ResourceType)
            .ThenInclude(rt => rt.Application)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<string>> GetPermissionsForRolesAsync(
        IEnumerable<Guid> roleIds,
        string? applicationCode = null,
        CancellationToken ct = default)
    {
        var roleIdList = roleIds.ToList();
        if (!roleIdList.Any())
            return [];

        var effectiveRoleIds = await ExpandToAncestorsAsync(roleIdList, ct);

        var query = _db.RolePermissions
            .Where(rp => effectiveRoleIds.Contains(rp.RoleId))
            .Include(rp => rp.Permission)
            .ThenInclude(p => p.ResourceType)
            .ThenInclude(rt => rt.Application)
            .Include(rp => rp.Permission)
            .ThenInclude(p => p.Action)
            .Select(rp => rp.Permission);

        if (!string.IsNullOrEmpty(applicationCode))
        {
            query = query.Where(p => p.ResourceType.Application != null && p.ResourceType.Application.Code == applicationCode);
        }

        var permissions = await query
            .Select(p => p.ResourceType.Application!.Code + ":" + p.ResourceType.Code + ":" + p.Action.Code)
            .Distinct()
            .ToListAsync(ct);

        return permissions;
    }

    public async Task<bool> HasPermissionForRolesAsync(
        IEnumerable<Guid> roleIds,
        string permissionCode,
        string? resourceInstanceId = null,
        CancellationToken ct = default)
    {
        var roleIdList = roleIds.ToList();
        if (!roleIdList.Any())
            return false;

        var parts = permissionCode.Split(':');
        if (parts.Length != 3)
            return false;

        var appCode = parts[0];
        var resourceCode = parts[1];
        var actionCode = parts[2];

        var effectiveRoleIds = await ExpandToAncestorsAsync(roleIdList, ct);

        return await _db.RolePermissions
            .Where(rp => effectiveRoleIds.Contains(rp.RoleId))
            .AnyAsync(rp =>
                rp.Permission.ResourceType.Application != null &&
                rp.Permission.ResourceType.Application.Code == appCode &&
                rp.Permission.ResourceType.Code == resourceCode &&
                rp.Permission.Action.Code == actionCode, ct);
    }
}
