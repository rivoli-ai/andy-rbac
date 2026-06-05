// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Rbac.Api.Services;
using Andy.Rbac.Infrastructure.Data;
using Andy.Rbac.Infrastructure.Messaging;
using Andy.Rbac.Messaging.Events;
using Andy.Rbac.Models;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Andy.Rbac.Api.Tests.Services;

// SM.2.11 — unit tests for GrantService. Verifies that:
//   - admin revoke removes the grant + stages grant.revoked outbox row in one tx
//   - expiry sweep removes expired grants + stages grant.expired per row
//   - each outbox row carries grantId + principal + scope for exact reconciliation
//   - non-existent grant revoke returns Found=false, no outbox row
//   - grant.revoked for a different grant id doesn't touch the active grant
//   - only grants with ExpiresAt ≤ now are swept (no future grants affected)
public class GrantServiceTests
{
    // -----------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------

    private static async Task<(RbacDbContext db, InstancePermission grant)> CreateDbWithGrantAsync(
        DateTimeOffset? expiresAt = null)
    {
        var context = await TestDbContextFactory.CreateWithSeedDataAsync();

        // Pull ids from TestDbContextFactory seed
        var viewerSubjectId = Guid.Parse("66666666-6666-6666-6666-666666666668");
        var readPermissionId = Guid.Parse("44444444-4444-4444-4444-444444444444");

        // Create a resource instance to scope the grant
        var resourceInstance = new ResourceInstance
        {
            Id = Guid.NewGuid(),
            ResourceTypeId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
            ExternalId = "doc-sm211",
            DisplayName = "SM.2.11 test doc"
        };
        context.ResourceInstances.Add(resourceInstance);

        var grant = new InstancePermission
        {
            Id = Guid.NewGuid(),
            SubjectId = viewerSubjectId,
            PermissionId = readPermissionId,
            ResourceInstanceId = resourceInstance.Id,
            GrantedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
            ExpiresAt = expiresAt
        };
        context.InstancePermissions.Add(grant);
        await context.SaveChangesAsync();

        return (context, grant);
    }

    // -----------------------------------------------------------------
    // Admin revoke — happy path
    // -----------------------------------------------------------------

    [Fact]
    public async Task RevokeAsync_ExistingGrant_RemovesGrantAndStagesOutboxRow()
    {
        var (context, grant) = await CreateDbWithGrantAsync();
        var publisher = new RbacEventPublisher(context);
        var sut = new GrantService(context, publisher, Mock.Of<ILogger<GrantService>>());

        var result = await sut.RevokeAsync(grant.Id, "admin-user");

        // Grant must be removed
        result.Found.Should().BeTrue();
        result.GrantId.Should().Be(grant.Id);
        var remaining = await context.InstancePermissions.FindAsync(grant.Id);
        remaining.Should().BeNull();

        // Exactly one outbox row, on the correct subject
        var outboxEntry = await context.Outbox.SingleAsync();
        outboxEntry.Subject.Should().Be($"andy.rbac.events.grant.{grant.Id}.revoked");
        outboxEntry.PayloadType.Should().Be(typeof(GrantRevoked).FullName);
        outboxEntry.PublishedAt.Should().BeNull();
    }

    [Fact]
    public async Task RevokeAsync_OutboxRow_CarriesGrantIdPrincipalAndPermissionCode()
    {
        var (context, grant) = await CreateDbWithGrantAsync();
        var publisher = new RbacEventPublisher(context);
        var sut = new GrantService(context, publisher, Mock.Of<ILogger<GrantService>>());

        var result = await sut.RevokeAsync(grant.Id, "admin-user");

        result.Principal.Should().Be("viewer-user"); // ExternalId from seed
        result.PermissionCode.Should().Be("test-app:document:read");

        var outboxEntry = await context.Outbox.SingleAsync();
        // snake_case JSON per EventJson.Options
        outboxEntry.PayloadJson.Should().Contain("\"grant_id\"");
        outboxEntry.PayloadJson.Should().Contain("\"principal\"");
        outboxEntry.PayloadJson.Should().Contain("\"permission_code\"");
        outboxEntry.PayloadJson.Should().Contain("viewer-user");
        outboxEntry.PayloadJson.Should().Contain("test-app:document:read");
        outboxEntry.PayloadJson.Should().Contain("doc-sm211"); // scope
        outboxEntry.PayloadJson.Should().Contain("admin-user"); // revoked_by_principal
    }

    [Fact]
    public async Task RevokeAsync_NonExistentGrant_ReturnsFoundFalseNoOutboxRow()
    {
        var (context, _) = await CreateDbWithGrantAsync();
        var publisher = new RbacEventPublisher(context);
        var sut = new GrantService(context, publisher, Mock.Of<ILogger<GrantService>>());

        var result = await sut.RevokeAsync(Guid.NewGuid(), "admin-user");

        result.Found.Should().BeFalse();
        context.Outbox.Should().BeEmpty();
    }

