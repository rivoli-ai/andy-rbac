namespace Andy.Rbac.Abstractions;

/// <summary>
/// Cache abstraction for RBAC data.
/// </summary>
public interface IRbacCache
{
    /// <summary>
    /// Gets cached permissions for a subject.
    /// </summary>
    Task<IReadOnlyList<string>?> GetPermissionsAsync(string subjectId, CancellationToken ct = default);

    /// <summary>
    /// Caches permissions for a subject.
    /// </summary>
    Task SetPermissionsAsync(string subjectId, IReadOnlyList<string> permissions, CancellationToken ct = default);

    /// <summary>
    /// Gets cached roles for a subject.
    /// </summary>
    Task<IReadOnlyList<string>?> GetRolesAsync(string subjectId, CancellationToken ct = default);

    /// <summary>
    /// Caches roles for a subject.
    /// </summary>
    Task SetRolesAsync(string subjectId, IReadOnlyList<string> roles, CancellationToken ct = default);

    /// <summary>
    /// Invalidates all cached data for a subject.
    /// </summary>
    Task InvalidateAsync(string subjectId, CancellationToken ct = default);

    /// <summary>
    /// Invalidates all cached data.
    /// </summary>
    Task InvalidateAllAsync(CancellationToken ct = default);
}
