using Andy.Rbac.Infrastructure.Data;
using Andy.Rbac.Models;
using Microsoft.EntityFrameworkCore;

namespace Andy.Rbac.Api.Services;

/// <summary>
/// Service for managing roles.
/// </summary>
public class RoleService : IRoleService
{
    private readonly RbacDbContext _db;
    private readonly ILogger<RoleService> _logger;

    public RoleService(RbacDbContext db, ILogger<RoleService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<RoleListResult> GetAllAsync(string? applicationCode = null, CancellationToken ct = default)
    {
        var query = _db.Roles
            .Include(r => r.Application)
            .Include(r => r.ParentRole)
            .AsNoTracking();

        if (!string.IsNullOrEmpty(applicationCode))
        {
            query = query.Where(r => r.ApplicationId == null || r.Application!.Code == applicationCode);
        }

        var roles = await query
            .OrderBy(r => r.Name)
            .Select(r => new RoleDetail(
                r.Id,
                r.Code,
                r.Name,
                r.Description,
                r.Application != null ? r.Application.Code : null,
                r.ParentRole != null ? r.ParentRole.Code : null,
                r.IsSystem,
                new List<string>()))
            .ToListAsync(ct);

        return new RoleListResult(roles);
    }

    public async Task<RoleDetailResult?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var role = await _db.Roles
            .Include(r => r.Application)
            .Include(r => r.ParentRole)
            .Include(r => r.RolePermissions)
            .ThenInclude(rp => rp.Permission)
            .ThenInclude(p => p.Action)
            .Include(r => r.RolePermissions)
            .ThenInclude(rp => rp.Permission)
            .ThenInclude(p => p.ResourceType)
            .ThenInclude(rt => rt.Application)
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == id, ct);

