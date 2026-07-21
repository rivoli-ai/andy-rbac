namespace Andy.Rbac.Api.Services;

public interface IResourceInstanceService
{
    Task<ResourceInstanceMutationResult> RegisterAsync(
        string applicationCode, string resourceTypeCode, string externalId,
        string? ownerExternalId, string? ownerProvider, string? displayName,
        Dictionary<string, object>? metadata, CancellationToken ct = default);

    Task<ResourceInstanceMutationResult> RemoveAsync(
        string applicationCode, string resourceTypeCode, string externalId,
        CancellationToken ct = default);

    Task<ResourceInstanceMutationResult> GrantAsync(
        string applicationCode, string resourceTypeCode, string externalId,
        string subjectExternalId, string? subjectProvider, string action,
        DateTimeOffset? expiresAt, CancellationToken ct = default);

    Task<ResourceInstanceMutationResult> RevokeAsync(
        string applicationCode, string resourceTypeCode, string externalId,
        string subjectExternalId, string? subjectProvider, string action,
        CancellationToken ct = default);
}

public record ResourceInstanceMutationResult(bool Success, bool NotFound, string? Error, Guid? Id = null)
{
    public static ResourceInstanceMutationResult Missing(string error) => new(false, true, error);
    public static ResourceInstanceMutationResult Invalid(string error) => new(false, false, error);
    public static ResourceInstanceMutationResult Ok(Guid? id = null) => new(true, false, null, id);
}
