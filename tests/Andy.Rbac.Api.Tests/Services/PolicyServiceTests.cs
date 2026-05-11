// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Rbac.Api.Services;
using Andy.Rbac.Infrastructure.Messaging;
using Andy.Rbac.Models;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Andy.Rbac.Api.Tests.Services;

public class PolicyServiceTests
{
    private readonly Mock<ILogger<PolicyService>> _loggerMock = new();

    [Fact]
    public async Task CreateAsync_PersistsPolicyAndStagesOutbox()
    {
        using var ctx = TestDbContextFactory.Create();
        var service = new PolicyService(ctx, _loggerMock.Object, new RbacEventPublisher(ctx));

        var result = await service.CreateAsync(new CreatePolicyRequest(
            Code: "tenant-policy",
            Name: "Tenant Policy",
            Criticality: PolicyCriticality.Medium,
            Rules: new Dictionary<string, object> { ["retentionDays"] = 90 },
            Description: "Tenant override"));

        result.Policy.Code.Should().Be("tenant-policy");
        result.Policy.Criticality.Should().Be(PolicyCriticality.Medium);
        result.Policy.IsSystem.Should().BeFalse();

        var stored = await ctx.Policies.FirstAsync(p => p.Code == "tenant-policy");
        stored.Description.Should().Be("Tenant override");

        // AL4: creation with `retentionDays` in Rules emits a paired
        // retention_changed event alongside policy.created so AD4a's
        // consumer can cascade TTLs without re-reading the policy.
        var outbox = await ctx.Outbox.ToListAsync();
        outbox.Should().HaveCount(2);
        outbox.Should().ContainSingle(e => e.Subject.EndsWith(".created"));
        outbox.Should().ContainSingle(e => e.Subject.EndsWith(".retention_changed"));
        var retention = outbox.Single(e => e.Subject.EndsWith(".retention_changed"));
        retention.Subject.Should().StartWith("andy.rbac.events.policy.");
    }

    [Fact]
    public async Task CreateAsync_DuplicateCode_Throws()
    {
        using var ctx = TestDbContextFactory.Create();
        var service = new PolicyService(ctx, _loggerMock.Object, new RbacEventPublisher(ctx));

        await service.CreateAsync(new CreatePolicyRequest("dup", "Dup", PolicyCriticality.Low));

        var act = () => service.CreateAsync(new CreatePolicyRequest("dup", "Dup2", PolicyCriticality.Low));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*already exists*");
    }

    [Fact]
    public async Task UpdateAsync_StagesUpdateOutbox()
    {
        using var ctx = TestDbContextFactory.Create();
        var service = new PolicyService(ctx, _loggerMock.Object, new RbacEventPublisher(ctx));

        var created = await service.CreateAsync(new CreatePolicyRequest("p1", "P1", PolicyCriticality.Low));
        ctx.Outbox.RemoveRange(ctx.Outbox);
        await ctx.SaveChangesAsync();

        var updated = await service.UpdateAsync(created.Policy.Id, new UpdatePolicyRequest(Name: "P1 Renamed"));

        updated.Should().NotBeNull();
        updated!.Policy.Name.Should().Be("P1 Renamed");

        var outbox = await ctx.Outbox.ToListAsync();
        outbox.Should().ContainSingle(e => e.Subject.EndsWith(".updated"));
    }

    [Fact]
    public async Task UpdateAsync_SystemPolicy_Throws()
    {
        using var ctx = TestDbContextFactory.Create();
        ctx.Policies.Add(new Policy
        {
            Id = Guid.NewGuid(),
            Code = "high-risk",
            Name = "High risk",
            Criticality = PolicyCriticality.Critical,
            IsSystem = true,
        });
        await ctx.SaveChangesAsync();
        var service = new PolicyService(ctx, _loggerMock.Object, new RbacEventPublisher(ctx));
        var system = await ctx.Policies.FirstAsync();

        var act = () => service.UpdateAsync(system.Id, new UpdatePolicyRequest(Name: "renamed"));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*system policies*");
    }

