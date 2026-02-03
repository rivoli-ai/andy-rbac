using Andy.Rbac.Infrastructure.Data;
using Andy.Rbac.Models;
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
    private readonly RbacDbContext _db;
    private readonly ILogger<ApplicationsController> _logger;

    public ApplicationsController(RbacDbContext db, ILogger<ApplicationsController> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// Gets all registered applications.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(List<ApplicationDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetApplications(CancellationToken ct)
    {
        var apps = await _db.Applications
            .AsNoTracking()
            .OrderBy(a => a.Name)
            .Select(a => new ApplicationDto
            {
                Id = a.Id,
                Code = a.Code,
                Name = a.Name,
                Description = a.Description,
                ResourceTypeCount = a.ResourceTypes.Count,
                RoleCount = a.Roles.Count,
                CreatedAt = a.CreatedAt
            })
            .ToListAsync(ct);

        return Ok(apps);
    }

    /// <summary>
    /// Gets an application by ID with full details.
    /// </summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ApplicationDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetApplication(Guid id, CancellationToken ct)
    {
        var app = await _db.Applications
            .Include(a => a.ResourceTypes)
            .Include(a => a.Roles)
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == id, ct);

        if (app == null)
            return NotFound();

        return Ok(new ApplicationDetailDto
        {
            Id = app.Id,
            Code = app.Code,
            Name = app.Name,
            Description = app.Description,
            CreatedAt = app.CreatedAt,
            ResourceTypes = app.ResourceTypes.Select(rt => new ResourceTypeDto
            {
                Id = rt.Id,
                Code = rt.Code,
                Name = rt.Name,
                Description = rt.Description,
                SupportsInstances = rt.SupportsInstances
            }).ToList(),
            Roles = app.Roles.Select(r => new RoleSummaryDto
            {
                Id = r.Id,
                Code = r.Code,
                Name = r.Name,
                IsSystem = r.IsSystem
            }).ToList()
        });
    }

    /// <summary>
    /// Gets an application by code.
    /// </summary>
    [HttpGet("by-code/{code}")]
    [ProducesResponseType(typeof(ApplicationDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetApplicationByCode(string code, CancellationToken ct)
    {
        var app = await _db.Applications
            .Include(a => a.ResourceTypes)
            .Include(a => a.Roles)
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Code == code, ct);

        if (app == null)
            return NotFound();

        return Ok(new ApplicationDetailDto
        {
            Id = app.Id,
            Code = app.Code,
            Name = app.Name,
            Description = app.Description,
            CreatedAt = app.CreatedAt,
            ResourceTypes = app.ResourceTypes.Select(rt => new ResourceTypeDto
            {
                Id = rt.Id,
                Code = rt.Code,
                Name = rt.Name,
                Description = rt.Description,
                SupportsInstances = rt.SupportsInstances
            }).ToList(),
            Roles = app.Roles.Select(r => new RoleSummaryDto
            {
                Id = r.Id,
                Code = r.Code,
                Name = r.Name,
                IsSystem = r.IsSystem
            }).ToList()
        });
    }

    /// <summary>
    /// Creates a new application.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(ApplicationDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateApplication([FromBody] CreateApplicationRequest request, CancellationToken ct)
    {
        if (await _db.Applications.AnyAsync(a => a.Code == request.Code, ct))
            return BadRequest($"Application with code '{request.Code}' already exists");

        var app = new Application
        {
            Code = request.Code,
            Name = request.Name,
            Description = request.Description
        };

        _db.Applications.Add(app);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Created application {AppCode}", app.Code);

        return CreatedAtAction(nameof(GetApplication), new { id = app.Id }, new ApplicationDto
        {
            Id = app.Id,
            Code = app.Code,
            Name = app.Name,
            Description = app.Description,
            ResourceTypeCount = 0,
            RoleCount = 0,
            CreatedAt = app.CreatedAt
        });
    }

    /// <summary>
    /// Updates an application.
    /// </summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(ApplicationDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateApplication(Guid id, [FromBody] UpdateApplicationRequest request, CancellationToken ct)
    {
        var app = await _db.Applications.FindAsync([id], ct);
        if (app == null)
            return NotFound();

        app.Name = request.Name ?? app.Name;
        app.Description = request.Description ?? app.Description;

        await _db.SaveChangesAsync(ct);

        return Ok(new ApplicationDto
        {
            Id = app.Id,
            Code = app.Code,
            Name = app.Name,
            Description = app.Description,
            CreatedAt = app.CreatedAt
        });
    }

    /// <summary>
    /// Deletes an application and all associated data.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteApplication(Guid id, CancellationToken ct)
    {
        var app = await _db.Applications.FindAsync([id], ct);
        if (app == null)
            return NotFound();

        _db.Applications.Remove(app);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Deleted application {AppCode}", app.Code);

        return NoContent();
    }

    /// <summary>
    /// Adds a resource type to an application.
    /// </summary>
    [HttpPost("{id:guid}/resource-types")]
    [ProducesResponseType(typeof(ResourceTypeDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AddResourceType(Guid id, [FromBody] CreateResourceTypeRequest request, CancellationToken ct)
    {
        var app = await _db.Applications.FindAsync([id], ct);
        if (app == null)
            return NotFound();

        if (await _db.ResourceTypes.AnyAsync(rt => rt.ApplicationId == id && rt.Code == request.Code, ct))
            return BadRequest($"Resource type '{request.Code}' already exists in this application");

        var resourceType = new ResourceType
        {
            ApplicationId = id,
            Code = request.Code,
            Name = request.Name,
            Description = request.Description,
            SupportsInstances = request.SupportsInstances
        };

        _db.ResourceTypes.Add(resourceType);
        await _db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(GetApplication), new { id }, new ResourceTypeDto
        {
            Id = resourceType.Id,
            Code = resourceType.Code,
            Name = resourceType.Name,
            Description = resourceType.Description,
            SupportsInstances = resourceType.SupportsInstances
        });
    }
}

public record ApplicationDto
{
    public Guid Id { get; init; }
    public required string Code { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public int ResourceTypeCount { get; init; }
    public int RoleCount { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}

public record ApplicationDetailDto : ApplicationDto
{
    public List<ResourceTypeDto> ResourceTypes { get; init; } = [];
    public List<RoleSummaryDto> Roles { get; init; } = [];
}

public record ResourceTypeDto
{
    public Guid Id { get; init; }
    public required string Code { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public bool SupportsInstances { get; init; }
}

public record RoleSummaryDto
{
    public Guid Id { get; init; }
    public required string Code { get; init; }
    public required string Name { get; init; }
    public bool IsSystem { get; init; }
}

public record CreateApplicationRequest(string Code, string Name, string? Description = null);
public record UpdateApplicationRequest(string? Name = null, string? Description = null);
public record CreateResourceTypeRequest(string Code, string Name, string? Description = null, bool SupportsInstances = true);
