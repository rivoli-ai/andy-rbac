// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Rbac.Messaging.Events;

namespace Andy.Rbac.Messaging;

// AL3 + AL4 publisher facade for the rbac service. Callers (RoleService,
// future PolicyService) invoke the typed helpers below; the implementation
// stages an OutboxEntry on the DbContext but does NOT call SaveChangesAsync —
// the caller's existing SaveChanges commits both the domain row and the
// outbox row in a single transaction (transactional outbox pattern, AL2).
//
// Subject scheme (ADR 0001 + AL3):
//   andy.rbac.events.role.{role_id}.{created|updated|deleted}
//   andy.rbac.events.subject_role.{assignment_id}.{granted|revoked}
//   andy.rbac.events.policy.{policy_id}.{created|updated|deleted|retention_changed}
//   andy.rbac.events.grant.{grant_id}.{revoked|expired}   ← SM.2.11
public interface IRbacEventPublisher
{
    void RoleCreated(RoleCreated payload, MessageHeaders? headers = null);
    void RoleUpdated(RoleUpdated payload, MessageHeaders? headers = null);
    void RoleDeleted(RoleDeleted payload, MessageHeaders? headers = null);
    void RoleAssigned(RoleAssigned payload, MessageHeaders? headers = null);
    void RoleRevoked(RoleRevoked payload, MessageHeaders? headers = null);

    // Team grants reach every current and future member, so they carry their
    // own subject rather than masquerading as a subject_role event.
    void TeamRoleAssigned(TeamRoleAssigned payload, MessageHeaders? headers = null);
    void TeamRoleRevoked(TeamRoleRevoked payload, MessageHeaders? headers = null);

    // Emitted by the server-side expiry sweep. Separate from the revoked
    // events so consumers can distinguish an administrative action from a
    // time-boxed grant lapsing.
    void RoleExpired(RoleExpired payload, MessageHeaders? headers = null);
    void TeamRoleExpired(TeamRoleExpired payload, MessageHeaders? headers = null);

    void PolicyCreated(PolicyCreated payload, MessageHeaders? headers = null);
    void PolicyUpdated(PolicyUpdated payload, MessageHeaders? headers = null);
    void PolicyDeleted(PolicyDeleted payload, MessageHeaders? headers = null);
    // AL4 — fires when the policy's `retentionDays` rule value changes
    // (alongside the generic PolicyUpdated). Consumers (rivoli-ai/andy-tasks#74)
    // dedupe on `ChangeId` for at-least-once delivery semantics.
    void RetentionChanged(RetentionChanged payload, MessageHeaders? headers = null);

    // SM.2.11 — backend-pushed grant lifecycle events. Callers: GrantService
    // (admin revoke) and GrantExpiryWorker (server-side expiry sweep).
    // Consumers: Conductor GrantLifecycleEventSource → ConductorEventBus →
    // PermissionGrant aggregate (SM.10). The push makes a grant inert in
    // Conductor WITHOUT waiting for the next gate consultation.
    void GrantRevoked(GrantRevoked payload, MessageHeaders? headers = null);
    void GrantExpired(GrantExpired payload, MessageHeaders? headers = null);
}
