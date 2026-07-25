// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Rbac.Api.Services;
using Andy.Rbac.Infrastructure.Data;
using Andy.Rbac.Infrastructure.Messaging;
using Andy.Rbac.Messaging.Events;
using Andy.Rbac.Models;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Andy.Rbac.Api.Tests.Services;

/// <summary>
/// SM.2.11 coverage for the revoke paths clients actually call.
///
/// <see cref="GrantService.RevokeAsync"/> (by grant GUID, via GrantsController)
/// already staged <c>grant.revoked</c>. The paths reached from
/// <c>InstancesController</c> and <c>RbacHttpClient</c> —
/// <see cref="ResourceInstanceService.RevokeAsync"/> and
/// <see cref="ResourceInstanceService.RemoveAsync"/>, the latter cascading the
/// delete across every grant on the instance — staged nothing, so a consumer
/// kept treating revoked grants as live until its own cache lapsed.
/// </summary>
public class ResourceInstanceGrantEventTests
{
    private static readonly Guid DocumentResourceTypeId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid ViewerSubjectId = Guid.Parse("66666666-6666-6666-6666-666666666668");
    private static readonly Guid ReadPermissionId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    private static ResourceInstanceService CreateService(RbacDbContext db) =>
        new(db, new RbacEventPublisher(db));

    /// <summary>Seeds a resource instance carrying one read grant for viewer-user.</summary>
    private static async Task<(RbacDbContext db, ResourceInstance instance, InstancePermission grant)>
        CreateDbWithGrantAsync(string externalId = "doc-events")
    {
        var db = await TestDbContextFactory.CreateWithSeedDataAsync();

        var instance = new ResourceInstance
        {
            Id = Guid.NewGuid(),
            ResourceTypeId = DocumentResourceTypeId,
            ExternalId = externalId,
            DisplayName = "Grant event test doc"
        };
        db.ResourceInstances.Add(instance);

        var grant = new InstancePermission
        {
            Id = Guid.NewGuid(),
            SubjectId = ViewerSubjectId,
            PermissionId = ReadPermissionId,
            ResourceInstanceId = instance.Id,
            GrantedAt = DateTimeOffset.UtcNow.AddMinutes(-5)
        };
        db.InstancePermissions.Add(grant);
        await db.SaveChangesAsync();

        return (db, instance, grant);
    }

    [Fact]
    public async Task RevokeAsync_StagesGrantRevokedOutboxRow()
    {
        var (db, _, grant) = await CreateDbWithGrantAsync();
        using var _db = db;

        var result = await CreateService(db).RevokeAsync(
            "test-app", "document", "doc-events",
            "viewer-user", subjectProvider: null, action: "read",
            revokedByPrincipal: "admin-user");

        result.Success.Should().BeTrue();
        (await db.InstancePermissions.FindAsync(grant.Id)).Should().BeNull();

        var entry = await db.Outbox.SingleAsync();
        entry.Subject.Should().Be($"andy.rbac.events.grant.{grant.Id}.revoked");
        entry.PayloadType.Should().Be(typeof(GrantRevoked).FullName);
        entry.PublishedAt.Should().BeNull("the dispatcher publishes it; the write only stages it");
    }

    [Fact]
    public async Task RevokeAsync_OutboxRow_CarriesIdentityAndScope()
    {
        var (db, _, _) = await CreateDbWithGrantAsync();
        using var _db = db;

        await CreateService(db).RevokeAsync(
            "test-app", "document", "doc-events",
            "viewer-user", subjectProvider: null, action: "read",
            revokedByPrincipal: "admin-user");

        var entry = await db.Outbox.SingleAsync();
        entry.PayloadJson.Should().Contain("viewer-user");
        entry.PayloadJson.Should().Contain("test-app:document:read");
        entry.PayloadJson.Should().Contain("doc-events");
        entry.PayloadJson.Should().Contain("admin-user");
    }

    [Fact]
    public async Task RemoveAsync_StagesGrantRevokedForEveryCascadedGrant()
    {
        var (db, instance, firstGrant) = await CreateDbWithGrantAsync();
        using var _db = db;

        // A second grant on the same instance — both die with the cascade.
        var secondGrant = new InstancePermission
        {
            Id = Guid.NewGuid(),
            SubjectId = Guid.Parse("66666666-6666-6666-6666-666666666667"),
            PermissionId = ReadPermissionId,
            ResourceInstanceId = instance.Id,
            GrantedAt = DateTimeOffset.UtcNow
        };
        db.InstancePermissions.Add(secondGrant);
        await db.SaveChangesAsync();

        var result = await CreateService(db).RemoveAsync(
            "test-app", "document", "doc-events", revokedByPrincipal: "admin-user");

        result.Success.Should().BeTrue();

        var subjects = await db.Outbox.Select(e => e.Subject).ToListAsync();
        subjects.Should().BeEquivalentTo([
            $"andy.rbac.events.grant.{firstGrant.Id}.revoked",
            $"andy.rbac.events.grant.{secondGrant.Id}.revoked"
        ], "a cascade that silently kills grants is the stale-grant class SM.2.11 closes");
    }

    [Fact]
    public async Task RemoveAsync_InstanceWithNoGrants_StagesNothing()
    {
        var db = await TestDbContextFactory.CreateWithSeedDataAsync();
        using var _db = db;

        db.ResourceInstances.Add(new ResourceInstance
        {
            Id = Guid.NewGuid(),
            ResourceTypeId = DocumentResourceTypeId,
            ExternalId = "doc-empty"
        });
        await db.SaveChangesAsync();

        var result = await CreateService(db).RemoveAsync("test-app", "document", "doc-empty");

        result.Success.Should().BeTrue();
        (await db.Outbox.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task RevokeAsync_MissingGrant_StagesNothing()
    {
        var (db, _, _) = await CreateDbWithGrantAsync();
        using var _db = db;

        var result = await CreateService(db).RevokeAsync(
            "test-app", "document", "doc-events",
            "viewer-user", subjectProvider: null, action: "write");

        result.NotFound.Should().BeTrue();
        (await db.Outbox.AnyAsync()).Should().BeFalse();
    }
}
