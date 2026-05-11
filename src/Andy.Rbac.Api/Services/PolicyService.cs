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

        var now = DateTimeOffset.UtcNow;
        var policy = new Policy
        {
            Id = Guid.NewGuid(),
            Code = request.Code,
            Name = request.Name,
            Criticality = request.Criticality,
            Rules = request.Rules,
            Description = request.Description,
            IsSystem = false,
            CreatedAt = now,
            UpdatedAt = now,
        };

        _db.Policies.Add(policy);
        _events.PolicyCreated(new PolicyCreated(
            PolicyId: policy.Id,
            Code: policy.Code,
            ApplicationCode: null,
            OccurredAt: now));

        // AL4 retention_changed at creation time: previous = null (no prior
        // value), current = whatever the create request specified. Consumers
        // (rivoli-ai/andy-tasks#74) treat null → value the same as
        // value → value with the same idempotency semantics.
        var newDays = ExtractRetentionDays(policy.Rules);
        if (newDays.HasValue)
        {
            _events.RetentionChanged(new RetentionChanged(
                PolicyId: policy.Id,
                Code: policy.Code,
                PreviousRetentionDays: null,
                NewRetentionDays: newDays,
                ChangeId: Guid.NewGuid().ToString("N"),
                ChangedBy: null,
                OccurredAt: now));
        }

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

        // AL4: snapshot the previous retentionDays before mutating Rules so
        // the RetentionChanged event can carry the old value.
        var previousDays = ExtractRetentionDays(policy.Rules);

        if (request.Name != null) policy.Name = request.Name;
        if (request.Criticality.HasValue) policy.Criticality = request.Criticality.Value;
        if (request.Rules != null) policy.Rules = request.Rules;
        if (request.Description != null) policy.Description = request.Description;
        var now = DateTimeOffset.UtcNow;
        policy.UpdatedAt = now;

        _events.PolicyUpdated(new PolicyUpdated(
            PolicyId: policy.Id,
            Code: policy.Code,
            ApplicationCode: null,
            OccurredAt: now));

        // AL4: when the retentionDays rule changed (in either direction or
        // null ↔ value), fire the specific retention_changed event so
        // downstream consumers (rivoli-ai/andy-tasks#74) can cascade TTL
        // updates without re-parsing the generic PolicyUpdated payload.
        var newDays = ExtractRetentionDays(policy.Rules);
        if (previousDays != newDays)
        {
            _events.RetentionChanged(new RetentionChanged(
                PolicyId: policy.Id,
                Code: policy.Code,
                PreviousRetentionDays: previousDays,
                NewRetentionDays: newDays,
                ChangeId: Guid.NewGuid().ToString("N"),
                ChangedBy: null,
                OccurredAt: now));
        }

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

    // AL4 helper: read the `retentionDays` integer off the policy's
    // free-form Rules dictionary. Lives next to MapToDetail since both
    // probe the same loosely-typed surface. Returns null when the rule
    // is absent or unparseable so the caller can model "no retention
    // configured" the same way as "consumer ignored the rule."
    private static int? ExtractRetentionDays(Dictionary<string, object>? rules)
    {
        if (rules is null) return null;
        // Stable key per Policy.cs:35 doc-comment.
        if (!rules.TryGetValue("retentionDays", out var raw) || raw is null) return null;
        // EF Core's Postgres jsonb conversion + the in-memory test path can
        // return the same logical value under several CLR types — int,
        // long, double, decimal, or System.Text.Json.JsonElement (when the
        // dictionary was rehydrated from a JSON deserializer). Probe all
        // so we don't silently drop the event.
        return raw switch
        {
            int i => i,
            long l when l >= int.MinValue && l <= int.MaxValue => (int)l,
            double d when !double.IsNaN(d) && d >= int.MinValue && d <= int.MaxValue => (int)d,
            decimal m when m >= int.MinValue && m <= int.MaxValue => (int)m,
            System.Text.Json.JsonElement el => el.ValueKind == System.Text.Json.JsonValueKind.Number
                && el.TryGetInt32(out var v) ? v : null,
            string s when int.TryParse(s, out var v) => v,
            _ => null,
        };
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