    [Fact]
    public async Task DeleteAsync_StagesDeleteOutbox()
    {
        using var ctx = TestDbContextFactory.Create();
        var service = new PolicyService(ctx, _loggerMock.Object, new RbacEventPublisher(ctx));

        var created = await service.CreateAsync(new CreatePolicyRequest("dp", "Dp", PolicyCriticality.Low));
        ctx.Outbox.RemoveRange(ctx.Outbox);
        await ctx.SaveChangesAsync();

        var deleted = await service.DeleteAsync(created.Policy.Id);

        deleted.Should().BeTrue();
        (await ctx.Policies.AnyAsync(p => p.Id == created.Policy.Id)).Should().BeFalse();
        (await ctx.Outbox.ToListAsync()).Should().ContainSingle(e => e.Subject.EndsWith(".deleted"));
    }

    [Fact]
    public async Task DeleteAsync_SystemPolicy_Throws()
    {
        using var ctx = TestDbContextFactory.Create();
        ctx.Policies.Add(new Policy
        {
            Id = Guid.NewGuid(),
            Code = "high-risk",
            Name = "High risk",
            Criticality = PolicyCriticality.Critical,
            IsSystem = true,
        });
        await ctx.SaveChangesAsync();
        var service = new PolicyService(ctx, _loggerMock.Object, new RbacEventPublisher(ctx));
        var system = await ctx.Policies.FirstAsync();

        var act = () => service.DeleteAsync(system.Id);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*system policies*");
    }

    [Fact]
    public async Task GetByCodeAsync_ReturnsPolicy()
    {
        using var ctx = TestDbContextFactory.Create();
        var service = new PolicyService(ctx, _loggerMock.Object, new RbacEventPublisher(ctx));
        await service.CreateAsync(new CreatePolicyRequest("findme", "FindMe", PolicyCriticality.Low));

        var result = await service.GetByCodeAsync("findme");

        result.Should().NotBeNull();
        result!.Policy.Code.Should().Be("findme");
    }

    [Fact]
    public async Task GetAllAsync_ReturnsPoliciesSortedByCode()
    {
        using var ctx = TestDbContextFactory.Create();
        var service = new PolicyService(ctx, _loggerMock.Object, new RbacEventPublisher(ctx));
        await service.CreateAsync(new CreatePolicyRequest("z", "Z", PolicyCriticality.Low));
        await service.CreateAsync(new CreatePolicyRequest("a", "A", PolicyCriticality.Low));

        var result = await service.GetAllAsync();

        result.Policies.Should().HaveCount(2);
        result.Policies[0].Code.Should().Be("a");
        result.Policies[1].Code.Should().Be("z");
    }

    // ---- AL4 RetentionChanged ----------------------------------------

    [Fact]
    public async Task CreateAsync_WithoutRetentionDays_DoesNotEmitRetentionChanged()
    {
        using var ctx = TestDbContextFactory.Create();
        var service = new PolicyService(ctx, _loggerMock.Object, new RbacEventPublisher(ctx));

        await service.CreateAsync(new CreatePolicyRequest(
            Code: "no-retention",
            Name: "No Retention",
            Criticality: PolicyCriticality.Low,
            Rules: new Dictionary<string, object> { ["requirePreGate"] = true }));

        var outbox = await ctx.Outbox.ToListAsync();
        outbox.Should().ContainSingle(e => e.Subject.EndsWith(".created"));
        outbox.Should().NotContain(e => e.Subject.EndsWith(".retention_changed"));
    }

    [Fact]
    public async Task UpdateAsync_RetentionDaysChanged_EmitsRetentionChanged_WithBeforeAndAfter()
    {
        using var ctx = TestDbContextFactory.Create();
        var service = new PolicyService(ctx, _loggerMock.Object, new RbacEventPublisher(ctx));

        var created = await service.CreateAsync(new CreatePolicyRequest(
            Code: "rotate-up",
            Name: "Rotate Up",
            Criticality: PolicyCriticality.Medium,
            Rules: new Dictionary<string, object> { ["retentionDays"] = 30 }));
        ctx.Outbox.RemoveRange(ctx.Outbox);
        await ctx.SaveChangesAsync();

        await service.UpdateAsync(created.Policy.Id, new UpdatePolicyRequest(
            Rules: new Dictionary<string, object> { ["retentionDays"] = 365 }));

        var outbox = await ctx.Outbox.ToListAsync();
        outbox.Should().ContainSingle(e => e.Subject.EndsWith(".retention_changed"));
        var retention = outbox.Single(e => e.Subject.EndsWith(".retention_changed"));
        retention.Subject.Should().Contain(created.Policy.Id.ToString());

        // Payload carries before / after so the consumer can decide
        // direction without re-fetching.
        var payload = outbox.Single(e => e.Subject.EndsWith(".retention_changed")).PayloadJson;
        payload.Should().Contain("\"previous_retention_days\":30");
        payload.Should().Contain("\"new_retention_days\":365");
        payload.Should().Contain("\"change_id\":");
    }

