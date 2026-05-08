// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Rbac.Infrastructure.Data;
using Andy.Rbac.Messaging;
using Andy.Rbac.Messaging.Events;
using Andy.Rbac.Models;
using Microsoft.EntityFrameworkCore;

namespace Andy.Rbac.Api.Services;

/// <summary>
/// Service for managing policies. Mirrors <see cref="RoleService"/>'s shape:
/// stages outbox events on the same DbContext as the domain write so the
/// transactional outbox (AL2) commits both atomically.
/// </summary>
public class PolicyService : IPolicyService
{
    private readonly RbacDbContext _db;
    private readonly ILogger<PolicyService> _logger;
    private readonly IRbacEventPublisher _events;

    public PolicyService(RbacDbContext db, ILogger<PolicyService> logger, IRbacEventPublisher events)
    {
        _db = db;
        _logger = logger;
        _events = events;
    }

    public async Task<PolicyListResult> GetAllAsync(CancellationToken ct = default)
    {
        var policies = await _db.Policies
            .AsNoTracking()
            .OrderBy(p => p.Code)
            .Select(p => MapToDetail(p))
            .ToListAsync(ct);

        return new PolicyListResult(policies);
    }

    public async Task<PolicyDetailResult?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var policy = await _db.Policies.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct);
        return policy == null ? null : new PolicyDetailResult(MapToDetail(policy));
    }

    public async Task<PolicyDetailResult?> GetByCodeAsync(string code, CancellationToken ct = default)
    {
        var policy = await _db.Policies.AsNoTracking().FirstOrDefaultAsync(p => p.Code == code, ct);
        return policy == null ? null : new PolicyDetailResult(MapToDetail(policy));
    }

    public async Task<PolicyDetailResult> CreateAsync(CreatePolicyRequest request, CancellationToken ct = default)
    {
        if (await _db.Policies.AnyAsync(p => p.Code == request.Code, ct))
            throw new InvalidOperationException($"Policy with code '{request.Code}' already exists");

        var policy = new Policy
        {
            Id = Guid.NewGuid(),
            Code = request.Code,
            Name = request.Name,
            Criticality = request.Criticality,
            Rules = request.Rules,
            Description = request.Description,
            IsSystem = false,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        _db.Policies.Add(policy);
        _events.PolicyCreated(new PolicyCreated(
            PolicyId: policy.Id,
            Code: policy.Code,
            ApplicationCode: null,
            OccurredAt: DateTimeOffset.UtcNow));
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Created policy {PolicyCode}", policy.Code);

        return new PolicyDetailResult(MapToDetail(policy));
    }

    public async Task<PolicyDetailResult?> UpdateAsync(Guid id, UpdatePolicyRequest request, CancellationToken ct = default)
    {
        var policy = await _db.Policies.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (policy == null) return null;

        if (policy.IsSystem)
            throw new InvalidOperationException("Cannot modify system policies");

        if (request.Name != null) policy.Name = request.Name;
        if (request.Criticality.HasValue) policy.Criticality = request.Criticality.Value;
        if (request.Rules != null) policy.Rules = request.Rules;
        if (request.Description != null) policy.Description = request.Description;
        policy.UpdatedAt = DateTimeOffset.UtcNow;

        _events.PolicyUpdated(new PolicyUpdated(
            PolicyId: policy.Id,
            Code: policy.Code,
            ApplicationCode: null,
            OccurredAt: DateTimeOffset.UtcNow));
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Updated policy {PolicyCode}", policy.Code);

        return new PolicyDetailResult(MapToDetail(policy));
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var policy = await _db.Policies.FindAsync([id], ct);
        if (policy == null) return false;

        if (policy.IsSystem)
            throw new InvalidOperationException("Cannot delete system policies");

        _db.Policies.Remove(policy);
        _events.PolicyDeleted(new PolicyDeleted(
            PolicyId: policy.Id,
            Code: policy.Code,
            ApplicationCode: null,
            OccurredAt: DateTimeOffset.UtcNow));
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Deleted policy {PolicyCode}", policy.Code);

        return true;
    }

    private static PolicyDetail MapToDetail(Policy p) => new(
        p.Id,
        p.Code,
        p.Name,
        p.Criticality,
        p.Rules,
        p.Description,
        p.IsSystem,
        p.CreatedAt,
        p.UpdatedAt);
}
