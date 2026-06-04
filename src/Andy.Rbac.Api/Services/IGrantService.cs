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
}

public record RevokeGrantResult(bool Found, Guid? GrantId, string? Principal, string? PermissionCode);
