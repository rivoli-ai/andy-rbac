// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

namespace Andy.Rbac.Messaging.Events;

// AL4 Policy lifecycle events. Emitted by PolicyService writes via
// RbacEventPublisher.Policy{Created,Updated,Deleted} on a transactional
// outbox commit. Consumers: andy-tasks AC3 (auto-gating cache invalidation),
// andy-tasks AD4/AD4a (retention cascade), andy-docs RetentionCascadeWorker.
//
// Subject scheme (AL3): andy.rbac.events.policy.{policy_id}.{kind}

public sealed record PolicyCreated(
    Guid PolicyId,
    string Code,
    string? ApplicationCode,
    DateTimeOffset OccurredAt
);

public sealed record PolicyUpdated(
    Guid PolicyId,
    string Code,
    string? ApplicationCode,
    DateTimeOffset OccurredAt
);

public sealed record PolicyDeleted(
    Guid PolicyId,
    string Code,
    string? ApplicationCode,
    DateTimeOffset OccurredAt
);

// AL4 RetentionChanged event. Fires from PolicyService.Update / Create
// whenever the policy's `retentionDays` rule value changes (or moves
// between null ↔ value). Subject:
//   andy.rbac.events.policy.{policy_id}.retention_changed
//
// Carries before / after values so the consumer can decide direction
// (increase vs decrease) without joining back to andy-rbac. `ChangeId`
// is the idempotency token the consumer dedupes on; pre-generated here
// so the same DB transaction that stages the outbox row carries the
// id the bus will publish. AD4a's downstream cascade in andy-tasks +
// andy-docs subscribes to this subject.
//
// Consumed by: rivoli-ai/andy-tasks#74 (AD4a — TTL cascade on agent_runs +
// archive-tier mutation via andy-docs Epic AJ6).
public sealed record RetentionChanged(
    Guid PolicyId,
    string Code,
    int? PreviousRetentionDays,
    int? NewRetentionDays,
    string ChangeId,
    string? ChangedBy,
    DateTimeOffset OccurredAt,
    int SchemaVersion = 1
);
