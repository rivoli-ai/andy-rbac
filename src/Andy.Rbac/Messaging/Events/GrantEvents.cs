// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

namespace Andy.Rbac.Messaging.Events;

// SM.2.11 — grant lifecycle push signals. Emitted server-side so that
// Conductor's PermissionGrant aggregate (SM.10) reconciles on a backend
// FACT rather than relying on a local TTL or a lazy gate-time check.
//
// Subject scheme (ADR 0001):
//   andy.rbac.events.grant.{grantId}.revoked
//   andy.rbac.events.grant.{grantId}.expired
//
// Consumers: Conductor GrantLifecycleEventSource (SM.10) subscribes to
// andy.rbac.events.grant.> and republishes onto ConductorEvent.grantLifecycle,
// so the local PermissionGrantStore reduces .revoked/.expired on a push,
// WITHOUT waiting for the next gate consultation.
//
// Wire format: snake_case JSON via EventJson.Options (same as all rbac events).

/// <summary>
/// Emitted when an admin explicitly revokes an InstancePermission grant.
/// GrantId is the InstancePermission.Id; Principal is the Subject.ExternalId.
/// Scope fields carry enough identity to match a local grant exactly.
/// </summary>
public sealed record GrantRevoked(
    // The InstancePermission.Id that was revoked.
    Guid GrantId,
    // Subject.ExternalId of the grantee.
    string Principal,
    // Subject.Id of the grantee (local DB identity for index lookups).
    Guid SubjectId,
    // Permission code (app:resource:action).
    string PermissionCode,
    // Resource instance external ID (scope). Null = global grant.
    string? ScopeResourceInstanceId,
    // Subject.ExternalId of the admin who performed the revocation. Null if automated.
    string? RevokedByPrincipal,
    DateTimeOffset RevokedAt
);

/// <summary>
/// Emitted by the server-side expiry sweep when an InstancePermission's
/// ExpiresAt has been crossed. The sweep runs as a background worker
/// (GrantExpiryWorker) so this event arrives without any client
/// involvement — the grant becomes inert in Conductor on the push,
/// not on the next gate consultation.
/// </summary>
public sealed record GrantExpired(
    // The InstancePermission.Id that expired.
    Guid GrantId,
    // Subject.ExternalId of the grantee.
    string Principal,
    // Subject.Id of the grantee.
    Guid SubjectId,
    // Permission code (app:resource:action).
    string PermissionCode,
    // Resource instance external ID (scope). Null = global grant.
    string? ScopeResourceInstanceId,
    // The ExpiresAt timestamp that was crossed.
    DateTimeOffset ExpiredAt,
    DateTimeOffset OccurredAt
);
