using Andy.Rbac.Infrastructure.Data;
using Andy.Rbac.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Andy.Rbac.Api.Controllers;

/// <summary>
/// Team/organization management endpoints.
/// </summary>
[ApiController]
[Route("api/[controller]")]
// Note: Auth temporarily disabled for development/testing
// [Authorize]
public class TeamsController : ControllerBase
{
    private readonly RbacDbContext _db;
    private readonly ILogger<TeamsController> _logger;

    public TeamsController(RbacDbContext db, ILogger<TeamsController> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// Gets all teams.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(List<TeamDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTeams([FromQuery] string? applicationCode, CancellationToken ct)
    {
        var query = _db.Teams
            .Include(t => t.Application)
            .Include(t => t.ParentTeam)
            .AsNoTracking();

        if (!string.IsNullOrEmpty(applicationCode))
        {
            query = query.Where(t => t.ApplicationId == null || t.Application!.Code == applicationCode);
        }

        var teams = await query
            .OrderBy(t => t.Name)
            .Select(t => new TeamDto
            {
                Id = t.Id,
                Code = t.Code,
                Name = t.Name,
                Description = t.Description,
                ParentTeamCode = t.ParentTeam != null ? t.ParentTeam.Code : null,
                ApplicationCode = t.Application != null ? t.Application.Code : null,
                MemberCount = t.Members.Count,
                IsActive = t.IsActive,
                CreatedAt = t.CreatedAt
            })
            .ToListAsync(ct);

        return Ok(teams);
    }

    /// <summary>
    /// Gets a team by ID with full details.
    /// </summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(TeamDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetTeam(Guid id, CancellationToken ct)
    {
        var team = await _db.Teams
            .Include(t => t.Application)
            .Include(t => t.ParentTeam)
            .Include(t => t.Members)
            .ThenInclude(m => m.Subject)
            .Include(t => t.TeamRoles)
            .ThenInclude(tr => tr.Role)
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == id, ct);

        if (team == null)
            return NotFound();

        return Ok(MapToDetailDto(team));
    }

