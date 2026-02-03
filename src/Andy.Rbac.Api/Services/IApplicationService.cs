namespace Andy.Rbac.Api.Services;

/// <summary>
/// Service for managing applications.
/// </summary>
public interface IApplicationService
{
    Task<ApplicationResult> GetAllAsync(CancellationToken ct = default);
    Task<ApplicationDetailResult?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<ApplicationDetailResult?> GetByCodeAsync(string code, CancellationToken ct = default);
    Task<ApplicationDetailResult> CreateAsync(CreateApplicationRequest request, CancellationToken ct = default);
    Task<ApplicationDetailResult?> UpdateAsync(Guid id, UpdateApplicationRequest request, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
    Task<ResourceTypeResult?> AddResourceTypeAsync(Guid applicationId, CreateResourceTypeRequest request, CancellationToken ct = default);
}

// Shared DTOs used by both API and MCP
public record ApplicationSummary(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    int ResourceTypeCount,
    int RoleCount,
    DateTimeOffset CreatedAt);

public record ApplicationDetail(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    DateTimeOffset CreatedAt,
    List<ResourceTypeSummary> ResourceTypes,
    List<RoleSummary> Roles);

public record ResourceTypeSummary(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    bool SupportsInstances);

public record RoleSummary(
    Guid Id,
    string Code,
    string Name,
    bool IsSystem);

public record ApplicationResult(List<ApplicationSummary> Applications);
public record ApplicationDetailResult(ApplicationDetail Application);
public record ResourceTypeResult(ResourceTypeSummary ResourceType);

public record CreateApplicationRequest(string Code, string Name, string? Description = null);
public record UpdateApplicationRequest(string? Name = null, string? Description = null);
public record CreateResourceTypeRequest(string Code, string Name, string? Description = null, bool SupportsInstances = true);
