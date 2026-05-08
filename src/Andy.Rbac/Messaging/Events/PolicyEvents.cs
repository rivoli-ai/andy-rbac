// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

namespace Andy.Rbac.Messaging.Events;

// AL4 Policy lifecycle events — STUB. Epic V (Policy entity + service)
// shipped as the design doc only; the runtime entity has not landed on
// main yet. These types and their subject scheme are reserved here so
// that downstream consumers (andy-docs RetentionCascadeWorker, future
// PDP cache invalidation) can pin against a stable wire contract while
// the entity work catches up. The publisher exposes Policy.* helpers
// that throw NotImplementedException — wiring them to a real entity is
// a one-line change once Epic V lands.
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
