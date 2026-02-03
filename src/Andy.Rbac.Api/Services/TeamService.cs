using Andy.Rbac.Infrastructure.Data;
using Andy.Rbac.Models;
using Microsoft.EntityFrameworkCore;

namespace Andy.Rbac.Api.Services;

/// <summary>
/// Service for managing teams.
/// </summary>
public class TeamService : ITeamService
{
    private readonly RbacDbContext _db;
    private readonly ILogger<TeamService> _logger;

    public TeamService(RbacDbContext db, ILogger<TeamService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<TeamListResult> GetAllAsync(string? applicationCode = null, CancellationToken ct = default)
    {
        var query = _db.Teams
            .Include(t => t.Application)
            .Include(t => t.ParentTeam)
            .Include(t => t.Members)
            .AsNoTracking();

        if (!string.IsNullOrEmpty(applicationCode))
        {
            query = query.Where(t => t.ApplicationId == null || t.Application!.Code == applicationCode);
        }

        var teams = await query
            .OrderBy(t => t.Name)
            .Select(t => new TeamSummary(
                t.Id,
                t.Code,
                t.Name,
                t.Description,
                t.ParentTeam != null ? t.ParentTeam.Code : null,
                t.Application != null ? t.Application.Code : null,
                t.Members.Count,
                t.IsActive))
            .ToListAsync(ct);

        return new TeamListResult(teams);
    }

    public async Task<TeamDetailResult?> GetByIdAsync(Guid id, CancellationToken ct = default)
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

        return team == null ? null : MapToDetailResult(team);
    }

    public async Task<TeamDetailResult?> GetByCodeAsync(string code, CancellationToken ct = default)
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

        return team == null ? null : MapToDetailResult(team);
    }

    public async Task<TeamDetailResult> CreateAsync(CreateTeamRequest request, CancellationToken ct = default)
    {
        Guid? parentId = null;
        if (!string.IsNullOrEmpty(request.ParentTeamCode))
        {
            var parent = await _db.Teams.FirstOrDefaultAsync(t => t.Code == request.ParentTeamCode, ct);
            if (parent == null)
                throw new InvalidOperationException($"Parent team '{request.ParentTeamCode}' not found");
            parentId = parent.Id;
        }

        Guid? appId = null;
        if (!string.IsNullOrEmpty(request.ApplicationCode))
        {
            var app = await _db.Applications.FirstOrDefaultAsync(a => a.Code == request.ApplicationCode, ct);
            if (app == null)
                throw new InvalidOperationException($"Application '{request.ApplicationCode}' not found");
            appId = app.Id;
        }

        if (await _db.Teams.AnyAsync(t => t.Code == request.Code, ct))
            throw new InvalidOperationException($"Team with code '{request.Code}' already exists");

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

        return new TeamDetailResult(new TeamDetail(
            team.Id,
            team.Code,
            team.Name,
            team.Description,
            request.ParentTeamCode,
            request.ApplicationCode,
            true,
            [],
            []));
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var team = await _db.Teams.FindAsync([id], ct);
        if (team == null)
            return false;

        _db.Teams.Remove(team);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Deleted team {TeamCode}", team.Code);

        return true;
    }

    public async Task<string> AddMemberAsync(string teamCode, string subjectExternalId, TeamMembershipRole role = TeamMembershipRole.Member, CancellationToken ct = default)
    {
        var team = await _db.Teams.FirstOrDefaultAsync(t => t.Code == teamCode, ct);
        if (team == null)
            return $"Error: Team '{teamCode}' not found";

        var subject = await _db.Subjects.FirstOrDefaultAsync(s => s.ExternalId == subjectExternalId, ct);
        if (subject == null)
            return $"Error: Subject '{subjectExternalId}' not found";

        if (await _db.TeamMembers.AnyAsync(tm => tm.TeamId == team.Id && tm.SubjectId == subject.Id, ct))
            return $"User is already a member of team '{teamCode}'";

        _db.TeamMembers.Add(new TeamMember
        {
            TeamId = team.Id,
            SubjectId = subject.Id,
            MembershipRole = role
        });

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Added {SubjectId} to team {TeamCode}", subjectExternalId, teamCode);

        return $"Successfully added user '{subjectExternalId}' to team '{teamCode}' as {role}";
    }

    public async Task<string> RemoveMemberAsync(string teamCode, string subjectExternalId, CancellationToken ct = default)
    {
        var team = await _db.Teams.FirstOrDefaultAsync(t => t.Code == teamCode, ct);
        if (team == null)
            return $"Error: Team '{teamCode}' not found";

        var subject = await _db.Subjects.FirstOrDefaultAsync(s => s.ExternalId == subjectExternalId, ct);
        if (subject == null)
            return $"Error: Subject '{subjectExternalId}' not found";

        var membership = await _db.TeamMembers
            .FirstOrDefaultAsync(tm => tm.TeamId == team.Id && tm.SubjectId == subject.Id, ct);

        if (membership == null)
            return $"User is not a member of team '{teamCode}'";

        _db.TeamMembers.Remove(membership);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Removed {SubjectId} from team {TeamCode}", subjectExternalId, teamCode);

        return $"Successfully removed user '{subjectExternalId}' from team '{teamCode}'";
    }

    private static TeamDetailResult MapToDetailResult(Team team)
    {
        return new TeamDetailResult(new TeamDetail(
            team.Id,
            team.Code,
            team.Name,
            team.Description,
            team.ParentTeam?.Code,
            team.Application?.Code,
            team.IsActive,
            team.Members.Select(m => new TeamMemberSummary(
                m.SubjectId,
                m.Subject.ExternalId,
                m.Subject.DisplayName,
                m.MembershipRole.ToString())).ToList(),
            team.TeamRoles.Select(tr => tr.Role.Code).ToList()));
    }
}
