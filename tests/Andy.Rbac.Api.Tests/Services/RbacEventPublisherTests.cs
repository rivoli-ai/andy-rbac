using Andy.Rbac.Api.Services;
using Andy.Rbac.Infrastructure.Messaging;
using Andy.Rbac.Messaging;
using Andy.Rbac.Messaging.Events;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Andy.Rbac.Api.Tests.Services;

// AL2 + AL3 + AL4 — verifies that lifecycle events from RoleService land
// on the OutboxEntry table inside the same transaction as the domain
// row. The OutboxDispatcher (a separate hosted worker) is responsible
// for actually pushing to NATS — those tests live alongside the
// dispatcher.
public class RbacEventPublisherTests
{
    [Fact]
    public async Task RoleCreated_writes_outbox_row_with_correct_subject()
    {
        using var context = await TestDbContextFactory.CreateWithSeedDataAsync();
        var publisher = new RbacEventPublisher(context);
        var service = new RoleService(context, Mock.Of<ILogger<RoleService>>(), publisher);

        var result = await service.CreateAsync(new CreateRoleRequest(
            Code: "writer",
            Name: "Writer",
            Description: null,
            ApplicationCode: "test-app"));

        var entry = await context.Outbox.SingleAsync();
        entry.Subject.Should().Be($"andy.rbac.events.role.{result.Role.Id}.created");
        entry.PayloadType.Should().Be(typeof(RoleCreated).FullName);
        entry.Generation.Should().Be(0);
        entry.PublishedAt.Should().BeNull();
        entry.PayloadJson.Should().Contain("\"writer\"");
        entry.PayloadJson.Should().Contain("\"test-app\"");
    }

    [Fact]
    public async Task RoleDeleted_writes_outbox_row_with_correct_subject()
    {
        using var context = await TestDbContextFactory.CreateWithSeedDataAsync();
        var publisher = new RbacEventPublisher(context);
        var service = new RoleService(context, Mock.Of<ILogger<RoleService>>(), publisher);
        var created = await service.CreateAsync(new CreateRoleRequest(
            Code: "doomed",
            Name: "Doomed",
            Description: null,
            ApplicationCode: "test-app"));

        var deleted = await service.DeleteAsync(created.Role.Id);

        deleted.Should().BeTrue();
        var entries = await context.Outbox.OrderBy(e => e.CreatedAt).ToListAsync();
        entries.Should().HaveCount(2);
        entries[1].Subject.Should().Be($"andy.rbac.events.role.{created.Role.Id}.deleted");
        entries[1].PayloadType.Should().Be(typeof(RoleDeleted).FullName);
    }

    [Fact]
    public async Task RoleAssigned_writes_outbox_row_with_assignment_id()
    {
        using var context = await TestDbContextFactory.CreateWithSeedDataAsync();
        var publisher = new RbacEventPublisher(context);
        var service = new RoleService(context, Mock.Of<ILogger<RoleService>>(), publisher);
        // editor role + admin-user already in seed (TestDbContextFactory).

        var msg = await service.AssignToSubjectAsync(
            subjectExternalId: "no-role-user",
            roleCode: "viewer");

        msg.Should().StartWith("Successfully assigned");
        var entry = await context.Outbox
            .Where(e => e.Subject.Contains(".subject_role."))
            .SingleAsync();
        entry.Subject.Should().EndWith(".granted");
        entry.PayloadType.Should().Be(typeof(RoleAssigned).FullName);
    }

    [Fact]
    public async Task RoleRevoked_writes_outbox_row()
    {
        using var context = await TestDbContextFactory.CreateWithSeedDataAsync();
        var publisher = new RbacEventPublisher(context);
        var service = new RoleService(context, Mock.Of<ILogger<RoleService>>(), publisher);
        await service.AssignToSubjectAsync("no-role-user", "viewer");

        var msg = await service.RevokeFromSubjectAsync("no-role-user", "viewer");

        msg.Should().StartWith("Successfully revoked");
        var entries = await context.Outbox
            .Where(e => e.Subject.Contains(".subject_role."))
            .OrderBy(e => e.CreatedAt)
            .ToListAsync();
        entries.Should().HaveCount(2);
        entries[1].Subject.Should().EndWith(".revoked");
        entries[1].PayloadType.Should().Be(typeof(RoleRevoked).FullName);
    }

    [Fact]
    public async Task Outbox_row_uses_canonical_event_json_options_snake_case()
    {
        using var context = await TestDbContextFactory.CreateWithSeedDataAsync();
        var publisher = new RbacEventPublisher(context);
        var service = new RoleService(context, Mock.Of<ILogger<RoleService>>(), publisher);

        var result = await service.CreateAsync(new CreateRoleRequest(
            Code: "naming",
            Name: "Naming",
            Description: null,
            ApplicationCode: "test-app"));

        var entry = await context.Outbox.SingleAsync();
        // Snake case per EventJson.Options — application_code / parent_role_code / occurred_at.
        entry.PayloadJson.Should().Contain("\"role_id\"");
        entry.PayloadJson.Should().Contain("\"application_code\"");
        entry.PayloadJson.Should().Contain("\"is_system\"");
        entry.PayloadJson.Should().NotContain("\"RoleId\"");
        entry.PayloadJson.Should().NotContain("\"applicationCode\"");
        result.Role.Code.Should().Be("naming");
    }

    [Fact]
    public void PolicyCreated_throws_until_Epic_V_lands()
    {
        // Sanity check on the AL4 stub: calling the Policy.* helpers must
        // fail loudly rather than silently no-op so accidental wiring
        // shows up immediately in tests.
        var publisher = new RbacEventPublisher(null!); // ctor doesn't dereference
        var act = () => publisher.PolicyCreated(new PolicyCreated(
            PolicyId: Guid.NewGuid(),
            Code: "test",
            ApplicationCode: null,
            OccurredAt: DateTimeOffset.UtcNow));
        act.Should().Throw<NotImplementedException>();
    }

    [Fact]
    public void Headers_starting_above_max_generation_drop_silently()
    {
        // Defense-in-depth: should be unreachable in rbac (publisher-only
        // service starts every chain at generation 0), but if we ever
        // wire event-driven reactions, exceeding MaxGeneration must NOT
        // append a runaway row to the outbox.
        var options = new DbContextOptionsBuilder<Andy.Rbac.Infrastructure.Data.RbacDbContext>()
            .UseInMemoryDatabase($"db-{Guid.NewGuid()}")
            .Options;
        using var ctx = new Andy.Rbac.Infrastructure.Data.RbacDbContext(options);
        var publisher = new RbacEventPublisher(ctx);
        var overflowed = new MessageHeaders(
            MsgId: Guid.NewGuid(),
            CorrelationId: Guid.NewGuid(),
            CausationId: Guid.NewGuid(),
            Generation: MessageHeaders.MaxGeneration + 1);

        publisher.RoleCreated(new RoleCreated(
            RoleId: Guid.NewGuid(),
            Code: "x",
            Name: "X",
            ApplicationCode: null,
            ParentRoleCode: null,
            IsSystem: false,
            OccurredAt: DateTimeOffset.UtcNow), overflowed);

        ctx.Outbox.Should().BeEmpty();
    }
}
