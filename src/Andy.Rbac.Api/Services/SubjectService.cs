using Andy.Rbac.Infrastructure.Data;
using Andy.Rbac.Models;
using Microsoft.EntityFrameworkCore;

namespace Andy.Rbac.Api.Services;

/// <summary>
/// Service for managing subjects (users and service accounts).
/// </summary>
public class SubjectService : ISubjectService
{
    private readonly RbacDbContext _db;
    private readonly ILogger<SubjectService> _logger;

    public SubjectService(RbacDbContext db, ILogger<SubjectService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<SubjectListResult> SearchAsync(string? query = null, int limit = 20, CancellationToken ct = default)
    {
        var dbQuery = _db.Subjects.AsNoTracking();

        if (!string.IsNullOrEmpty(query))
        {
            dbQuery = dbQuery.Where(s =>
                (s.Email != null && s.Email.Contains(query)) ||
                (s.DisplayName != null && s.DisplayName.Contains(query)) ||
                s.ExternalId.Contains(query));
        }

        var subjects = await dbQuery
            .OrderBy(s => s.DisplayName ?? s.ExternalId)
            .Take(limit)
            .Select(s => new SubjectSummary(
                s.Id,
                s.ExternalId,
                s.Provider,
                s.Email,
                s.DisplayName,
                s.IsActive))
            .ToListAsync(ct);

        return new SubjectListResult(subjects);
    }

    public async Task<SubjectDetailResult?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var subject = await GetSubjectWithDetails()
            .FirstOrDefaultAsync(s => s.Id == id, ct);

        return subject == null ? null : await MapToDetailResultAsync(subject, ct);
    }

    public async Task<SubjectDetailResult?> GetByExternalIdAsync(string externalId, CancellationToken ct = default)
    {
        var subject = await GetSubjectWithDetails()
            .FirstOrDefaultAsync(s => s.ExternalId == externalId, ct);

        return subject == null ? null : await MapToDetailResultAsync(subject, ct);
    }

    public async Task<SubjectDetailResult> CreateAsync(CreateSubjectRequest request, CancellationToken ct = default)
    {
        if (await _db.Subjects.AnyAsync(s => s.ExternalId == request.ExternalId && s.Provider == request.Provider, ct))
            throw new InvalidOperationException($"Subject with external ID '{request.ExternalId}' and provider '{request.Provider}' already exists");

        var subject = new Subject
        {
            ExternalId = request.ExternalId,
            Provider = request.Provider,
            Email = request.Email,
            DisplayName = request.DisplayName
        };

        _db.Subjects.Add(subject);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Created subject {ExternalId} from provider {Provider}", request.ExternalId, request.Provider);

        return new SubjectDetailResult(new SubjectDetail(
            subject.Id,
            subject.ExternalId,
            subject.Provider,
            subject.Email,
            subject.DisplayName,
            subject.IsActive,
            subject.CreatedAt,
            [],
            []));
    }

    public async Task<SubjectDetailResult?> UpdateAsync(Guid id, UpdateSubjectRequest request, CancellationToken ct = default)
    {
        var subject = await GetSubjectWithDetails()
            .FirstOrDefaultAsync(s => s.Id == id, ct);

        if (subject == null)
            return null;

        // Re-attach for update
        var tracked = await _db.Subjects.FindAsync([id], ct);
        if (tracked == null) return null;

        if (request.Email != null)
            tracked.Email = request.Email;
        if (request.DisplayName != null)
            tracked.DisplayName = request.DisplayName;
        if (request.IsActive.HasValue)
            tracked.IsActive = request.IsActive.Value;

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Updated subject {ExternalId}", tracked.ExternalId);

        // Re-fetch with details
        return await GetByIdAsync(id, ct);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var subject = await _db.Subjects.FindAsync([id], ct);
        if (subject == null)
            return false;

        _db.Subjects.Remove(subject);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Deleted subject {ExternalId}", subject.ExternalId);

        return true;
    }

    public async Task<SubjectDetailResult> GetOrCreateAsync(string externalId, string provider, string? email = null, string? displayName = null, CancellationToken ct = default)
    {
        var existing = await GetByExternalIdAsync(externalId, ct);
        if (existing != null)
            return existing;

        return await CreateAsync(new CreateSubjectRequest(externalId, provider, email, displayName), ct);
    }

    private IQueryable<Subject> GetSubjectWithDetails()
    {
        return _db.Subjects
            .Include(s => s.SubjectRoles)
            .ThenInclude(sr => sr.Role)
            .ThenInclude(r => r.Application)
            .AsNoTracking();
    }

    private async Task<SubjectDetailResult> MapToDetailResultAsync(Subject subject, CancellationToken ct = default)
    {
        var teams = await _db.TeamMembers
            .Include(tm => tm.Team)
            .Where(tm => tm.SubjectId == subject.Id)
            .Select(tm => new SubjectTeamInfo(
                tm.Team.Code,
                tm.Team.Name,
                tm.MembershipRole.ToString()))
            .ToListAsync(ct);

        return new SubjectDetailResult(new SubjectDetail(
            subject.Id,
            subject.ExternalId,
            subject.Provider,
            subject.Email,
            subject.DisplayName,
            subject.IsActive,
            subject.CreatedAt,
            subject.SubjectRoles.Select(sr => new SubjectRoleInfo(
                sr.Role.Code,
                sr.Role.Application?.Code,
                sr.ResourceInstanceId)).ToList(),
            teams));
    }
}
