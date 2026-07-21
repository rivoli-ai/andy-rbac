using Andy.Rbac.Infrastructure.Data;
using Andy.Rbac.Infrastructure.Messaging;
using Andy.Rbac.Messaging;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Andy.Rbac.Api.Tests.Messaging;

public sealed class OutboxDispatcherTests
{
    [Fact]
    public async Task FailedPublish_BacksOffThenDeadLettersAtConfiguredLimit()
    {
        var databaseName = Guid.NewGuid().ToString();
        var bus = new Mock<IMessageBus>();
        bus.Setup(value => value.PublishAsync(
                It.IsAny<string>(), It.IsAny<object>(), It.IsAny<MessageHeaders>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("poison"));
        var services = new ServiceCollection();
        services.AddDbContext<RbacDbContext>(options => options.UseInMemoryDatabase(databaseName));
        services.AddSingleton(bus.Object);
        await using var provider = services.BuildServiceProvider();

        var entryId = Guid.NewGuid();
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RbacDbContext>();
            db.Outbox.Add(new OutboxEntry
            {
                Id = entryId,
                Subject = "andy.rbac.events.test.1.created",
                PayloadJson = "{}",
                CorrelationId = Guid.NewGuid()
            });
            await db.SaveChangesAsync();
        }

        var dispatcher = new OutboxDispatcher(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Mock.Of<ILogger<OutboxDispatcher>>(),
            Options.Create(new OutboxDispatcherOptions
            {
                MaxAttempts = 2,
                InitialRetryDelay = TimeSpan.FromSeconds(5),
                MaxRetryDelay = TimeSpan.FromSeconds(10)
            }));

        (await dispatcher.DrainOnceAsync(default)).Should().Be(1);
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RbacDbContext>();
            var entry = await db.Outbox.SingleAsync(value => value.Id == entryId);
            entry.AttemptCount.Should().Be(1);
            entry.NextAttemptAt.Should().BeAfter(entry.LastAttemptAt!.Value);
            entry.DeadLetteredAt.Should().BeNull();
            entry.NextAttemptAt = DateTimeOffset.UtcNow.AddSeconds(-1);
            await db.SaveChangesAsync();
        }

        (await dispatcher.DrainOnceAsync(default)).Should().Be(1);
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RbacDbContext>();
            var entry = await db.Outbox.SingleAsync(value => value.Id == entryId);
            entry.AttemptCount.Should().Be(2);
            entry.NextAttemptAt.Should().BeNull();
            entry.DeadLetteredAt.Should().NotBeNull();
        }

        (await dispatcher.DrainOnceAsync(default)).Should().Be(0);
        bus.Verify(value => value.PublishAsync(
            It.IsAny<string>(), It.IsAny<object>(), It.IsAny<MessageHeaders>(),
            It.IsAny<CancellationToken>()), Times.Exactly(2));
    }
}
