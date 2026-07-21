using Andy.Rbac.Api.Services;
using Andy.Rbac.Api.Authorization;
using Andy.Rbac.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Andy.Rbac.Api.Controllers;

/// <summary>
/// Application management endpoints.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ApplicationsController : ControllerBase
{
    private readonly IApplicationService _applicationService;
    private readonly RbacDbContext _db;

    public ApplicationsController(IApplicationService applicationService, RbacDbContext db)
    {
        _applicationService = applicationService;
        _db = db;
    }

    /// <summary>
    /// Gets all registered applications.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApplicationResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetApplications(CancellationToken ct)
    {
        var result = await _applicationService.GetAllAsync(ct);
        return Ok(result.Applications);
    }

    /// <summary>
    /// Gets an application by ID with full details.
    /// </summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ApplicationDetail), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetApplication(Guid id, CancellationToken ct)
    {
        var result = await _applicationService.GetByIdAsync(id, ct);
        if (result == null)
            return NotFound();

        return Ok(result.Application);
    }

    /// <summary>
    /// Gets an application by code.
    /// </summary>
    [HttpGet("by-code/{code}")]
    [ProducesResponseType(typeof(ApplicationDetail), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetApplicationByCode(string code, CancellationToken ct)
    {
        var result = await _applicationService.GetByCodeAsync(code, ct);
        if (result == null)
            return NotFound();

        return Ok(result.Application);
    }

    /// <summary>
    /// Creates a new application.
    /// </summary>
    [HttpPost]
    [Authorize(Policy = RbacAuthorizationPolicies.Administrator)]
    [ProducesResponseType(typeof(ApplicationDetail), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateApplication([FromBody] CreateApplicationRequest request, CancellationToken ct)
    {
        try
        {
            var result = await _applicationService.CreateAsync(request, ct);
            return CreatedAtAction(nameof(GetApplication), new { id = result.Application.Id }, result.Application);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    /// <summary>
    /// Updates an application.
    /// </summary>
    [HttpPut("{id:guid}")]
    [Authorize(Policy = RbacAuthorizationPolicies.Administrator)]
    [ProducesResponseType(typeof(ApplicationDetail), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateApplication(Guid id, [FromBody] UpdateApplicationRequest request, CancellationToken ct)
    {
        var result = await _applicationService.UpdateAsync(id, request, ct);
        if (result == null)
            return NotFound();

        return Ok(result.Application);
    }

    /// <summary>
    /// Deletes an application and all associated data.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = RbacAuthorizationPolicies.Administrator)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteApplication(Guid id, CancellationToken ct)
    {
        var deleted = await _applicationService.DeleteAsync(id, ct);
        if (!deleted)
            return NotFound();

        return NoContent();
    }

    /// <summary>
    /// Adds a resource type to an application.
    /// </summary>
    [HttpPost("{id:guid}/resource-types")]
    [Authorize(Policy = RbacAuthorizationPolicies.Administrator)]
    [ProducesResponseType(typeof(ResourceTypeSummary), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> AddResourceType(Guid id, [FromBody] CreateResourceTypeRequest request, CancellationToken ct)
    {
        try
        {
            var result = await _applicationService.AddResourceTypeAsync(id, request, ct);
            if (result == null)
                return NotFound();

            return CreatedAtAction(nameof(GetApplication), new { id }, result.ResourceType);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    /// <summary>
    /// Lists subjects that hold one or more roles in the named
    /// application — read-only admin Users view (#9).
    ///
    /// Searches email + display name (case-insensitive substring),
    /// optionally filters by role code, and returns each subject's
    /// roles scoped to this application only (not their global role
    /// list).
    ///
    /// Auth: authenticated read access at class level; all management
    /// mutations in this controller require the RBAC administrator policy.
    /// </summary>
    [HttpGet("by-code/{code}/users")]
    [ProducesResponseType(typeof(PagedResult<ApplicationUserDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ListUsersByApplicationCode(
        string code,
        [FromQuery] string? query,
        [FromQuery] string? role,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50,
        CancellationToken ct = default)
    {
        var application = await _db.Applications
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Code == code, ct);
        if (application is null) return NotFound();

        // Clamp pagination per spec (default take 50, max 200).
        skip = Math.Max(0, skip);
        take = Math.Clamp(take, 1, 200);

        // Pull all role-assignments in the app + the subject + role
        // nav properties in one EF round-trip. Grouping + search-
        // filtering happen in memory rather than via joins so the
        // EF InMemory provider (used by integration tests) keeps up
        // with the relational providers (Postgres / SQLite).
        var roleCodeFilter = string.IsNullOrWhiteSpace(role) ? null : role.Trim();

        var assignments = await _db.SubjectRoles
            .AsNoTracking()
            .Where(sr => sr.Role.ApplicationId == application.Id)
            .Include(sr => sr.Subject)
            .Include(sr => sr.Role)
            .ToListAsync(ct);

        if (roleCodeFilter is not null)
        {
            assignments = assignments
                .Where(sr => string.Equals(sr.Role.Code, roleCodeFilter, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        // Group by subject to collapse multiple role assignments per
        // person into a single row.
        var bySubject = assignments
            .Where(sr => sr.Subject is not null)
            .GroupBy(sr => sr.Subject)
            .Select(g => new
            {
                Subject = g.Key,
                RoleCodes = g.Select(sr => sr.Role.Code).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            })
            .ToList();

        if (!string.IsNullOrWhiteSpace(query))
        {
            var needle = query.Trim();
            bySubject = bySubject
                .Where(x =>
                    (x.Subject.Email is not null && x.Subject.Email.Contains(needle, StringComparison.OrdinalIgnoreCase)) ||
                    (x.Subject.DisplayName is not null && x.Subject.DisplayName.Contains(needle, StringComparison.OrdinalIgnoreCase)))
                .ToList();
        }

        var total = bySubject.Count;
        var items = bySubject
            .OrderBy(x => x.Subject.DisplayName ?? x.Subject.Email ?? x.Subject.ExternalId, StringComparer.OrdinalIgnoreCase)
            .Skip(skip)
            .Take(take)
            .Select(x => new ApplicationUserDto
            {
                UserId = x.Subject.Id,
                Email = x.Subject.Email,
                DisplayName = x.Subject.DisplayName,
                Roles = x.RoleCodes.OrderBy(c => c).ToList(),
                LastSeenAt = x.Subject.LastSeenAt,
            })
            .ToList();

        return Ok(new PagedResult<ApplicationUserDto>
        {
            Items = items,
            Total = total,
            Skip = skip,
            Take = take,
        });
    }
}

/// <summary>
/// Per-subject row in the <c>GET /api/applications/by-code/{code}/users</c>
/// response. <see cref="Roles"/> contains role codes scoped to that
/// application only.
/// </summary>
public record ApplicationUserDto
{
    public Guid UserId { get; init; }
    public string? Email { get; init; }
    public string? DisplayName { get; init; }
    public List<string> Roles { get; init; } = [];
    public DateTimeOffset? LastSeenAt { get; init; }
}
