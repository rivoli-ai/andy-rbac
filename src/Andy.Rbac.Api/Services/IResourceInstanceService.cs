namespace Andy.Rbac.Api.Services;

public interface IResourceInstanceService
{
    Task<ResourceInstanceMutationResult> RegisterAsync(
        string applicationCode, string resourceTypeCode, string externalId,
        string? ownerExternalId, string? ownerProvider, string? displayName,
        Dictionary<string, object>? metadata, CancellationToken ct = default);

    /// <remarks>
    /// <c>revokedByPrincipal</c> is the Subject.ExternalId of the caller,
    /// recorded on the <c>grant.revoked</c> events staged for the instance
    /// permissions this removal cascades away. Null for automated callers.
    /// </remarks>
    Task<ResourceInstanceMutationResult> RemoveAsync(
        string applicationCode, string resourceTypeCode, string externalId,
        string? revokedByPrincipal = null, CancellationToken ct = default);

    Task<ResourceInstanceMutationResult> GrantAsync(
        string applicationCode, string resourceTypeCode, string externalId,
        string subjectExternalId, string? subjectProvider, string action,
        DateTimeOffset? expiresAt, CancellationToken ct = default);

    /// <remarks>
    /// <c>revokedByPrincipal</c> is the Subject.ExternalId of the caller,
    /// recorded on the staged <c>grant.revoked</c> event. Null for automated
    /// callers.
    /// </remarks>
    Task<ResourceInstanceMutationResult> RevokeAsync(
        string applicationCode, string resourceTypeCode, string externalId,
        string subjectExternalId, string? subjectProvider, string action,
        string? revokedByPrincipal = null, CancellationToken ct = default);
}

public record ResourceInstanceMutationResult(bool Success, bool NotFound, string? Error, Guid? Id = null)
{
    public static ResourceInstanceMutationResult Missing(string error) => new(false, true, error);
    public static ResourceInstanceMutationResult Invalid(string error) => new(false, false, error);
    public static ResourceInstanceMutationResult Ok(Guid? id = null) => new(true, false, null, id);
}
