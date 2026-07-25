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
    /// <remarks>
    /// <c>subjectProvider</c> disambiguates an external ID that exists under
    /// more than one provider — the database key is (Provider, ExternalId).
    /// Without it these operations could only report the ambiguity, never
    /// resolve it, so such a subject could not be added to a team at all.
    /// </remarks>
    Task<MutationResult> AddMemberAsync(string teamCode, string subjectExternalId, TeamMembershipRole role = TeamMembershipRole.Member, string? subjectProvider = null, CancellationToken ct = default);

    /// <remarks>See <see cref="AddMemberAsync"/> for <c>subjectProvider</c>.</remarks>
    Task<MutationResult> RemoveMemberAsync(string teamCode, string subjectExternalId, string? subjectProvider = null, CancellationToken ct = default);
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
