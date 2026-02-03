namespace Andy.Rbac.Abstractions;

/// <summary>
/// Service for resource-level authorization with support for filtering queries.
/// </summary>
public interface IResourceAuthorizationService
{
    /// <summary>
    /// Checks if a subject can perform an action on a specific resource.
    /// </summary>
    /// <typeparam name="TResource">Type of the resource.</typeparam>
    /// <param name="subjectId">The external ID of the subject.</param>
    /// <param name="resource">The resource to check access for.</param>
    /// <param name="action">The action to perform (e.g., "read", "write", "delete").</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if access is allowed.</returns>
    Task<bool> CanAccessAsync<TResource>(
        string subjectId,
        TResource resource,
        string action,
        CancellationToken ct = default) where TResource : class;

    /// <summary>
    /// Filters a queryable to only include resources the subject can access.
    /// </summary>
    /// <typeparam name="TResource">Type of the resource.</typeparam>
    /// <param name="subjectId">The external ID of the subject.</param>
    /// <param name="query">The query to filter.</param>
    /// <param name="action">The action to filter for (e.g., "read").</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Filtered queryable containing only authorized resources.</returns>
    Task<IQueryable<TResource>> FilterAuthorizedAsync<TResource>(
        string subjectId,
        IQueryable<TResource> query,
        string action,
        CancellationToken ct = default) where TResource : class;
}

/// <summary>
/// Interface that resources must implement for automatic authorization filtering.
/// </summary>
public interface IAuthorizedResource
{
    /// <summary>
    /// Gets the external ID of this resource instance for permission checking.
    /// </summary>
    string GetResourceInstanceId();

    /// <summary>
    /// Gets the resource type code (e.g., "document", "collection").
    /// </summary>
    string GetResourceTypeCode();

    /// <summary>
    /// Gets the application code this resource belongs to.
    /// </summary>
    string GetApplicationCode();

    /// <summary>
    /// Gets the owner's subject ID, if applicable.
    /// </summary>
    string? GetOwnerSubjectId();
}
