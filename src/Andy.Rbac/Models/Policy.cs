// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

namespace Andy.Rbac.Models;

/// <summary>
/// A policy binds delegation contracts, action-bus invocations and approval
/// gates to a named risk profile. Stock policies (read-only, write-branch,
/// sandboxed, no-prod, high-risk, draft-only) are seeded as IsSystem; tenants
/// may register additional non-system policies.
///
/// Cross-service identity is by <see cref="Code"/> — andy-tasks stores the
/// code in <c>Goal.PolicyId</c> / <c>DelegationContract.PolicyId</c>, not the
/// Guid. The Guid is the storage key and the wire-event identifier.
/// </summary>
public class Policy
{
    public Guid Id { get; set; }

    /// <summary>
    /// Stable slug used by other services as the cross-service identifier
    /// (e.g. "high-risk", "no-prod", "sandboxed"). Unique within the rbac DB.
    /// </summary>
    public required string Code { get; set; }

    public required string Name { get; set; }

    public PolicyCriticality Criticality { get; set; } = PolicyCriticality.Medium;

    /// <summary>
    /// Free-form rule body, persisted as jsonb on Postgres and TEXT on SQLite.
    /// Schema is intentionally open: each consumer (Conductor ActionBus per V5,
    /// andy-tasks AC3 auto-gating, andy-tasks AD4 retention) picks the keys it
    /// understands. Stable keys defined to date:
    ///   - <c>retentionDays</c>          (int)    — AD4 row retention override
    ///   - <c>archiveTier</c>            (string) — AD3/AJ archive tier
    ///   - <c>requirePreGate</c>         (bool)   — AC3 pre-execution gate
    ///   - <c>requirePostGate</c>        (bool)   — AC3 post-execution gate
    ///   - <c>blocksDeployTools</c>      (bool)   — V5 ActionBus deploy guard
    /// </summary>
    public Dictionary<string, object>? Rules { get; set; } = new();

    public string? Description { get; set; }

    /// <summary>
    /// System policies cannot be deleted or have their Code mutated. Stock
    /// policies (V2 seed) are flagged IsSystem.
    /// </summary>
    public bool IsSystem { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Risk classification for a policy. Drives default retention and gate
/// behaviour in downstream consumers (AD4, AC3, AD7).
/// </summary>
public enum PolicyCriticality
{
    Low = 0,
    Medium = 1,
    High = 2,
    Critical = 3,
}
