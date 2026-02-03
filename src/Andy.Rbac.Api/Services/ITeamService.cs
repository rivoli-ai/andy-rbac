using Andy.Rbac.Models;

namespace Andy.Rbac.Api.Services;

/// <summary>
/// Service for managing teams.
/// </summary>
public interface ITeamService
{
    Task<TeamListResult> GetAllAsync(string? applicationCode = null, CancellationToken ct = default);
    Task<TeamDetailResult?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<TeamDetailResult?> GetByCodeAsync(string code, CancellationToken ct = default);
    Task<TeamDetailResult> CreateAsync(CreateTeamRequest request, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
    Task<string> AddMemberAsync(string teamCode, string subjectExternalId, TeamMembershipRole role = TeamMembershipRole.Member, CancellationToken ct = default);
    Task<string> RemoveMemberAsync(string teamCode, string subjectExternalId, CancellationToken ct = default);
}

public record TeamSummary(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    string? ParentTeamCode,
    string? ApplicationCode,
    int MemberCount,
    bool IsActive);

public record TeamDetail(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    string? ParentTeamCode,
    string? ApplicationCode,
    bool IsActive,
    List<TeamMemberSummary> Members,
    List<string> Roles);

public record TeamMemberSummary(
    Guid SubjectId,
    string ExternalId,
    string? DisplayName,
    string MembershipRole);

public record TeamListResult(List<TeamSummary> Teams);
public record TeamDetailResult(TeamDetail Team);

public record CreateTeamRequest(
    string Code,
    string Name,
    string? Description = null,
    string? ParentTeamCode = null,
    string? ApplicationCode = null);
