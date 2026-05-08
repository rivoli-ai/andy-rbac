// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Rbac.Models;

namespace Andy.Rbac.Api.Services;

/// <summary>
/// Service for managing policies (Epic V). Policies are catalog rows that
/// downstream services (andy-tasks, andy-docs, Conductor) reference by
/// <see cref="PolicyDetail.Code"/> to drive auto-gating, retention, and
/// action-bus enforcement decisions.
/// </summary>
public interface IPolicyService
{
    Task<PolicyListResult> GetAllAsync(CancellationToken ct = default);
    Task<PolicyDetailResult?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<PolicyDetailResult?> GetByCodeAsync(string code, CancellationToken ct = default);
    Task<PolicyDetailResult> CreateAsync(CreatePolicyRequest request, CancellationToken ct = default);
    Task<PolicyDetailResult?> UpdateAsync(Guid id, UpdatePolicyRequest request, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
}

public record PolicyDetail(
    Guid Id,
    string Code,
    string Name,
    PolicyCriticality Criticality,
    Dictionary<string, object>? Rules,
    string? Description,
    bool IsSystem,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public record PolicyListResult(List<PolicyDetail> Policies);
public record PolicyDetailResult(PolicyDetail Policy);

public record CreatePolicyRequest(
    string Code,
    string Name,
    PolicyCriticality Criticality,
    Dictionary<string, object>? Rules = null,
    string? Description = null);

public record UpdatePolicyRequest(
    string? Name = null,
    PolicyCriticality? Criticality = null,
    Dictionary<string, object>? Rules = null,
    string? Description = null);
