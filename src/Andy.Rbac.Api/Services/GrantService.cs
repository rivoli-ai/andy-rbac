// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Rbac.Infrastructure.Data;
using Andy.Rbac.Messaging;
using Andy.Rbac.Messaging.Events;
using Microsoft.EntityFrameworkCore;

namespace Andy.Rbac.Api.Services;

// SM.2.11 — service that owns admin revoke and the server-side expiry sweep
// for InstancePermission grants. Both operations stage a grant.revoked /
// grant.expired outbox row in the SAME transaction as the domain mutation,
// guaranteeing the event is published if and only if the revocation landed.
//
// The OutboxDispatcher (already wired in Program.cs) drains the rows to NATS
// with at-least-once delivery semantics; Conductor's GrantLifecycleEventSource
// subscribes on andy.rbac.events.grant.> and republishes onto
// ConductorEvent.grantLifecycle, driving the PermissionGrant aggregate (SM.10)
// to reduce to .revoked or .expired WITHOUT waiting for the next gate check.
public sealed class GrantService : IGrantService
{
    private readonly RbacDbContext _db;
    private readonly IRbacEventPublisher _events;
    private readonly ILogger<GrantService> _logger;

    public GrantService(RbacDbContext db, IRbacEventPublisher events, ILogger<GrantService> logger)
    {
        _db = db;
        _events = events;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<RevokeGrantResult> RevokeAsync(
        Guid grantId,
        string? revokedByPrincipal,
        CancellationToken ct = default)
    {
        // Load grant with navigation properties needed to populate the event.
        var grant = await _db.InstancePermissions
            .Include(ip => ip.Subject)
            .Include(ip => ip.Permission)
                .ThenInclude(p => p.ResourceType)
                    .ThenInclude(rt => rt.Application)
            .Include(ip => ip.Permission)
                .ThenInclude(p => p.Action)
            .Include(ip => ip.ResourceInstance)
            .FirstOrDefaultAsync(ip => ip.Id == grantId, ct);

        if (grant == null)
            return new RevokeGrantResult(Found: false, GrantId: null, Principal: null, PermissionCode: null);

        var principal = grant.Subject.ExternalId;
        var permissionCode = grant.Permission.Code;
        var scopeResourceInstanceId = grant.ResourceInstance?.ExternalId;

        _db.InstancePermissions.Remove(grant);

        // Stage grant.revoked outbox row inside the same transaction.
        _events.GrantRevoked(new GrantRevoked(
            GrantId: grantId,
            Principal: principal,
            SubjectId: grant.SubjectId,
            PermissionCode: permissionCode,
            ScopeResourceInstanceId: scopeResourceInstanceId,
            RevokedByPrincipal: revokedByPrincipal,
            RevokedAt: DateTimeOffset.UtcNow));

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Revoked grant {GrantId} (permission={Permission}, principal={Principal}) by {RevokedBy}",
            grantId, permissionCode, principal, revokedByPrincipal ?? "<system>");

        return new RevokeGrantResult(Found: true, GrantId: grantId, Principal: principal, PermissionCode: permissionCode);
    }

    /// <inheritdoc/>
    public async Task<int> SweepExpiredGrantsAsync(CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;

        // Load all expired grants with the identity fields the event needs.
        var expired = await _db.InstancePermissions
            .Include(ip => ip.Subject)
            .Include(ip => ip.Permission)
                .ThenInclude(p => p.ResourceType)
                    .ThenInclude(rt => rt.Application)
            .Include(ip => ip.Permission)
                .ThenInclude(p => p.Action)
            .Include(ip => ip.ResourceInstance)
            .Where(ip => ip.ExpiresAt != null && ip.ExpiresAt <= now)
            .ToListAsync(ct);

        if (expired.Count == 0)
            return 0;

        foreach (var grant in expired)
        {
            _db.InstancePermissions.Remove(grant);

            // Stage grant.expired outbox row — same transaction as removal.
            _events.GrantExpired(new GrantExpired(
                GrantId: grant.Id,
                Principal: grant.Subject.ExternalId,
                SubjectId: grant.SubjectId,
                PermissionCode: grant.Permission.Code,
                ScopeResourceInstanceId: grant.ResourceInstance?.ExternalId,
                ExpiredAt: grant.ExpiresAt!.Value,
                OccurredAt: now));
        }

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Swept {Count} expired grant(s) and staged grant.expired events",
            expired.Count);

        return expired.Count;
    }
}
