using Andy.Rbac.Infrastructure.Data;
using Andy.Rbac.Models;
using Microsoft.EntityFrameworkCore;

namespace Andy.Rbac.Api.Services;

/// <summary>
/// Service for managing applications.
/// </summary>
public class ApplicationService : IApplicationService
{
    private readonly RbacDbContext _db;
    private readonly ILogger<ApplicationService> _logger;

    public ApplicationService(RbacDbContext db, ILogger<ApplicationService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<ApplicationResult> GetAllAsync(CancellationToken ct = default)
    {
        var apps = await _db.Applications
            .AsNoTracking()
            .OrderBy(a => a.Name)
            .Select(a => new ApplicationSummary(
                a.Id,
                a.Code,
                a.Name,
                a.Description,
                a.ResourceTypes.Count,
                a.Roles.Count,
                a.CreatedAt))
            .ToListAsync(ct);

        return new ApplicationResult(apps);
    }

    public async Task<ApplicationDetailResult?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var app = await _db.Applications
            .Include(a => a.ResourceTypes)
            .Include(a => a.Roles)
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == id, ct);

        return app == null ? null : MapToDetailResult(app);
    }

    public async Task<ApplicationDetailResult?> GetByCodeAsync(string code, CancellationToken ct = default)
    {
        var app = await _db.Applications
            .Include(a => a.ResourceTypes)
            .Include(a => a.Roles)
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Code == code, ct);

        return app == null ? null : MapToDetailResult(app);
    }

    public async Task<ApplicationDetailResult> CreateAsync(CreateApplicationRequest request, CancellationToken ct = default)
    {
        if (await _db.Applications.AnyAsync(a => a.Code == request.Code, ct))
            throw new InvalidOperationException($"Application with code '{request.Code}' already exists");

        var app = new Application
        {
            Code = request.Code,
            Name = request.Name,
            Description = request.Description
        };

        _db.Applications.Add(app);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Created application {AppCode}", app.Code);

        return new ApplicationDetailResult(new ApplicationDetail(
            app.Id,
            app.Code,
            app.Name,
            app.Description,
            app.CreatedAt,
            [],
            []));
    }

    public async Task<ApplicationDetailResult?> UpdateAsync(Guid id, UpdateApplicationRequest request, CancellationToken ct = default)
    {
        var app = await _db.Applications
            .Include(a => a.ResourceTypes)
            .Include(a => a.Roles)
            .FirstOrDefaultAsync(a => a.Id == id, ct);

        if (app == null)
            return null;

        if (request.Name != null)
            app.Name = request.Name;
        if (request.Description != null)
            app.Description = request.Description;

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Updated application {AppCode}", app.Code);

        return MapToDetailResult(app);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var app = await _db.Applications.FindAsync([id], ct);
        if (app == null)
            return false;

        _db.Applications.Remove(app);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Deleted application {AppCode}", app.Code);

        return true;
    }

    public async Task<ResourceTypeResult?> AddResourceTypeAsync(Guid applicationId, CreateResourceTypeRequest request, CancellationToken ct = default)
    {
        var app = await _db.Applications.FindAsync([applicationId], ct);
        if (app == null)
            return null;

        if (await _db.ResourceTypes.AnyAsync(rt => rt.ApplicationId == applicationId && rt.Code == request.Code, ct))
            throw new InvalidOperationException($"Resource type '{request.Code}' already exists in this application");

        var resourceType = new ResourceType
        {
            ApplicationId = applicationId,
            Code = request.Code,
            Name = request.Name,
            Description = request.Description,
            SupportsInstances = request.SupportsInstances
        };

        _db.ResourceTypes.Add(resourceType);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Added resource type {ResourceTypeCode} to application {AppId}", request.Code, applicationId);

        return new ResourceTypeResult(new ResourceTypeSummary(
            resourceType.Id,
            resourceType.Code,
            resourceType.Name,
            resourceType.Description,
            resourceType.SupportsInstances));
    }

    private static ApplicationDetailResult MapToDetailResult(Application app)
    {
        return new ApplicationDetailResult(new ApplicationDetail(
            app.Id,
            app.Code,
            app.Name,
            app.Description,
            app.CreatedAt,
            app.ResourceTypes.Select(rt => new ResourceTypeSummary(
                rt.Id,
                rt.Code,
                rt.Name,
                rt.Description,
                rt.SupportsInstances)).ToList(),
            app.Roles.Select(r => new RoleSummary(
                r.Id,
                r.Code,
                r.Name,
                r.IsSystem)).ToList()));
    }
}