    // -----------------------------------------------------------------
    // Revoke one grant does not touch another
    // -----------------------------------------------------------------

    [Fact]
    public async Task RevokeAsync_StaleGrantId_LeavesActiveGrantUntouched()
    {
        // Two grants: we revoke only the first one.
        var (context, grant1) = await CreateDbWithGrantAsync();

        var grant2 = new InstancePermission
        {
            Id = Guid.NewGuid(),
            SubjectId = Guid.Parse("66666666-6666-6666-6666-666666666668"),
            PermissionId = Guid.Parse("44444444-4444-4444-4444-444444444445"), // write
            ResourceInstanceId = context.ResourceInstances.First().Id,
            GrantedAt = DateTimeOffset.UtcNow.AddMinutes(-5)
        };
        context.InstancePermissions.Add(grant2);
        await context.SaveChangesAsync();

        var publisher = new RbacEventPublisher(context);
        var sut = new GrantService(context, publisher, Mock.Of<ILogger<GrantService>>());

        await sut.RevokeAsync(grant1.Id, "admin-user");

        // grant1 is gone
        var remaining1 = await context.InstancePermissions.FindAsync(grant1.Id);
        remaining1.Should().BeNull();

        // grant2 is untouched
        var remaining2 = await context.InstancePermissions.FindAsync(grant2.Id);
        remaining2.Should().NotBeNull();

        // Only one outbox row for grant1
        var entries = await context.Outbox.ToListAsync();
        entries.Should().HaveCount(1);
        entries[0].Subject.Should().Contain(grant1.Id.ToString());
    }

    // -----------------------------------------------------------------
    // Expiry sweep — happy path
    // -----------------------------------------------------------------

    [Fact]
    public async Task SweepExpiredGrantsAsync_ExpiredGrant_RemovesAndStagesGrantExpiredRow()
    {
        var pastExpiry = DateTimeOffset.UtcNow.AddMinutes(-5);
        var (context, grant) = await CreateDbWithGrantAsync(expiresAt: pastExpiry);
        var publisher = new RbacEventPublisher(context);
        var sut = new GrantService(context, publisher, Mock.Of<ILogger<GrantService>>());

        var swept = await sut.SweepExpiredGrantsAsync();

        swept.Should().Be(1);

        // Grant must be removed
        var remaining = await context.InstancePermissions.FindAsync(grant.Id);
        remaining.Should().BeNull();

        // Outbox row on the correct subject
        var outboxEntry = await context.Outbox.SingleAsync();
        outboxEntry.Subject.Should().Be($"andy.rbac.events.grant.{grant.Id}.expired");
        outboxEntry.PayloadType.Should().Be(typeof(GrantExpired).FullName);
        outboxEntry.PublishedAt.Should().BeNull();
    }

    [Fact]
    public async Task SweepExpiredGrantsAsync_OutboxRow_CarriesGrantIdPrincipalAndExpiredAt()
    {
        var pastExpiry = DateTimeOffset.UtcNow.AddMinutes(-5);
        var (context, grant) = await CreateDbWithGrantAsync(expiresAt: pastExpiry);
        var publisher = new RbacEventPublisher(context);
        var sut = new GrantService(context, publisher, Mock.Of<ILogger<GrantService>>());

        await sut.SweepExpiredGrantsAsync();

        var outboxEntry = await context.Outbox.SingleAsync();
        outboxEntry.PayloadJson.Should().Contain("\"grant_id\"");
        outboxEntry.PayloadJson.Should().Contain("\"principal\"");
        outboxEntry.PayloadJson.Should().Contain("\"expired_at\"");
        outboxEntry.PayloadJson.Should().Contain("viewer-user");
        outboxEntry.PayloadJson.Should().Contain("test-app:document:read");
        outboxEntry.PayloadJson.Should().Contain("doc-sm211");
    }

    [Fact]
    public async Task SweepExpiredGrantsAsync_FutureGrant_NotSwept()
    {
        var futureExpiry = DateTimeOffset.UtcNow.AddHours(1);
        var (context, grant) = await CreateDbWithGrantAsync(expiresAt: futureExpiry);
        var publisher = new RbacEventPublisher(context);
        var sut = new GrantService(context, publisher, Mock.Of<ILogger<GrantService>>());

        var swept = await sut.SweepExpiredGrantsAsync();

        swept.Should().Be(0);
        context.Outbox.Should().BeEmpty();

        // Grant is still present
        var remaining = await context.InstancePermissions.FindAsync(grant.Id);
        remaining.Should().NotBeNull();
    }

