namespace Andy.Rbac.Abstractions;

/// <summary>
/// Cache abstraction for RBAC data.
///
/// Issue #47: cache entries are keyed by the full lookup tuple
/// <c>(subjectId, applicationCode, groups)</c>. Implementations must NOT
/// share an entry across different <c>applicationCode</c> or different
/// <c>groups</c> sets — otherwise a hit caches the union of two evaluations
/// and returns the wrong result for the next caller.
/// </summary>
public interface IRbacCache
{
    /// <summary>
    /// Gets cached permissions for a subject + applicationCode + groups tuple.
    /// </summary>
    Task<IReadOnlyList<string>?> GetPermissionsAsync(
        string subjectId,
        string? applicationCode = null,
        IEnumerable<string>? groups = null,
        CancellationToken ct = default);

    /// <summary>
    /// Caches permissions for a subject + applicationCode + groups tuple.
    /// </summary>
    Task SetPermissionsAsync(
        string subjectId,
        IReadOnlyList<string> permissions,
        string? applicationCode = null,
        IEnumerable<string>? groups = null,
        CancellationToken ct = default);

    /// <summary>
    /// Gets cached roles for a subject + applicationCode + groups tuple.
    /// </summary>
    Task<IReadOnlyList<string>?> GetRolesAsync(
        string subjectId,
        string? applicationCode = null,
        IEnumerable<string>? groups = null,
        CancellationToken ct = default);

    /// <summary>
    /// Caches roles for a subject + applicationCode + groups tuple.
    /// </summary>
    Task SetRolesAsync(
        string subjectId,
        IReadOnlyList<string> roles,
        string? applicationCode = null,
        IEnumerable<string>? groups = null,
        CancellationToken ct = default);

    /// <summary>
    /// Invalidates all cached data for a subject across all applicationCode /
    /// groups tuples.
    /// </summary>
    Task InvalidateAsync(string subjectId, CancellationToken ct = default);

    /// <summary>
    /// Invalidates ALL cached data — every subject, every applicationCode,
    /// every groups tuple.
    /// </summary>
    Task InvalidateAllAsync(CancellationToken ct = default);
}
