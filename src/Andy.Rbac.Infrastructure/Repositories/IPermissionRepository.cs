using Andy.Rbac.Models;

namespace Andy.Rbac.Infrastructure.Repositories;

/// <summary>
/// Repository for permission-related queries.
/// </summary>
public interface IPermissionRepository
{
    /// <summary>
    /// Gets all permission codes for a subject, including inherited permissions from roles.
    /// </summary>
    Task<IReadOnlyList<string>> GetPermissionsForSubjectAsync(
        Guid subjectId,
        string? applicationCode = null,
        CancellationToken ct = default);

    /// <summary>
    /// Gets all role codes for a subject.
    /// </summary>
    Task<IReadOnlyList<string>> GetRolesForSubjectAsync(
        Guid subjectId,
        string? applicationCode = null,
        CancellationToken ct = default);

    /// <summary>
    /// Checks if a subject has a specific permission on a resource instance.
    /// </summary>
    Task<bool> HasPermissionAsync(
        Guid subjectId,
        string permissionCode,
        string? resourceInstanceId = null,
        CancellationToken ct = default);

    /// <summary>
    /// Gets instance permissions for a subject on a specific resource.
    /// </summary>
    Task<IReadOnlyList<Permission>> GetInstancePermissionsAsync(
        Guid subjectId,
        Guid resourceInstanceId,
        CancellationToken ct = default);
}