    [Fact]
    public async Task UpdateAsync_RetentionDaysUnchanged_DoesNotEmitRetentionChanged()
    {
        using var ctx = TestDbContextFactory.Create();
        var service = new PolicyService(ctx, _loggerMock.Object, new RbacEventPublisher(ctx));

        var created = await service.CreateAsync(new CreatePolicyRequest(
            Code: "stable",
            Name: "Stable",
            Criticality: PolicyCriticality.Low,
            Rules: new Dictionary<string, object> { ["retentionDays"] = 30 }));
        ctx.Outbox.RemoveRange(ctx.Outbox);
        await ctx.SaveChangesAsync();

        // Same retentionDays — just renaming. No retention event.
        await service.UpdateAsync(created.Policy.Id, new UpdatePolicyRequest(Name: "Stable Renamed"));

        var outbox = await ctx.Outbox.ToListAsync();
        outbox.Should().ContainSingle(e => e.Subject.EndsWith(".updated"));
        outbox.Should().NotContain(e => e.Subject.EndsWith(".retention_changed"));
    }

    [Fact]
    public async Task UpdateAsync_RetentionDaysCleared_EmitsRetentionChanged_WithNullNew()
    {
        using var ctx = TestDbContextFactory.Create();
        var service = new PolicyService(ctx, _loggerMock.Object, new RbacEventPublisher(ctx));

        var created = await service.CreateAsync(new CreatePolicyRequest(
            Code: "clear-it",
            Name: "Clear It",
            Criticality: PolicyCriticality.Low,
            Rules: new Dictionary<string, object> { ["retentionDays"] = 30 }));
        ctx.Outbox.RemoveRange(ctx.Outbox);
        await ctx.SaveChangesAsync();

        // Replace Rules with one that has no retentionDays key.
        await service.UpdateAsync(created.Policy.Id, new UpdatePolicyRequest(
            Rules: new Dictionary<string, object> { ["requirePreGate"] = true }));

        var payload = (await ctx.Outbox.FirstAsync(e => e.Subject.EndsWith(".retention_changed"))).PayloadJson;
        payload.Should().Contain("\"previous_retention_days\":30");
        // EventJson omits null fields on write (WhenWritingNull). The
        // absence of `new_retention_days` is the wire form of "cleared";
        // consumers treat missing the same as null.
        payload.Should().NotContain("\"new_retention_days\"");
    }

    [Fact]
    public async Task UpdateAsync_RetentionDaysDecreased_EmitsEvent_WithoutBlockingPath()
    {
        // AL4 producer side emits decreases the same way as increases.
        // The downstream consumer (rivoli-ai/andy-tasks#74) is responsible
        // for the RetentionDecreaseApproval gate when that ships — andy-rbac
        // simply records the operator's intent on the bus.
        using var ctx = TestDbContextFactory.Create();
        var service = new PolicyService(ctx, _loggerMock.Object, new RbacEventPublisher(ctx));

        var created = await service.CreateAsync(new CreatePolicyRequest(
            Code: "rotate-down",
            Name: "Rotate Down",
            Criticality: PolicyCriticality.Medium,
            Rules: new Dictionary<string, object> { ["retentionDays"] = 2555 }));
        ctx.Outbox.RemoveRange(ctx.Outbox);
        await ctx.SaveChangesAsync();

        await service.UpdateAsync(created.Policy.Id, new UpdatePolicyRequest(
            Rules: new Dictionary<string, object> { ["retentionDays"] = 365 }));

        var payload = (await ctx.Outbox.FirstAsync(e => e.Subject.EndsWith(".retention_changed"))).PayloadJson;
        payload.Should().Contain("\"previous_retention_days\":2555");
        payload.Should().Contain("\"new_retention_days\":365");
    }
}
