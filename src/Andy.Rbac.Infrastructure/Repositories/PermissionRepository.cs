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

    public async Task<IReadOnlyList<string>> GetPermissionsForSubjectAsync(
        Guid subjectId,
        string? applicationCode = null,
        CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;

        // Get permissions from role assignments
        var rolePermissionsQuery = _db.SubjectRoles
            .Where(sr => sr.SubjectId == subjectId)
            .Where(sr => sr.ExpiresAt == null || sr.ExpiresAt > now)
            .Where(sr => sr.ResourceInstanceId == null) // Only global role assignments
            .SelectMany(sr => GetRoleWithInheritedPermissions(sr.RoleId))
            .SelectMany(r => r.RolePermissions)
            .Select(rp => rp.Permission);

        if (!string.IsNullOrEmpty(applicationCode))
        {
            rolePermissionsQuery = rolePermissionsQuery
                .Where(p => p.ResourceType.Application.Code == applicationCode);
        }

        var permissions = await rolePermissionsQuery
            .Select(p => $"{p.ResourceType.Application.Code}:{p.ResourceType.Code}:{p.Action.Code}")
            .Distinct()
            .ToListAsync(ct);

        return permissions;
    }

    public async Task<IReadOnlyList<string>> GetRolesForSubjectAsync(
        Guid subjectId,
        string? applicationCode = null,
        CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;

        var query = _db.SubjectRoles
            .Where(sr => sr.SubjectId == subjectId)
            .Where(sr => sr.ExpiresAt == null || sr.ExpiresAt > now)
            .Where(sr => sr.ResourceInstanceId == null)
            .Select(sr => sr.Role);

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

        // Check role-based permissions
        var hasRolePermission = await _db.SubjectRoles
            .Where(sr => sr.SubjectId == subjectId)
            .Where(sr => sr.ExpiresAt == null || sr.ExpiresAt > now)
            .Where(sr => sr.ResourceInstanceId == null || sr.ResourceInstanceId == resourceInstanceId)
            .SelectMany(sr => GetRoleWithInheritedPermissions(sr.RoleId))
            .SelectMany(r => r.RolePermissions)
            .AnyAsync(rp =>
                rp.Permission.ResourceType.Application.Code == appCode &&
                rp.Permission.ResourceType.Code == resourceCode &&
                rp.Permission.Action.Code == actionCode, ct);

        if (hasRolePermission)
            return true;

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

    /// <summary>
    /// Gets a role and all its parent roles (for inheritance).
    /// </summary>
    private IQueryable<Role> GetRoleWithInheritedPermissions(Guid roleId)
    {
        // This is a recursive CTE in PostgreSQL
        // For simplicity, we'll use a simpler approach with limited depth
        // In production, you might want to use a proper recursive CTE
        return _db.Roles
            .Where(r => r.Id == roleId)
            .Include(r => r.ParentRole)
            .ThenInclude(p => p!.ParentRole)
            .ThenInclude(p => p!.ParentRole); // Support up to 3 levels of inheritance
    }
}
