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
