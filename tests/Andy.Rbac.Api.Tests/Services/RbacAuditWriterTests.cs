using Andy.Rbac.Api.Services;
using Andy.Rbac.Infrastructure.Data;
using Andy.Rbac.Infrastructure.Repositories;
using Andy.Rbac.Models;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Andy.Rbac.Api.Tests.Services;

/// <summary>
/// Issue #124. PermissionEvaluator used to insert an audit row and commit it
/// inline on every check — a write plus its own transaction on the hottest path,
/// multiplied by the permission count on CheckAnyPermission. Records are now
/// queued to a bounded channel and written in batches by a background service.
///
/// The trade is explicit and tested here: nothing is written on the calling
/// thread, an orderly shutdown drains what was accepted, and saturation drops
/// the oldest records rather than slowing requests down.
/// </summary>
public class RbacAuditWriterTests
{
    private static RbacAuditOptions Options(int capacity = 1000, int batchSize = 100) => new()
    {
        Capacity = capacity,
        BatchSize = batchSize,
        FlushInterval = TimeSpan.FromMilliseconds(50)
    };

    private static ChannelRbacAuditSink CreateSink(RbacAuditOptions options) =>
        new(options, NullLogger<ChannelRbacAuditSink>.Instance);

    private static RbacAuditLog Entry(string permission = "test-app:document:read") => new()
    {
        Id = Guid.NewGuid(),
        EventType = AuditEventTypes.PermissionCheck,
        PermissionCode = permission,
        Result = "allowed"
    };

    /// <summary>A scope factory handing out the given context.</summary>
    private static IServiceScopeFactory ScopeFactoryFor(RbacDbContext db)
    {
        var services = new ServiceCollection();
        services.AddSingleton(db);
        return services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }

    private static RbacAuditWriter CreateWriter(ChannelRbacAuditSink sink, RbacDbContext db, RbacAuditOptions options) =>
        new(sink,
            ScopeFactoryFor(db),
            Microsoft.Extensions.Options.Options.Create(options),
            NullLogger<RbacAuditWriter>.Instance);

    // ---- the sink ----------------------------------------------------------

    [Fact]
    public void TryWrite_DoesNotTouchTheDatabase()
    {
        var sink = CreateSink(Options());

        sink.TryWrite(Entry()).Should().BeTrue();

        // Nothing to assert against a context because the sink has none — that
        // is the point. It holds no DbContext and cannot write.
        sink.Reader.Count.Should().Be(1);
    }

    [Fact]
    public void Saturation_DropsOldestRatherThanBlocking()
    {
        var sink = CreateSink(Options(capacity: 10));

        for (var i = 0; i < 50; i++)
            sink.TryWrite(Entry($"app:doc:action-{i}")).Should().BeTrue("writing must never block a request");

        sink.Reader.Count.Should().Be(10, "capacity bounds memory");

        // The survivors are the most recent — those are what an operator
        // investigating a live incident needs.
        var remaining = new List<RbacAuditLog>();
        while (sink.Reader.TryRead(out var entry)) remaining.Add(entry);
        remaining.Select(e => e.PermissionCode).Should().Contain("app:doc:action-49");
        remaining.Select(e => e.PermissionCode).Should().NotContain("app:doc:action-0");
    }

    [Fact]
    public void TryWrite_AfterCompletion_ReportsDrop()
    {
        var sink = CreateSink(Options());
        sink.Complete();

        sink.TryWrite(Entry()).Should().BeFalse();
        sink.DroppedCount.Should().Be(1, "drops are counted, not silent");
    }

    // ---- the writer --------------------------------------------------------

    [Fact]
    public async Task Writer_PersistsQueuedRecords()
    {
        using var db = await TestDbContextFactory.CreateWithSeedDataAsync();
        var options = Options();
        var sink = CreateSink(options);
        var writer = CreateWriter(sink, db, options);

        for (var i = 0; i < 5; i++) sink.TryWrite(Entry());

        await writer.StartAsync(CancellationToken.None);
        await WaitForCountAsync(db, 5);
        await writer.StopAsync(CancellationToken.None);

        (await db.AuditLogs.CountAsync()).Should().Be(5);
    }

    [Fact]
    public async Task Writer_BatchesRatherThanOneRoundTripPerRecord()
    {
        using var db = await TestDbContextFactory.CreateWithSeedDataAsync();
        var options = Options(batchSize: 100);
        var sink = CreateSink(options);
        var writer = CreateWriter(sink, db, options);

        for (var i = 0; i < 250; i++) sink.TryWrite(Entry());

        await writer.StartAsync(CancellationToken.None);
        await WaitForCountAsync(db, 250);
        await writer.StopAsync(CancellationToken.None);

        (await db.AuditLogs.CountAsync()).Should().Be(250);
    }

    [Fact]
    public async Task Shutdown_DrainsWhatWasAlreadyAccepted()
    {
        using var db = await TestDbContextFactory.CreateWithSeedDataAsync();
        var options = new RbacAuditOptions
        {
            Capacity = 1000,
            BatchSize = 100,
            // Long enough that the loop is parked when we stop it, so the
            // final drain is what persists these.
            FlushInterval = TimeSpan.FromSeconds(30)
        };
        var sink = CreateSink(options);
        var writer = CreateWriter(sink, db, options);

        await writer.StartAsync(CancellationToken.None);
        for (var i = 0; i < 20; i++) sink.TryWrite(Entry());

        await writer.StopAsync(CancellationToken.None);

        (await db.AuditLogs.CountAsync()).Should().Be(20,
            "an orderly shutdown must not discard records it already accepted");
    }

    // ---- the evaluator hands off rather than writing ------------------------

    [Fact]
    public async Task PermissionCheck_QueuesAnAuditRecordWithoutWritingInline()
    {
        using var db = await TestDbContextFactory.CreateWithSeedDataAsync();
        var options = Options();
        var sink = CreateSink(options);
        var evaluator = new PermissionEvaluator(
            db, new PermissionRepository(db), NullLogger<PermissionEvaluator>.Instance,
            httpContextAccessor: null, auditSink: sink);

        var result = await evaluator.CheckPermissionAsync("admin-user", "test-app:document:read");

        (await db.AuditLogs.CountAsync()).Should().Be(0,
            "the check path must not write audit rows on the calling thread");
        sink.Reader.Count.Should().Be(1);

        sink.Reader.TryRead(out var entry).Should().BeTrue();
        entry!.PermissionCode.Should().Be("test-app:document:read");
        entry.Result.Should().Be(result.Allowed ? "allowed" : "denied");
        entry.ResourceType.Should().Be("document");
    }

    [Fact]
    public async Task PermissionCheck_WithoutASink_StillSucceeds()
    {
        // Direct unit construction wires no sink; auditing must never be the
        // reason a check behaves differently.
        using var db = await TestDbContextFactory.CreateWithSeedDataAsync();
        var evaluator = new PermissionEvaluator(
            db, new PermissionRepository(db), NullLogger<PermissionEvaluator>.Instance);

        var act = async () => await evaluator.CheckPermissionAsync("admin-user", "test-app:document:read");

        await act.Should().NotThrowAsync();
    }

    private static async Task WaitForCountAsync(RbacDbContext db, int expected)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (await db.AuditLogs.CountAsync() >= expected) return;
            await Task.Delay(25);
        }
    }
}
