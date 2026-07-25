// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

namespace Andy.Rbac.Api.Services;

// SM.2.11 — GrantService owns admin revoke and server-side expiry sweep.
// Both operations emit a backend-pushed grant.revoked / grant.expired event
// via IRbacEventPublisher so Conductor's PermissionGrant aggregate (SM.10)
// reconciles on the FACT, not on a local TTL or a lazy gate-time check.
public interface IGrantService
{
    /// <summary>
    /// Revokes a specific InstancePermission grant by its ID.
    /// Emits grant.revoked to the outbox in the same transaction.
    /// </summary>
    /// <param name="grantId">InstancePermission.Id to revoke.</param>
    /// <param name="revokedByPrincipal">ExternalId of the admin performing the revocation. Null if automated.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>RevokeGrantResult describing the outcome.</returns>
    Task<RevokeGrantResult> RevokeAsync(Guid grantId, string? revokedByPrincipal, CancellationToken ct = default);

    /// <summary>
    /// Sweeps all InstancePermission rows whose ExpiresAt has been crossed,
    /// emits grant.expired for each, and removes them.
    /// Returns the count of grants swept.
    /// </summary>
    Task<int> SweepExpiredGrantsAsync(CancellationToken ct = default);

    /// <summary>
    /// Sweeps expired SubjectRole and TeamRole assignments, emitting
    /// subject_role.expired / team_role.expired for each, and removes them.
    /// Returns the total count swept.
    ///
    /// Issue #121: only InstancePermission was ever swept. Role assignments
    /// were honoured lazily — PermissionRepository filters expired ones at
    /// evaluation time — but nothing announced the lapse, so a consumer
    /// holding cached permissions kept authorising until its own TTL ran out,
    /// and the dead rows accumulated indefinitely. Expired assignments are
    /// deleted rather than retained, matching the instance-grant path; the
    /// audit log records the lifecycle.
    /// </summary>
    Task<int> SweepExpiredRoleAssignmentsAsync(CancellationToken ct = default);
}

public record RevokeGrantResult(bool Found, Guid? GrantId, string? Principal, string? PermissionCode);
