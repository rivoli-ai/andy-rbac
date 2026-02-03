namespace Andy.Rbac.Api.Services;

/// <summary>
/// Service for managing roles.
/// </summary>
public interface IRoleService
{
    Task<RoleListResult> GetAllAsync(string? applicationCode = null, CancellationToken ct = default);
    Task<RoleDetailResult?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<RoleDetailResult?> GetByCodeAsync(string code, string? applicationCode = null, CancellationToken ct = default);
    Task<RoleDetailResult> CreateAsync(CreateRoleRequest request, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
    Task<string> AssignToSubjectAsync(string subjectExternalId, string roleCode, string? resourceInstanceId = null, CancellationToken ct = default);
    Task<string> RevokeFromSubjectAsync(string subjectExternalId, string roleCode, string? resourceInstanceId = null, CancellationToken ct = default);
    Task<string> AssignToTeamAsync(string teamCode, string roleCode, CancellationToken ct = default);
}

public record RoleDetail(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    string? ApplicationCode,
    string? ParentRoleCode,
    bool IsSystem,
    List<string> Permissions);

public record RoleListResult(List<RoleDetail> Roles);
public record RoleDetailResult(RoleDetail Role);

public record CreateRoleRequest(
    string Code,
    string Name,
    string? Description = null,
    string? ApplicationCode = null,
    string? ParentRoleCode = null);
