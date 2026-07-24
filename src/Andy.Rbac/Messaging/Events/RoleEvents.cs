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

// Per-team role grant/revoke. Subject scheme:
//   andy.rbac.events.team_role.{assignment_id}.{kind}
//
// Distinct from subject_role rather than reusing it: a team grant reaches every
// current AND future member, so a consumer holding a per-subject projection
// must re-expand the team's membership rather than record one identity. Sending
// these as subject_role events with the team id in SubjectId would have
// consumers register a subject that does not exist.

public sealed record TeamRoleAssigned(
    Guid AssignmentId,
    Guid TeamId,
    string TeamCode,
    Guid RoleId,
    string RoleCode,
    string? ResourceInstanceId,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset OccurredAt
);

public sealed record TeamRoleRevoked(
    Guid AssignmentId,
    Guid TeamId,
    string TeamCode,
    Guid RoleId,
    string RoleCode,
    string? ResourceInstanceId,
    DateTimeOffset OccurredAt
);
