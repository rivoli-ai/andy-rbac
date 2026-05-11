// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Text.Json;
using Andy.Rbac.Infrastructure.Data;
using Andy.Rbac.Messaging;
using Andy.Rbac.Messaging.Events;

namespace Andy.Rbac.Infrastructure.Messaging;

// AL2 + AL3 + AL4: stages outbox rows on the shared RbacDbContext. The
// caller's SaveChangesAsync commits the domain row + the outbox row in
// a single transaction; the OutboxDispatcher later drains the row to
// NATS with at-least-once delivery semantics.
public sealed class RbacEventPublisher : IRbacEventPublisher
{
    private const string SubjectPrefix = "andy.rbac.events";

    private readonly RbacDbContext _db;

    public RbacEventPublisher(RbacDbContext db)
    {
        _db = db;
    }

    public void RoleCreated(RoleCreated payload, MessageHeaders? headers = null)
        => Stage($"{SubjectPrefix}.role.{payload.RoleId}.created", payload, headers);

    public void RoleUpdated(RoleUpdated payload, MessageHeaders? headers = null)
        => Stage($"{SubjectPrefix}.role.{payload.RoleId}.updated", payload, headers);

    public void RoleDeleted(RoleDeleted payload, MessageHeaders? headers = null)
        => Stage($"{SubjectPrefix}.role.{payload.RoleId}.deleted", payload, headers);

    public void RoleAssigned(RoleAssigned payload, MessageHeaders? headers = null)
        => Stage($"{SubjectPrefix}.subject_role.{payload.AssignmentId}.granted", payload, headers);

    public void RoleRevoked(RoleRevoked payload, MessageHeaders? headers = null)
        => Stage($"{SubjectPrefix}.subject_role.{payload.AssignmentId}.revoked", payload, headers);

    public void PolicyCreated(PolicyCreated payload, MessageHeaders? headers = null)
        => Stage($"{SubjectPrefix}.policy.{payload.PolicyId}.created", payload, headers);

    public void PolicyUpdated(PolicyUpdated payload, MessageHeaders? headers = null)
        => Stage($"{SubjectPrefix}.policy.{payload.PolicyId}.updated", payload, headers);

    public void PolicyDeleted(PolicyDeleted payload, MessageHeaders? headers = null)
        => Stage($"{SubjectPrefix}.policy.{payload.PolicyId}.deleted", payload, headers);

    public void RetentionChanged(RetentionChanged payload, MessageHeaders? headers = null)
        => Stage($"{SubjectPrefix}.policy.{payload.PolicyId}.retention_changed", payload, headers);

    private void Stage(string subject, object payload, MessageHeaders? headers)
    {
        headers ??= MessageHeaders.NewRoot();
        if (headers.ExceedsGenerationLimit)
        {
            // Defense-in-depth — should be unreachable since rbac is publisher-
            // only and starts every chain at generation 0. If we ever wire
            // event-driven reactions in rbac itself, this guard prevents a
            // runaway cycle from leaking onto the bus.
            return;
        }

        var json = JsonSerializer.Serialize(payload, payload.GetType(), EventJson.Options);

        _db.Outbox.Add(new OutboxEntry
        {
            Id = headers.MsgId,
            Subject = subject,
            PayloadType = payload.GetType().FullName,
            PayloadJson = json,
            CorrelationId = headers.CorrelationId,
            CausationId = headers.CausationId,
            Generation = headers.Generation,
            CreatedAt = DateTimeOffset.UtcNow,
        });
    }
}
