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
//   andy.rbac.events.policy.{policy_id}.{created|updated|deleted}    (stub — Epic V pending)
public interface IRbacEventPublisher
{
    void RoleCreated(RoleCreated payload, MessageHeaders? headers = null);
    void RoleUpdated(RoleUpdated payload, MessageHeaders? headers = null);
    void RoleDeleted(RoleDeleted payload, MessageHeaders? headers = null);
    void RoleAssigned(RoleAssigned payload, MessageHeaders? headers = null);
    void RoleRevoked(RoleRevoked payload, MessageHeaders? headers = null);

    // Stubs — Epic V (Policy entity) hasn't shipped to main yet. Calling
    // these throws NotImplementedException so accidental wiring fails
    // loudly rather than silently no-op'ing. Once Epic V lands, drop the
    // throws and stage outbox rows just like the Role helpers above.
    void PolicyCreated(PolicyCreated payload, MessageHeaders? headers = null);
    void PolicyUpdated(PolicyUpdated payload, MessageHeaders? headers = null);
    void PolicyDeleted(PolicyDeleted payload, MessageHeaders? headers = null);
}