    [Fact]
    public async Task SweepExpiredGrantsAsync_NoExpirySet_NotSwept()
    {
        var (context, grant) = await CreateDbWithGrantAsync(expiresAt: null);
        var publisher = new RbacEventPublisher(context);
        var sut = new GrantService(context, publisher, Mock.Of<ILogger<GrantService>>());

        var swept = await sut.SweepExpiredGrantsAsync();

        swept.Should().Be(0);
        context.Outbox.Should().BeEmpty();
        var remaining = await context.InstancePermissions.FindAsync(grant.Id);
        remaining.Should().NotBeNull();
    }

    [Fact]
    public async Task SweepExpiredGrantsAsync_MultipleExpired_EmitsOneRowPerGrant()
    {
        var context = await TestDbContextFactory.CreateWithSeedDataAsync();
        var viewerSubjectId = Guid.Parse("66666666-6666-6666-6666-666666666668");
        var readPermId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var writePermId = Guid.Parse("44444444-4444-4444-4444-444444444445");

        var resourceInstance = new ResourceInstance
        {
            Id = Guid.NewGuid(),
            ResourceTypeId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
            ExternalId = "doc-multi-sm211",
            DisplayName = "Multi sweep test"
        };
        context.ResourceInstances.Add(resourceInstance);

        var grant1 = new InstancePermission
        {
            Id = Guid.NewGuid(),
            SubjectId = viewerSubjectId,
            PermissionId = readPermId,
            ResourceInstanceId = resourceInstance.Id,
            GrantedAt = DateTimeOffset.UtcNow.AddHours(-2),
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-30)
        };
        var grant2 = new InstancePermission
        {
            Id = Guid.NewGuid(),
            SubjectId = viewerSubjectId,
            PermissionId = writePermId,
            ResourceInstanceId = resourceInstance.Id,
            GrantedAt = DateTimeOffset.UtcNow.AddHours(-1),
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-10)
        };
        context.InstancePermissions.AddRange(grant1, grant2);
        await context.SaveChangesAsync();

        var publisher = new RbacEventPublisher(context);
        var sut = new GrantService(context, publisher, Mock.Of<ILogger<GrantService>>());

        var swept = await sut.SweepExpiredGrantsAsync();

        swept.Should().Be(2);
        var outboxEntries = await context.Outbox.OrderBy(e => e.Subject).ToListAsync();
        outboxEntries.Should().HaveCount(2);
        outboxEntries.Should().OnlyContain(e => e.Subject.EndsWith(".expired"));
        outboxEntries.Select(e => e.Subject)
            .Should().Contain(e => e.Contains(grant1.Id.ToString()))
            .And.Contain(e => e.Contains(grant2.Id.ToString()));
    }

    // -----------------------------------------------------------------
    // Outbox row uses canonical EventJson options (snake_case)
    // -----------------------------------------------------------------

    [Fact]
    public async Task RevokeAsync_OutboxRow_UsesSnakeCaseJson()
    {
        var (context, grant) = await CreateDbWithGrantAsync();
        var publisher = new RbacEventPublisher(context);
        var sut = new GrantService(context, publisher, Mock.Of<ILogger<GrantService>>());

        await sut.RevokeAsync(grant.Id, null);

        var outboxEntry = await context.Outbox.SingleAsync();
        outboxEntry.PayloadJson.Should().Contain("\"grant_id\"");
        outboxEntry.PayloadJson.Should().Contain("\"revoked_at\"");
        outboxEntry.PayloadJson.Should().Contain("\"scope_resource_instance_id\"");
        outboxEntry.PayloadJson.Should().NotContain("\"GrantId\"");
        outboxEntry.PayloadJson.Should().NotContain("\"revokedAt\"");
    }

    [Fact]
    public async Task SweepExpiredGrantsAsync_OutboxRow_UsesSnakeCaseJson()
    {
        var (context, _) = await CreateDbWithGrantAsync(expiresAt: DateTimeOffset.UtcNow.AddMinutes(-1));
        var publisher = new RbacEventPublisher(context);
        var sut = new GrantService(context, publisher, Mock.Of<ILogger<GrantService>>());

        await sut.SweepExpiredGrantsAsync();

        var outboxEntry = await context.Outbox.SingleAsync();
        outboxEntry.PayloadJson.Should().Contain("\"grant_id\"");
        outboxEntry.PayloadJson.Should().Contain("\"expired_at\"");
        outboxEntry.PayloadJson.Should().Contain("\"occurred_at\"");
        outboxEntry.PayloadJson.Should().NotContain("\"GrantId\"");
        outboxEntry.PayloadJson.Should().NotContain("\"expiredAt\"");
    }
}
