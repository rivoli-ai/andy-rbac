using Andy.Rbac.Infrastructure.Data;
using Andy.Rbac.Models;
using Microsoft.EntityFrameworkCore;

namespace Andy.Rbac.Api.Services;

public sealed class ResourceInstanceService : IResourceInstanceService
{
    private readonly RbacDbContext _db;

    public ResourceInstanceService(RbacDbContext db) => _db = db;

    public async Task<ResourceInstanceMutationResult> RegisterAsync(
        string applicationCode, string resourceTypeCode, string externalId,
        string? ownerExternalId, string? ownerProvider, string? displayName,
        Dictionary<string, object>? metadata, CancellationToken ct = default)
    {
        var resourceType = await ResolveResourceTypeAsync(applicationCode, resourceTypeCode, ct);
        if (resourceType is null)
            return ResourceInstanceMutationResult.Missing("Resource type not found in application");
        if (!resourceType.SupportsInstances)
            return ResourceInstanceMutationResult.Invalid("Resource type does not support instances");

        Subject? owner = null;
        if (!string.IsNullOrWhiteSpace(ownerExternalId))
        {
            var resolution = await SubjectResolver.ResolveAsync(
                _db, ownerExternalId, ownerProvider, tracking: true, ct);
            if (resolution.IsAmbiguous)
                return ResourceInstanceMutationResult.Invalid("Owner subject provider is required for an ambiguous external ID");
            owner = resolution.Subject;
            if (owner is null)
                return ResourceInstanceMutationResult.Missing("Owner subject not found");
        }

        var instance = await _db.ResourceInstances.FirstOrDefaultAsync(
            ri => ri.ResourceTypeId == resourceType.Id && ri.ExternalId == externalId, ct);
        if (instance is null)
        {
            instance = new ResourceInstance
            {
                ResourceTypeId = resourceType.Id,
                ExternalId = externalId,
                OwnerSubjectId = owner?.Id,
                DisplayName = displayName,
                Metadata = metadata
            };
            _db.ResourceInstances.Add(instance);
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(ownerExternalId))
                instance.OwnerSubjectId = owner!.Id;
            instance.DisplayName = displayName ?? instance.DisplayName;
            instance.Metadata = metadata ?? instance.Metadata;
        }

        await _db.SaveChangesAsync(ct);
        return ResourceInstanceMutationResult.Ok(instance.Id);
    }

    public async Task<ResourceInstanceMutationResult> RemoveAsync(
        string applicationCode, string resourceTypeCode, string externalId,
        CancellationToken ct = default)
    {
        var instance = await ResolveInstanceAsync(applicationCode, resourceTypeCode, externalId, ct);
        if (instance is null)
            return ResourceInstanceMutationResult.Missing("Resource instance not found");

        _db.ResourceInstances.Remove(instance);
        await _db.SaveChangesAsync(ct);
        return ResourceInstanceMutationResult.Ok();
    }

    public async Task<ResourceInstanceMutationResult> GrantAsync(
        string applicationCode, string resourceTypeCode, string externalId,
        string subjectExternalId, string? subjectProvider, string action,
        DateTimeOffset? expiresAt, CancellationToken ct = default)
    {
        var instance = await ResolveInstanceAsync(applicationCode, resourceTypeCode, externalId, ct);
        if (instance is null)
            return ResourceInstanceMutationResult.Missing("Resource instance not found");

        var subjectResolution = await SubjectResolver.ResolveAsync(
            _db, subjectExternalId, subjectProvider, tracking: true, ct);
        if (subjectResolution.IsAmbiguous)
            return ResourceInstanceMutationResult.Invalid("Subject provider is required for an ambiguous external ID");
        if (subjectResolution.Subject is null)
            return ResourceInstanceMutationResult.Missing("Subject not found");

        var permission = await _db.Permissions.FirstOrDefaultAsync(p =>
            p.ResourceTypeId == instance.ResourceTypeId && p.Action.Code == action, ct);
        if (permission is null)
            return ResourceInstanceMutationResult.Missing("Permission action not found for resource type");

        var existing = await _db.InstancePermissions.FirstOrDefaultAsync(ip =>
            ip.ResourceInstanceId == instance.Id &&
            ip.SubjectId == subjectResolution.Subject.Id &&
            ip.PermissionId == permission.Id, ct);

        if (existing is null)
        {
            existing = new InstancePermission
            {
                ResourceInstanceId = instance.Id,
                SubjectId = subjectResolution.Subject.Id,
                PermissionId = permission.Id,
                ExpiresAt = expiresAt
            };
            _db.InstancePermissions.Add(existing);
        }
        else
        {
            existing.ExpiresAt = expiresAt;
        }

        await _db.SaveChangesAsync(ct);
        return ResourceInstanceMutationResult.Ok(existing.Id);
    }

    public async Task<ResourceInstanceMutationResult> RevokeAsync(
        string applicationCode, string resourceTypeCode, string externalId,
        string subjectExternalId, string? subjectProvider, string action,
        CancellationToken ct = default)
    {
        var instance = await ResolveInstanceAsync(applicationCode, resourceTypeCode, externalId, ct);
        if (instance is null)
            return ResourceInstanceMutationResult.Missing("Resource instance not found");

        var subjectResolution = await SubjectResolver.ResolveAsync(
            _db, subjectExternalId, subjectProvider, tracking: false, ct);
        if (subjectResolution.IsAmbiguous)
            return ResourceInstanceMutationResult.Invalid("Subject provider is required for an ambiguous external ID");
        if (subjectResolution.Subject is null)
            return ResourceInstanceMutationResult.Missing("Subject not found");

        var grant = await _db.InstancePermissions.FirstOrDefaultAsync(ip =>
            ip.ResourceInstanceId == instance.Id &&
            ip.SubjectId == subjectResolution.Subject.Id &&
            ip.Permission.ResourceTypeId == instance.ResourceTypeId &&
            ip.Permission.Action.Code == action, ct);
        if (grant is null)
            return ResourceInstanceMutationResult.Missing("Instance permission grant not found");

        _db.InstancePermissions.Remove(grant);
        await _db.SaveChangesAsync(ct);
        return ResourceInstanceMutationResult.Ok();
    }

    private Task<ResourceType?> ResolveResourceTypeAsync(
        string applicationCode, string resourceTypeCode, CancellationToken ct) =>
        _db.ResourceTypes.FirstOrDefaultAsync(rt =>
            rt.Code == resourceTypeCode && rt.Application.Code == applicationCode, ct);

    private Task<ResourceInstance?> ResolveInstanceAsync(
        string applicationCode, string resourceTypeCode, string externalId, CancellationToken ct) =>
        _db.ResourceInstances.FirstOrDefaultAsync(ri =>
            ri.ExternalId == externalId &&
            ri.ResourceType.Code == resourceTypeCode &&
            ri.ResourceType.Application.Code == applicationCode, ct);
}
