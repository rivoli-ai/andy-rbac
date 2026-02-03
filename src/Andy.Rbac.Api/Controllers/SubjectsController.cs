using Andy.Rbac.Infrastructure.Data;
using Andy.Rbac.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Andy.Rbac.Api.Controllers;

/// <summary>
/// Subject (user/service account) management endpoints.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class SubjectsController : ControllerBase
{
    private readonly RbacDbContext _db;
    private readonly ILogger<SubjectsController> _logger;

    public SubjectsController(RbacDbContext db, ILogger<SubjectsController> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// Searches for subjects.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<SubjectDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> SearchSubjects(
        [FromQuery] string? query,
        [FromQuery] string? provider,
        [FromQuery] SubjectType? type,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50,
        CancellationToken ct = default)
    {
        var q = _db.Subjects.AsNoTracking();

        if (!string.IsNullOrEmpty(query))
        {
            q = q.Where(s =>
                s.Email != null && s.Email.Contains(query) ||
                s.DisplayName != null && s.DisplayName.Contains(query) ||
                s.ExternalId.Contains(query));
        }

        if (!string.IsNullOrEmpty(provider))
            q = q.Where(s => s.Provider == provider);

        if (type.HasValue)
            q = q.Where(s => s.Type == type.Value);

        var total = await q.CountAsync(ct);

        var subjects = await q
            .OrderBy(s => s.DisplayName ?? s.Email ?? s.ExternalId)
            .Skip(skip)
            .Take(take)
            .Select(s => new SubjectDto
            {
                Id = s.Id,
                ExternalId = s.ExternalId,
                Provider = s.Provider,
                Type = s.Type,
                Email = s.Email,
                DisplayName = s.DisplayName,
                IsActive = s.IsActive,
                CreatedAt = s.CreatedAt,
                LastSeenAt = s.LastSeenAt
            })
            .ToListAsync(ct);

        return Ok(new PagedResult<SubjectDto>
        {
            Items = subjects,
            Total = total,
            Skip = skip,
            Take = take
        });
    }

    /// <summary>
    /// Gets a subject by ID.
    /// </summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(SubjectDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetSubject(Guid id, CancellationToken ct)
    {
        var subject = await _db.Subjects
            .Include(s => s.SubjectRoles)
            .ThenInclude(sr => sr.Role)
            .ThenInclude(r => r.Application)
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == id, ct);

        if (subject == null)
            return NotFound();

        return Ok(MapToDetailDto(subject));
    }

    /// <summary>
    /// Gets a subject by external ID and provider.
    /// </summary>
    [HttpGet("by-external/{provider}/{externalId}")]
    [ProducesResponseType(typeof(SubjectDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetSubjectByExternalId(string provider, string externalId, CancellationToken ct)
    {
        var subject = await _db.Subjects
            .Include(s => s.SubjectRoles)
            .ThenInclude(sr => sr.Role)
            .ThenInclude(r => r.Application)
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Provider == provider && s.ExternalId == externalId, ct);

        if (subject == null)
            return NotFound();

        return Ok(MapToDetailDto(subject));
    }

    /// <summary>
    /// Creates or updates a subject (provision/sync).
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(SubjectDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(SubjectDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> ProvisionSubject([FromBody] ProvisionSubjectRequest request, CancellationToken ct)
    {
        var existing = await _db.Subjects
            .FirstOrDefaultAsync(s => s.Provider == request.Provider && s.ExternalId == request.ExternalId, ct);

        if (existing != null)
        {
            // Update existing
            existing.Email = request.Email ?? existing.Email;
            existing.DisplayName = request.DisplayName ?? existing.DisplayName;
            existing.LastSeenAt = DateTimeOffset.UtcNow;
            if (request.Metadata != null)
                existing.Metadata = request.Metadata;

            await _db.SaveChangesAsync(ct);

            return Ok(MapToDto(existing));
        }

        // Create new
        var subject = new Subject
        {
            ExternalId = request.ExternalId,
            Provider = request.Provider,
            Type = request.Type,
            Email = request.Email,
            DisplayName = request.DisplayName,
            Metadata = request.Metadata,
            IsActive = true
        };

        _db.Subjects.Add(subject);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Provisioned subject {SubjectId} from {Provider}", subject.ExternalId, subject.Provider);

        return CreatedAtAction(nameof(GetSubject), new { id = subject.Id }, MapToDto(subject));
    }

    /// <summary>
    /// Updates a subject.
    /// </summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(SubjectDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateSubject(Guid id, [FromBody] UpdateSubjectRequest request, CancellationToken ct)
    {
        var subject = await _db.Subjects.FindAsync([id], ct);
        if (subject == null)
            return NotFound();

        if (request.Email != null) subject.Email = request.Email;
        if (request.DisplayName != null) subject.DisplayName = request.DisplayName;
        if (request.IsActive.HasValue) subject.IsActive = request.IsActive.Value;
        if (request.Metadata != null) subject.Metadata = request.Metadata;

        await _db.SaveChangesAsync(ct);

        return Ok(MapToDto(subject));
    }

    /// <summary>
    /// Deactivates a subject.
    /// </summary>
    [HttpPost("{id:guid}/deactivate")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeactivateSubject(Guid id, CancellationToken ct)
    {
        var subject = await _db.Subjects.FindAsync([id], ct);
        if (subject == null)
            return NotFound();

        subject.IsActive = false;
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Deactivated subject {SubjectId}", subject.ExternalId);

        return NoContent();
    }

    /// <summary>
    /// Assigns a role to a subject.
    /// </summary>
    [HttpPost("{id:guid}/roles")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> AssignRole(Guid id, [FromBody] AssignRoleRequest request, CancellationToken ct)
    {
        var subject = await _db.Subjects.FindAsync([id], ct);
        if (subject == null)
            return NotFound("Subject not found");

        var role = await _db.Roles.FirstOrDefaultAsync(r => r.Code == request.RoleCode, ct);
        if (role == null)
            return NotFound($"Role '{request.RoleCode}' not found");

        // Check if already assigned
        var existing = await _db.SubjectRoles
            .FirstOrDefaultAsync(sr =>
                sr.SubjectId == id &&
                sr.RoleId == role.Id &&
                sr.ResourceInstanceId == request.ResourceInstanceId, ct);

        if (existing != null)
            return BadRequest("Role already assigned");

        var subjectRole = new SubjectRole
        {
            SubjectId = id,
            RoleId = role.Id,
            ResourceInstanceId = request.ResourceInstanceId,
            ExpiresAt = request.ExpiresAt
        };

        _db.SubjectRoles.Add(subjectRole);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Assigned role {RoleCode} to subject {SubjectId}", request.RoleCode, subject.ExternalId);

        return Created("", new { SubjectId = id, RoleCode = request.RoleCode });
    }

    /// <summary>
    /// Revokes a role from a subject.
    /// </summary>
    [HttpDelete("{id:guid}/roles/{roleCode}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RevokeRole(Guid id, string roleCode, [FromQuery] string? resourceInstanceId, CancellationToken ct)
    {
        var role = await _db.Roles.FirstOrDefaultAsync(r => r.Code == roleCode, ct);
        if (role == null)
            return NotFound($"Role '{roleCode}' not found");

        var subjectRole = await _db.SubjectRoles
            .FirstOrDefaultAsync(sr =>
                sr.SubjectId == id &&
                sr.RoleId == role.Id &&
                sr.ResourceInstanceId == resourceInstanceId, ct);

        if (subjectRole == null)
            return NotFound("Role assignment not found");

        _db.SubjectRoles.Remove(subjectRole);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Revoked role {RoleCode} from subject {SubjectId}", roleCode, id);

        return NoContent();
    }

    private static SubjectDto MapToDto(Subject s) => new()
    {
        Id = s.Id,
        ExternalId = s.ExternalId,
        Provider = s.Provider,
        Type = s.Type,
        Email = s.Email,
        DisplayName = s.DisplayName,
        IsActive = s.IsActive,
        CreatedAt = s.CreatedAt,
        LastSeenAt = s.LastSeenAt
    };

    private static SubjectDetailDto MapToDetailDto(Subject s) => new()
    {
        Id = s.Id,
        ExternalId = s.ExternalId,
        Provider = s.Provider,
        Type = s.Type,
        Email = s.Email,
        DisplayName = s.DisplayName,
        IsActive = s.IsActive,
        CreatedAt = s.CreatedAt,
        LastSeenAt = s.LastSeenAt,
        Metadata = s.Metadata,
        Roles = s.SubjectRoles.Select(sr => new SubjectRoleDto
        {
            RoleCode = sr.Role.Code,
            RoleName = sr.Role.Name,
            ApplicationCode = sr.Role.Application?.Code,
            ResourceInstanceId = sr.ResourceInstanceId,
            GrantedAt = sr.GrantedAt,
            ExpiresAt = sr.ExpiresAt
        }).ToList()
    };
}

public record SubjectDto
{
    public Guid Id { get; init; }
    public required string ExternalId { get; init; }
    public required string Provider { get; init; }
    public SubjectType Type { get; init; }
    public string? Email { get; init; }
    public string? DisplayName { get; init; }
    public bool IsActive { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? LastSeenAt { get; init; }
}

public record SubjectDetailDto : SubjectDto
{
    public Dictionary<string, object>? Metadata { get; init; }
    public List<SubjectRoleDto> Roles { get; init; } = [];
}

public record SubjectRoleDto
{
    public required string RoleCode { get; init; }
    public required string RoleName { get; init; }
    public string? ApplicationCode { get; init; }
    public string? ResourceInstanceId { get; init; }
    public DateTimeOffset GrantedAt { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
}

public record ProvisionSubjectRequest(
    string ExternalId,
    string Provider,
    SubjectType Type = SubjectType.User,
    string? Email = null,
    string? DisplayName = null,
    Dictionary<string, object>? Metadata = null);

public record UpdateSubjectRequest(
    string? Email = null,
    string? DisplayName = null,
    bool? IsActive = null,
    Dictionary<string, object>? Metadata = null);

public record AssignRoleRequest(string RoleCode, string? ResourceInstanceId = null, DateTimeOffset? ExpiresAt = null);

public record PagedResult<T>
{
    public required List<T> Items { get; init; }
    public int Total { get; init; }
    public int Skip { get; init; }
    public int Take { get; init; }
}
