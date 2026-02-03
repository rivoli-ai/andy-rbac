using Andy.Rbac.Infrastructure.Data;
using Andy.Rbac.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Andy.Rbac.Api.Controllers;

/// <summary>
/// Role management endpoints.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class RolesController : ControllerBase
{
    private readonly RbacDbContext _db;
    private readonly ILogger<RolesController> _logger;

    public RolesController(RbacDbContext db, ILogger<RolesController> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// Gets all roles, optionally filtered by application.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(List<RoleDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetRoles([FromQuery] string? applicationCode, CancellationToken ct)
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
            .Select(r => new RoleDto
            {
                Id = r.Id,
                Code = r.Code,
                Name = r.Name,
                Description = r.Description,
                ApplicationCode = r.Application != null ? r.Application.Code : null,
                ParentRoleCode = r.ParentRole != null ? r.ParentRole.Code : null,
                IsSystem = r.IsSystem
            })
            .ToListAsync(ct);

        return Ok(roles);
    }

    /// <summary>
    /// Gets a role by ID.
    /// </summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(RoleDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetRole(Guid id, CancellationToken ct)
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
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == id, ct);

        if (role == null)
            return NotFound();

        return Ok(new RoleDetailDto
        {
            Id = role.Id,
            Code = role.Code,
            Name = role.Name,
            Description = role.Description,
            ApplicationCode = role.Application?.Code,
            ParentRoleCode = role.ParentRole?.Code,
            IsSystem = role.IsSystem,
            Permissions = role.RolePermissions.Select(rp =>
                $"{rp.Permission.ResourceType.Application?.Code}:{rp.Permission.ResourceType.Code}:{rp.Permission.Action.Code}").ToList()
        });
    }

    /// <summary>
    /// Creates a new role.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(RoleDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateRole([FromBody] CreateRoleRequest request, CancellationToken ct)
    {
        Guid? applicationId = null;
        if (!string.IsNullOrEmpty(request.ApplicationCode))
        {
            var app = await _db.Applications.FirstOrDefaultAsync(a => a.Code == request.ApplicationCode, ct);
            if (app == null)
                return BadRequest($"Application '{request.ApplicationCode}' not found");
            applicationId = app.Id;
        }

        Guid? parentRoleId = null;
        if (!string.IsNullOrEmpty(request.ParentRoleCode))
        {
            var parent = await _db.Roles.FirstOrDefaultAsync(r => r.Code == request.ParentRoleCode, ct);
            if (parent == null)
                return BadRequest($"Parent role '{request.ParentRoleCode}' not found");
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

        return CreatedAtAction(nameof(GetRole), new { id = role.Id }, new RoleDto
        {
            Id = role.Id,
            Code = role.Code,
            Name = role.Name,
            Description = role.Description,
            ApplicationCode = request.ApplicationCode,
            ParentRoleCode = request.ParentRoleCode,
            IsSystem = role.IsSystem
        });
    }

    /// <summary>
    /// Deletes a role.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> DeleteRole(Guid id, CancellationToken ct)
    {
        var role = await _db.Roles.FindAsync([id], ct);
        if (role == null)
            return NotFound();

        if (role.IsSystem)
            return BadRequest("Cannot delete system roles");

        _db.Roles.Remove(role);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Deleted role {RoleCode}", role.Code);

        return NoContent();
    }
}

public record RoleDto
{
    public Guid Id { get; init; }
    public required string Code { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public string? ApplicationCode { get; init; }
    public string? ParentRoleCode { get; init; }
    public bool IsSystem { get; init; }
}

public record RoleDetailDto : RoleDto
{
    public List<string> Permissions { get; init; } = [];
}

public record CreateRoleRequest(string Code, string Name, string? Description = null, string? ApplicationCode = null, string? ParentRoleCode = null);