    /// <summary>
    /// Gets a team by code.
    /// </summary>
    [HttpGet("by-code/{code}")]
    [ProducesResponseType(typeof(TeamDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetTeamByCode(string code, CancellationToken ct)
    {
        var team = await _db.Teams
            .Include(t => t.Application)
            .Include(t => t.ParentTeam)
            .Include(t => t.Members)
            .ThenInclude(m => m.Subject)
            .Include(t => t.TeamRoles)
            .ThenInclude(tr => tr.Role)
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Code == code, ct);

        if (team == null)
            return NotFound();

        return Ok(MapToDetailDto(team));
    }

    /// <summary>
    /// Creates a new team.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(TeamDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateTeam([FromBody] CreateTeamRequest request, CancellationToken ct)
    {
        if (await _db.Teams.AnyAsync(t => t.Code == request.Code, ct))
            return BadRequest($"Team with code '{request.Code}' already exists");

        Guid? parentId = null;
        if (!string.IsNullOrEmpty(request.ParentTeamCode))
        {
            var parent = await _db.Teams.FirstOrDefaultAsync(t => t.Code == request.ParentTeamCode, ct);
            if (parent == null)
                return BadRequest($"Parent team '{request.ParentTeamCode}' not found");
            parentId = parent.Id;
        }

        Guid? appId = null;
        if (!string.IsNullOrEmpty(request.ApplicationCode))
        {
            var app = await _db.Applications.FirstOrDefaultAsync(a => a.Code == request.ApplicationCode, ct);
            if (app == null)
                return BadRequest($"Application '{request.ApplicationCode}' not found");
            appId = app.Id;
        }

        var team = new Team
        {
            Code = request.Code,
            Name = request.Name,
            Description = request.Description,
            ParentTeamId = parentId,
            ApplicationId = appId
        };

        _db.Teams.Add(team);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Created team {TeamCode}", team.Code);

        return CreatedAtAction(nameof(GetTeam), new { id = team.Id }, new TeamDto
        {
            Id = team.Id,
            Code = team.Code,
            Name = team.Name,
            Description = team.Description,
            ParentTeamCode = request.ParentTeamCode,
            ApplicationCode = request.ApplicationCode,
            MemberCount = 0,
            IsActive = true,
            CreatedAt = team.CreatedAt
        });
    }

    /// <summary>
    /// Updates a team.
    /// </summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(TeamDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateTeam(Guid id, [FromBody] UpdateTeamRequest request, CancellationToken ct)
    {
        var team = await _db.Teams.FindAsync([id], ct);
        if (team == null)
            return NotFound();

        if (request.Name != null) team.Name = request.Name;
        if (request.Description != null) team.Description = request.Description;
        if (request.IsActive.HasValue) team.IsActive = request.IsActive.Value;
        team.UpdatedAt = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync(ct);

        return Ok(new TeamDto
        {
            Id = team.Id,
            Code = team.Code,
            Name = team.Name,
            Description = team.Description,
            IsActive = team.IsActive,
            CreatedAt = team.CreatedAt
        });
    }

    /// <summary>
    /// Adds a member to a team.
    /// </summary>
    [HttpPost("{id:guid}/members")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> AddMember(Guid id, [FromBody] AddTeamMemberRequest request, CancellationToken ct)
    {
        var team = await _db.Teams.FindAsync([id], ct);
        if (team == null)
            return NotFound("Team not found");

        var subject = await _db.Subjects
            .FirstOrDefaultAsync(s => s.Provider == request.SubjectProvider && s.ExternalId == request.SubjectExternalId, ct);
        if (subject == null)
            return NotFound("Subject not found");

        if (await _db.TeamMembers.AnyAsync(tm => tm.TeamId == id && tm.SubjectId == subject.Id, ct))
            return BadRequest("Subject is already a member of this team");

        var member = new TeamMember
        {
            TeamId = id,
            SubjectId = subject.Id,
            MembershipRole = request.MembershipRole
        };

        _db.TeamMembers.Add(member);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Added {SubjectId} to team {TeamCode}", subject.ExternalId, team.Code);

        return Created("", new { TeamId = id, SubjectId = subject.Id });
    }

    /// <summary>
    /// Removes a member from a team.
    /// </summary>
    [HttpDelete("{id:guid}/members/{subjectId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RemoveMember(Guid id, Guid subjectId, CancellationToken ct)
    {
        var member = await _db.TeamMembers
            .FirstOrDefaultAsync(tm => tm.TeamId == id && tm.SubjectId == subjectId, ct);

        if (member == null)
            return NotFound();

        _db.TeamMembers.Remove(member);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Removed subject {SubjectId} from team {TeamId}", subjectId, id);

        return NoContent();
    }

    /// <summary>
    /// Assigns a role to a team (all members inherit this role).
    /// </summary>
    [HttpPost("{id:guid}/roles")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AssignTeamRole(Guid id, [FromBody] AssignTeamRoleRequest request, CancellationToken ct)
    {
        var team = await _db.Teams.FindAsync([id], ct);
        if (team == null)
            return NotFound("Team not found");

        var role = await _db.Roles.FirstOrDefaultAsync(r => r.Code == request.RoleCode, ct);
        if (role == null)
            return NotFound($"Role '{request.RoleCode}' not found");

        if (await _db.TeamRoles.AnyAsync(tr => tr.TeamId == id && tr.RoleId == role.Id && tr.ResourceInstanceId == request.ResourceInstanceId, ct))
            return BadRequest("Role already assigned to team");

        var teamRole = new TeamRole
        {
            TeamId = id,
            RoleId = role.Id,
            ResourceInstanceId = request.ResourceInstanceId,
            ExpiresAt = request.ExpiresAt
        };

        _db.TeamRoles.Add(teamRole);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Assigned role {RoleCode} to team {TeamCode}", request.RoleCode, team.Code);

        return Created("", new { TeamId = id, RoleCode = request.RoleCode });
    }

    /// <summary>
    /// Revokes a role from a team.
    /// </summary>
    [HttpDelete("{id:guid}/roles/{roleCode}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RevokeTeamRole(Guid id, string roleCode, [FromQuery] string? resourceInstanceId, CancellationToken ct)
    {
        var role = await _db.Roles.FirstOrDefaultAsync(r => r.Code == roleCode, ct);
        if (role == null)
            return NotFound($"Role '{roleCode}' not found");

        var teamRole = await _db.TeamRoles
            .FirstOrDefaultAsync(tr => tr.TeamId == id && tr.RoleId == role.Id && tr.ResourceInstanceId == resourceInstanceId, ct);

        if (teamRole == null)
            return NotFound();

        _db.TeamRoles.Remove(teamRole);
        await _db.SaveChangesAsync(ct);

        return NoContent();
    }

    private static TeamDetailDto MapToDetailDto(Team t) => new()
    {
        Id = t.Id,
        Code = t.Code,
        Name = t.Name,
        Description = t.Description,
        ParentTeamCode = t.ParentTeam?.Code,
        ApplicationCode = t.Application?.Code,
        MemberCount = t.Members.Count,
        IsActive = t.IsActive,
        CreatedAt = t.CreatedAt,
        Members = t.Members.Select(m => new TeamMemberDto
        {
            SubjectId = m.SubjectId,
            SubjectExternalId = m.Subject.ExternalId,
            SubjectDisplayName = m.Subject.DisplayName ?? m.Subject.Email ?? m.Subject.ExternalId,
            MembershipRole = m.MembershipRole,
            AddedAt = m.AddedAt
        }).ToList(),
        Roles = t.TeamRoles.Select(tr => new TeamRoleDto
        {
            RoleCode = tr.Role.Code,
            RoleName = tr.Role.Name,
            ResourceInstanceId = tr.ResourceInstanceId,
            GrantedAt = tr.GrantedAt,
            ExpiresAt = tr.ExpiresAt
        }).ToList()
    };
}

public record TeamDto
{
    public Guid Id { get; init; }
    public required string Code { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public string? ParentTeamCode { get; init; }
    public string? ApplicationCode { get; init; }
    public int MemberCount { get; init; }
    public bool IsActive { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}

public record TeamDetailDto : TeamDto
{
    public List<TeamMemberDto> Members { get; init; } = [];
    public List<TeamRoleDto> Roles { get; init; } = [];
}

public record TeamMemberDto
{
    public Guid SubjectId { get; init; }
    public required string SubjectExternalId { get; init; }
    public required string SubjectDisplayName { get; init; }
    public TeamMembershipRole MembershipRole { get; init; }
    public DateTimeOffset AddedAt { get; init; }
}

public record TeamRoleDto
{
    public required string RoleCode { get; init; }
    public required string RoleName { get; init; }
    public string? ResourceInstanceId { get; init; }
    public DateTimeOffset GrantedAt { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
}

public record CreateTeamRequest(string Code, string Name, string? Description = null, string? ParentTeamCode = null, string? ApplicationCode = null);
public record UpdateTeamRequest(string? Name = null, string? Description = null, bool? IsActive = null);
public record AddTeamMemberRequest(string SubjectExternalId, string SubjectProvider, TeamMembershipRole MembershipRole = TeamMembershipRole.Member);
public record AssignTeamRoleRequest(string RoleCode, string? ResourceInstanceId = null, DateTimeOffset? ExpiresAt = null);
