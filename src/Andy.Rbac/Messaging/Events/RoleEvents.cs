// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

namespace Andy.Rbac.Messaging.Events;

// AL4 Role lifecycle events. Subject scheme (AL3):
//   andy.rbac.events.role.{role_id}.{kind}
//
// Consumers (andy-tasks AD4a, future andy-docs RetentionCascadeWorker)
// subscribe to andy.rbac.events.role.> with a durable consumer and
// project role membership / permission deltas into their own caches.

public sealed record RoleCreated(
    Guid RoleId,
    string Code,
    string Name,
    string? ApplicationCode,
    string? ParentRoleCode,
    bool IsSystem,
    DateTimeOffset OccurredAt
);

public sealed record RoleUpdated(
    Guid RoleId,
    string Code,
    string Name,
    string? ApplicationCode,
    DateTimeOffset OccurredAt
);

public sealed record RoleDeleted(
    Guid RoleId,
    string Code,
    string? ApplicationCode,
    DateTimeOffset OccurredAt
);

// Per-subject role grant/revoke. Subject scheme:
//   andy.rbac.events.subject_role.{assignment_id}.{kind}

public sealed record RoleAssigned(
    Guid AssignmentId,
    Guid SubjectId,
    string SubjectExternalId,
    Guid RoleId,
    string RoleCode,
    string? ResourceInstanceId,
    DateTimeOffset OccurredAt
);

public sealed record RoleRevoked(
    Guid AssignmentId,
    Guid SubjectId,
    string SubjectExternalId,
    Guid RoleId,
    string RoleCode,
    string? ResourceInstanceId,
    DateTimeOffset OccurredAt
);
