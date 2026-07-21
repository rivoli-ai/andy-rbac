using Andy.Rbac.Api.Authorization;
using Andy.Rbac.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Andy.Rbac.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = RbacAuthorizationPolicies.Administrator)]
public sealed class AuditController : ControllerBase
{
    private readonly RbacDbContext _db;

    public AuditController(RbacDbContext db) => _db = db;

    [HttpGet]
    public async Task<ActionResult<AuditPage>> Get(
        [FromQuery] DateTimeOffset? since = null,
        [FromQuery] string? eventType = null,
        [FromQuery] string? result = null,
        [FromQuery] string? search = null,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 100,
        CancellationToken ct = default)
    {
        skip = Math.Max(0, skip);
        take = Math.Clamp(take, 1, 500);
        var query = _db.AuditLogs.AsNoTracking();
        if (since.HasValue) query = query.Where(log => log.Timestamp >= since.Value);
        if (!string.IsNullOrWhiteSpace(eventType))
            query = query.Where(log => log.EventType == eventType);
        if (!string.IsNullOrWhiteSpace(result))
            query = query.Where(log => log.Result == result);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            var matchingSubjectIds = await _db.Subjects.AsNoTracking()
                .Where(subject => subject.ExternalId.Contains(term))
                .Select(subject => subject.Id)
                .ToListAsync(ct);
            query = query.Where(log =>
                (log.SubjectId.HasValue && matchingSubjectIds.Contains(log.SubjectId.Value)) ||
                (log.ResourceType != null && log.ResourceType.Contains(term)) ||
                (log.ResourceInstanceId != null && log.ResourceInstanceId.Contains(term)) ||
                (log.PermissionCode != null && log.PermissionCode.Contains(term)));
        }

        var total = await query.LongCountAsync(ct);
        var logs = await query.OrderByDescending(log => log.Timestamp)
            .Skip(skip).Take(take).ToListAsync(ct);
        var subjectIds = logs.Where(log => log.SubjectId.HasValue)
            .Select(log => log.SubjectId!.Value).Distinct().ToList();
        var subjects = await _db.Subjects.AsNoTracking()
            .Where(subject => subjectIds.Contains(subject.Id))
            .ToDictionaryAsync(subject => subject.Id, subject => subject.ExternalId, ct);

        var items = logs.Select(log =>
        {
            var parts = log.PermissionCode?.Split(':', 3) ?? [];
            return new AuditEntry(
                log.Id, log.Timestamp, log.EventType,
                log.SubjectId.HasValue && subjects.TryGetValue(log.SubjectId.Value, out var externalId)
                    ? externalId : "unknown",
                parts.Length == 3 ? parts[0] : string.Empty,
                log.ResourceType ?? (parts.Length == 3 ? parts[1] : null),
                log.ResourceInstanceId,
                parts.Length == 3 ? parts[2] : null,
                log.Result ?? string.Empty);
        }).ToList();

        return Ok(new AuditPage(items, total, skip, take));
    }
}

public sealed record AuditEntry(
    Guid Id,
    DateTimeOffset Timestamp,
    string EventType,
    string SubjectExternalId,
    string ApplicationCode,
    string? ResourceType,
    string? ResourceId,
    string? Action,
    string Result);

public sealed record AuditPage(List<AuditEntry> Items, long Total, int Skip, int Take);
