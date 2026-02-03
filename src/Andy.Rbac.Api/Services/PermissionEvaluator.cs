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

        var hasPermission = await _permissionRepository.HasPermissionAsync(
            subject.Id,
            permission,
            resourceInstanceId,
            ct);

        if (hasPermission)
        {
            return new PermissionCheckResult(true);
        }

        return new PermissionCheckResult(false, "Permission denied");
    }

    public async Task<PermissionCheckResult> CheckAnyPermissionAsync(
        string subjectExternalId,
        IEnumerable<string> permissions,
        string? resourceInstanceId = null,
        CancellationToken ct = default)
    {
        foreach (var permission in permissions)
        {
            var result = await CheckPermissionAsync(subjectExternalId, permission, resourceInstanceId, ct);
            if (result.Allowed)
                return result;
        }

        return new PermissionCheckResult(false, "None of the required permissions found");
    }

    public async Task<IReadOnlyList<string>> GetPermissionsAsync(
        string subjectExternalId,
        string? applicationCode = null,
        CancellationToken ct = default)
    {
        var subject = await _db.Subjects
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.ExternalId == subjectExternalId, ct);

        if (subject == null || !subject.IsActive)
            return [];

        return await _permissionRepository.GetPermissionsForSubjectAsync(
            subject.Id,
            applicationCode,
            ct);
    }

    public async Task<IReadOnlyList<string>> GetRolesAsync(
        string subjectExternalId,
        string? applicationCode = null,
        CancellationToken ct = default)
    {
        var subject = await _db.Subjects
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.ExternalId == subjectExternalId, ct);

        if (subject == null || !subject.IsActive)
            return [];

        return await _permissionRepository.GetRolesForSubjectAsync(
            subject.Id,
            applicationCode,
            ct);
    }
}
