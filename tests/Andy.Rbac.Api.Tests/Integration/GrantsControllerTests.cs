// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Net;
using Andy.Rbac.Infrastructure.Data;
using Andy.Rbac.Messaging.Events;
using Andy.Rbac.Models;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Andy.Rbac.Api.Tests.Integration;

// SM.2.11 — integration tests for the admin grant revoke endpoint.
// Exercises the full HTTP stack (controller → GrantService → outbox).
//
// Key adversarial cases:
//   - Admin revoke → grant.revoked outbox row staged with grantId+principal
//   - Revoke non-existent grant → 404
//   - Revoke one grant → sibling grant is untouched (stale-id correctness)
//   - Outbox row is NOT yet published (PublishedAt = null) — OutboxDispatcher
//     is wired separately and will deliver it; the controller's job is staging.
public class GrantsControllerTests : IClassFixture<RbacWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly RbacWebApplicationFactory _factory;

    public GrantsControllerTests(RbacWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    // Helper: create an InstancePermission in the live test DB.
    private async Task<Guid> CreateGrantAsync(DateTimeOffset? expiresAt = null)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RbacDbContext>();

        // Ensure resource instance exists
        var resourceInstanceId = Guid.NewGuid();
        var resourceInstance = new ResourceInstance
        {
            Id = resourceInstanceId,
            ResourceTypeId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
            ExternalId = $"doc-{resourceInstanceId:N}",
            DisplayName = "Integration test doc"
        };
        db.ResourceInstances.Add(resourceInstance);

        var grantId = Guid.NewGuid();
        var grant = new InstancePermission
        {
            Id = grantId,
            SubjectId = Guid.Parse("66666666-6666-6666-6666-666666666668"), // viewer-user
            PermissionId = Guid.Parse("44444444-4444-4444-4444-444444444444"), // read
            ResourceInstanceId = resourceInstanceId,
            GrantedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            ExpiresAt = expiresAt
        };
        db.InstancePermissions.Add(grant);
        await db.SaveChangesAsync();
        return grantId;
    }

    private int OutboxCount()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RbacDbContext>();
        return db.Outbox.Count();
    }

    private Andy.Rbac.Messaging.OutboxEntry? LatestOutboxEntry()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RbacDbContext>();
        return db.Outbox.OrderByDescending(e => e.CreatedAt).FirstOrDefault();
    }

    // -----------------------------------------------------------------
    // DELETE /api/grants/{id} — admin revoke
    // -----------------------------------------------------------------

    [Fact]
    public async Task RevokeGrant_ExistingGrant_Returns204AndStagesOutboxRow()
    {
        var grantId = await CreateGrantAsync();

        var response = await _client.DeleteAsync($"/api/grants/{grantId}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var outboxEntry = LatestOutboxEntry();
        outboxEntry.Should().NotBeNull();
        outboxEntry!.Subject.Should().Be($"andy.rbac.events.grant.{grantId}.revoked");
        outboxEntry.PayloadType.Should().Be(typeof(GrantRevoked).FullName);
        outboxEntry.PublishedAt.Should().BeNull(); // OutboxDispatcher delivers asynchronously
    }

    [Fact]
    public async Task RevokeGrant_ExistingGrant_GrantIsRemovedFromDb()
    {
        var grantId = await CreateGrantAsync();

        await _client.DeleteAsync($"/api/grants/{grantId}");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RbacDbContext>();
        var remaining = await db.InstancePermissions.FindAsync(grantId);
        remaining.Should().BeNull();
    }

    [Fact]
    public async Task RevokeGrant_NonExistentGrant_Returns404()
    {
        var response = await _client.DeleteAsync($"/api/grants/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task RevokeGrant_NonExistentGrant_NoOutboxRowStaged()
    {
        var before = OutboxCount();

        await _client.DeleteAsync($"/api/grants/{Guid.NewGuid()}");

        var after = OutboxCount();
        after.Should().Be(before); // no new outbox row
    }

    [Fact]
    public async Task RevokeGrant_OneGrant_SiblingGrantUntouched()
    {
        var grantId1 = await CreateGrantAsync();
        var grantId2 = await CreateGrantAsync();

        await _client.DeleteAsync($"/api/grants/{grantId1}");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RbacDbContext>();
        var grant1 = await db.InstancePermissions.FindAsync(grantId1);
        var grant2 = await db.InstancePermissions.FindAsync(grantId2);

        grant1.Should().BeNull();   // revoked
        grant2.Should().NotBeNull(); // untouched
    }

    [Fact]
    public async Task RevokeGrant_OutboxRow_CarriesGrantIdPrincipalAndPermissionCode()
    {
        var grantId = await CreateGrantAsync();

        await _client.DeleteAsync($"/api/grants/{grantId}");

        var outboxEntry = LatestOutboxEntry();
        outboxEntry.Should().NotBeNull();
        outboxEntry!.PayloadJson.Should().Contain("\"grant_id\"");
        outboxEntry.PayloadJson.Should().Contain("\"principal\"");
        outboxEntry.PayloadJson.Should().Contain("\"permission_code\"");
        outboxEntry.PayloadJson.Should().Contain("viewer-user");
        outboxEntry.PayloadJson.Should().Contain("test-app:document:read");
    }
}