        return role == null ? null : MapToDetailResult(role);
    }

    public async Task<RoleDetailResult?> GetByCodeAsync(string code, string? applicationCode = null, CancellationToken ct = default)
    {
        var query = _db.Roles
            .Include(r => r.Application)
            .Include(r => r.ParentRole)
            .Include(r => r.RolePermissions)
            .ThenInclude(rp => rp.Permission)
            .ThenInclude(p => p.Action)
            .Include(r => r.RolePermissions)
            .ThenInclude(rp => rp.Permission)
            .ThenInclude(p => p.ResourceType)
            .ThenInclude(rt => rt.Application)
            .AsNoTracking()
            .Where(r => r.Code == code);

        if (!string.IsNullOrEmpty(applicationCode))
        {
            query = query.Where(r => r.Application != null && r.Application.Code == applicationCode);
        }

        var role = await query.FirstOrDefaultAsync(ct);
        return role == null ? null : MapToDetailResult(role);
    }

    public async Task<RoleDetailResult> CreateAsync(CreateRoleRequest request, CancellationToken ct = default)
    {
        Guid? applicationId = null;
        if (!string.IsNullOrEmpty(request.ApplicationCode))
        {
            var app = await _db.Applications.FirstOrDefaultAsync(a => a.Code == request.ApplicationCode, ct);
            if (app == null)
                throw new InvalidOperationException($"Application '{request.ApplicationCode}' not found");
            applicationId = app.Id;
        }

        Guid? parentRoleId = null;
        if (!string.IsNullOrEmpty(request.ParentRoleCode))
        {
            var parent = await _db.Roles.FirstOrDefaultAsync(r => r.Code == request.ParentRoleCode, ct);
            if (parent == null)
                throw new InvalidOperationException($"Parent role '{request.ParentRoleCode}' not found");
            parentRoleId = parent.Id;
        }

        var role = new Role
        {
            Code = request.Code,
            Name = request.Name,
            Description = request.Description,
            ApplicationId = applicationId,
            ParentRoleId = parentRoleId,
            IsSystem = false
        };

        _db.Roles.Add(role);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Created role {RoleCode}", role.Code);

        return new RoleDetailResult(new RoleDetail(
            role.Id,
            role.Code,
            role.Name,
            role.Description,
            request.ApplicationCode,
            request.ParentRoleCode,
            role.IsSystem,
            []));
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var role = await _db.Roles.FindAsync([id], ct);
        if (role == null)
            return false;

        if (role.IsSystem)
            throw new InvalidOperationException("Cannot delete system roles");

        _db.Roles.Remove(role);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Deleted role {RoleCode}", role.Code);

        return true;
    }

    public async Task<string> AssignToSubjectAsync(string subjectExternalId, string roleCode, string? resourceInstanceId = null, CancellationToken ct = default)
    {
        var subject = await _db.Subjects.FirstOrDefaultAsync(s => s.ExternalId == subjectExternalId, ct);
        if (subject == null)
            return $"Error: Subject '{subjectExternalId}' not found";

        var role = await _db.Roles.FirstOrDefaultAsync(r => r.Code == roleCode, ct);
        if (role == null)
            return $"Error: Role '{roleCode}' not found";

        var existing = await _db.SubjectRoles
            .AnyAsync(sr => sr.SubjectId == subject.Id && sr.RoleId == role.Id && sr.ResourceInstanceId == resourceInstanceId, ct);

        if (existing)
            return $"Role '{roleCode}' is already assigned to user";

        _db.SubjectRoles.Add(new SubjectRole
        {
            SubjectId = subject.Id,
            RoleId = role.Id,
            ResourceInstanceId = resourceInstanceId
        });

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Assigned role {RoleCode} to {SubjectId}", roleCode, subjectExternalId);

        return $"Successfully assigned role '{roleCode}' to user '{subjectExternalId}'";
    }

    public async Task<string> RevokeFromSubjectAsync(string subjectExternalId, string roleCode, string? resourceInstanceId = null, CancellationToken ct = default)
    {
        var subject = await _db.Subjects.FirstOrDefaultAsync(s => s.ExternalId == subjectExternalId, ct);
        if (subject == null)
            return $"Error: Subject '{subjectExternalId}' not found";

        var role = await _db.Roles.FirstOrDefaultAsync(r => r.Code == roleCode, ct);
        if (role == null)
            return $"Error: Role '{roleCode}' not found";

        var assignment = await _db.SubjectRoles
            .FirstOrDefaultAsync(sr => sr.SubjectId == subject.Id && sr.RoleId == role.Id && sr.ResourceInstanceId == resourceInstanceId, ct);

        if (assignment == null)
            return $"Role '{roleCode}' is not assigned to user";

        _db.SubjectRoles.Remove(assignment);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Revoked role {RoleCode} from {SubjectId}", roleCode, subjectExternalId);

        return $"Successfully revoked role '{roleCode}' from user '{subjectExternalId}'";
    }

    public async Task<string> AssignToTeamAsync(string teamCode, string roleCode, CancellationToken ct = default)
    {
        var team = await _db.Teams.FirstOrDefaultAsync(t => t.Code == teamCode, ct);
        if (team == null)
            return $"Error: Team '{teamCode}' not found";

        var role = await _db.Roles.FirstOrDefaultAsync(r => r.Code == roleCode, ct);
        if (role == null)
            return $"Error: Role '{roleCode}' not found";

        if (await _db.TeamRoles.AnyAsync(tr => tr.TeamId == team.Id && tr.RoleId == role.Id, ct))
            return $"Role '{roleCode}' is already assigned to team";

        _db.TeamRoles.Add(new TeamRole { TeamId = team.Id, RoleId = role.Id });
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Assigned role {RoleCode} to team {TeamCode}", roleCode, teamCode);

        return $"Successfully assigned role '{roleCode}' to team '{teamCode}'";
    }

    private static RoleDetailResult MapToDetailResult(Role role)
    {
        var permissions = role.RolePermissions
            .Select(rp =>
            {
                var appCode = rp.Permission.ResourceType.Application?.Code ?? "global";
                return $"{appCode}:{rp.Permission.ResourceType.Code}:{rp.Permission.Action.Code}";
            })
            .ToList();

        return new RoleDetailResult(new RoleDetail(
            role.Id,
            role.Code,
            role.Name,
            role.Description,
            role.Application?.Code,
            role.ParentRole?.Code,
            role.IsSystem,
            permissions));
    }
}
