namespace Andy.Rbac.Api.Services;

/// <summary>
/// Service for managing subjects (users and service accounts).
/// </summary>
public interface ISubjectService
{
    Task<SubjectListResult> SearchAsync(string? query = null, int limit = 20, CancellationToken ct = default);
    Task<SubjectDetailResult?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<SubjectDetailResult?> GetByExternalIdAsync(string externalId, CancellationToken ct = default);
    Task<SubjectDetailResult> CreateAsync(CreateSubjectRequest request, CancellationToken ct = default);
    Task<SubjectDetailResult?> UpdateAsync(Guid id, UpdateSubjectRequest request, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
    Task<SubjectDetailResult> GetOrCreateAsync(string externalId, string provider, string? email = null, string? displayName = null, CancellationToken ct = default);
}

public record SubjectSummary(
    Guid Id,
    string ExternalId,
    string Provider,
    string? Email,
    string? DisplayName,
    bool IsActive);

public record SubjectDetail(
    Guid Id,
    string ExternalId,
    string Provider,
    string? Email,
    string? DisplayName,
    bool IsActive,
    DateTimeOffset CreatedAt,
    List<SubjectRoleInfo> Roles,
    List<SubjectTeamInfo> Teams);

public record SubjectRoleInfo(
    string RoleCode,
    string? ApplicationCode,
    string? ResourceInstanceId);

public record SubjectTeamInfo(
    string TeamCode,
    string TeamName,
    string MembershipRole);

public record SubjectListResult(List<SubjectSummary> Subjects);
public record SubjectDetailResult(SubjectDetail Subject);

public record CreateSubjectRequest(
    string ExternalId,
    string Provider,
    string? Email = null,
    string? DisplayName = null);

public record UpdateSubjectRequest(
    string? Email = null,
    string? DisplayName = null,
    bool? IsActive = null);
