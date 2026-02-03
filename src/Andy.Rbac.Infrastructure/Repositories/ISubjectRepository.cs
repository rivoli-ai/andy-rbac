using Andy.Rbac.Models;

namespace Andy.Rbac.Infrastructure.Repositories;

/// <summary>
/// Repository for subject-related operations.
/// </summary>
public interface ISubjectRepository
{
    Task<Subject?> GetByExternalIdAsync(string externalId, string provider, CancellationToken ct = default);
    Task<Subject?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Subject> CreateAsync(Subject subject, CancellationToken ct = default);
    Task<Subject> UpdateAsync(Subject subject, CancellationToken ct = default);
    Task UpdateLastSeenAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Subject>> SearchAsync(string? query, int skip = 0, int take = 50, CancellationToken ct = default);
}

/// <summary>
/// Repository for role-related operations.
/// </summary>
public interface IRoleRepository
{
    Task<Role?> GetByCodeAsync(string code, Guid? applicationId = null, CancellationToken ct = default);
    Task<Role?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Role> CreateAsync(Role role, CancellationToken ct = default);
    Task<Role> UpdateAsync(Role role, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Role>> GetAllAsync(Guid? applicationId = null, CancellationToken ct = default);
    Task AssignToSubjectAsync(Guid subjectId, Guid roleId, string? resourceInstanceId = null, Guid? grantedById = null, DateTimeOffset? expiresAt = null, CancellationToken ct = default);
    Task RevokeFromSubjectAsync(Guid subjectId, Guid roleId, string? resourceInstanceId = null, CancellationToken ct = default);
}

/// <summary>
/// Repository for application-related operations.
/// </summary>
public interface IApplicationRepository
{
    Task<Application?> GetByCodeAsync(string code, CancellationToken ct = default);
    Task<Application?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Application> CreateAsync(Application application, CancellationToken ct = default);
    Task<Application> UpdateAsync(Application application, CancellationToken ct = default);
    Task<IReadOnlyList<Application>> GetAllAsync(CancellationToken ct = default);
}
