using Andy.Rbac.Caching;
using Andy.Rbac.Configuration;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Xunit;

namespace Andy.Rbac.Api.Tests.Caching;

/// <summary>
/// Issue #110. <c>IRbacCache</c> is registered as a singleton and reached from
/// concurrent requests, but its per-subject key index was an unsynchronised
/// <c>HashSet&lt;string&gt;</c>: concurrent writes could corrupt it, and
/// enumerating it during <c>InvalidateAsync</c> while another request wrote
/// threw "collection was modified".
///
/// The index lifetime mattered too — it inherited the TTL of the first entry it
/// tracked, so it could expire while entries were still live, leaving
/// invalidation with nothing to remove and revoked permissions still served.
/// </summary>
public class InMemoryRbacCacheConcurrencyTests
{
    private static InMemoryRbacCache CreateCache(TimeSpan? expiration = null)
    {
        var options = Options.Create(new RbacOptions
        {
            ApplicationCode = "test-app",
            Cache = new RbacCacheOptions { Expiration = expiration ?? TimeSpan.FromMinutes(5) }
        });
        return new InMemoryRbacCache(new MemoryCache(new MemoryCacheOptions()), options);
    }

    [Fact]
    public async Task ConcurrentWritesForOneSubject_DoNotCorruptTheIndex()
    {
        var cache = CreateCache();
        const string subjectId = "user-1";

        // Many distinct (applicationCode, groups) tuples for one subject —
        // every one of them lands in that subject's index.
        await Task.WhenAll(Enumerable.Range(0, 200).Select(i => Task.Run(async () =>
        {
            await cache.SetPermissionsAsync(
                subjectId, [$"app{i}:doc:read"], applicationCode: $"app{i}", groups: [$"g{i}"]);
            await cache.SetRolesAsync(
                subjectId, [$"role{i}"], applicationCode: $"app{i}", groups: [$"g{i}"]);
        })));

        // Every write must be reachable, and invalidation must sweep all of it.
        await cache.InvalidateAsync(subjectId);

        for (var i = 0; i < 200; i++)
        {
            (await cache.GetPermissionsAsync(subjectId, $"app{i}", [$"g{i}"]))
                .Should().BeNull($"permissions for app{i} must be invalidated");
            (await cache.GetRolesAsync(subjectId, $"app{i}", [$"g{i}"]))
                .Should().BeNull($"roles for app{i} must be invalidated");
        }
    }

    [Fact]
    public async Task InvalidateConcurrentWithWrites_DoesNotThrow()
    {
        // The original failure: InvalidateAsync enumerating the HashSet while a
        // request added to it.
        var cache = CreateCache();
        const string subjectId = "user-1";
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        var writer = Task.Run(async () =>
        {
            var i = 0;
            while (!cts.IsCancellationRequested)
            {
                await cache.SetPermissionsAsync(
                    subjectId, ["a:b:c"], applicationCode: $"app{i++}", groups: ["g"]);
            }
        });

        var invalidator = Task.Run(async () =>
        {
            while (!cts.IsCancellationRequested)
                await cache.InvalidateAsync(subjectId);
        });

        var act = async () => await Task.WhenAll(writer, invalidator);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task IndexOutlivesEntriesWrittenAfterIt()
    {
        // The index was Set once, when the first key was tracked, and its TTL
        // was never extended. A key added late therefore rode an index that was
        // nearly expired, and outlived it — leaving a live cache entry that
        // invalidation could no longer find.
        //
        // Timeline with a 400ms TTL:
        //   t=0    write "app-early" -> index created, expires t=400
        //   t=300  write "app-late"  -> old: rides the t=400 index, entry lives to t=700
        //                               new: index re-Set, now expires t=700
        //   t=550  invalidate        -> old: index already gone, removes nothing
        //                               new: index alive, removes both keys
        var cache = CreateCache(TimeSpan.FromMilliseconds(400));
        const string subjectId = "user-1";

        await cache.SetPermissionsAsync(subjectId, ["a:b:c"], applicationCode: "app-early", groups: null);
        await Task.Delay(300);
        await cache.SetPermissionsAsync(subjectId, ["d:e:f"], applicationCode: "app-late", groups: null);

        await Task.Delay(250); // past the un-refreshed index TTL, well inside the entry's
        (await cache.GetPermissionsAsync(subjectId, "app-late"))
            .Should().NotBeNull("precondition: the entry is still live when we invalidate");

        await cache.InvalidateAsync(subjectId);

        (await cache.GetPermissionsAsync(subjectId, "app-late"))
            .Should().BeNull("the index must outlive the newest entry it tracks");
    }

    [Fact]
    public async Task InvalidateOneSubject_LeavesOtherSubjectsIntact()
    {
        var cache = CreateCache();

        await cache.SetPermissionsAsync("user-1", ["a:b:c"], applicationCode: "app");
        await cache.SetPermissionsAsync("user-2", ["a:b:c"], applicationCode: "app");

        await cache.InvalidateAsync("user-1");

        (await cache.GetPermissionsAsync("user-1", "app")).Should().BeNull();
        (await cache.GetPermissionsAsync("user-2", "app")).Should().NotBeNull();
    }

    [Fact]
    public async Task ConcurrentInvalidateAll_IsSafe()
    {
        var cache = CreateCache();

        await Task.WhenAll(Enumerable.Range(0, 100).Select(i => Task.Run(async () =>
        {
            await cache.SetPermissionsAsync($"user-{i % 5}", ["a:b:c"], applicationCode: $"app{i}");
            await cache.InvalidateAllAsync();
        })));

        // Generation bumped past every write — nothing is reachable.
        for (var i = 0; i < 100; i++)
            (await cache.GetPermissionsAsync($"user-{i % 5}", $"app{i}")).Should().BeNull();
    }
}
