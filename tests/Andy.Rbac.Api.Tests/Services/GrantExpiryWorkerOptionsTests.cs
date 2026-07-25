using Andy.Rbac.Api.Services;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Andy.Rbac.Api.Tests.Services;

/// <summary>
/// Issue #120. The "must be ≥ 5 seconds" minimum was documented but never
/// enforced: a zero interval turned the worker into a tight loop issuing a
/// four-Include InstancePermissions query per iteration, and a negative one
/// threw from inside ExecuteAsync, where it surfaced as a silently dead worker
/// rather than a startup failure.
/// </summary>
public class GrantExpiryWorkerOptionsTests
{
    private static GrantExpiryWorker Create(TimeSpan sweepInterval) =>
        new(Mock.Of<IServiceScopeFactory>(),
            NullLogger<GrantExpiryWorker>.Instance,
            Options.Create(new GrantExpiryWorkerOptions { SweepInterval = sweepInterval }));

    [Fact]
    public void DefaultInterval_IsAccepted()
    {
        var act = () => Create(new GrantExpiryWorkerOptions().SweepInterval);
        act.Should().NotThrow();
    }

    [Fact]
    public void MinimumInterval_IsAccepted()
    {
        var act = () => Create(GrantExpiryWorkerOptions.MinimumSweepInterval);
        act.Should().NotThrow("the bound is inclusive");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(-30)]
    public void IntervalBelowMinimum_ThrowsAtConstruction(int seconds)
    {
        var act = () => Create(TimeSpan.FromSeconds(seconds));

        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithMessage("*SweepInterval*", "the failure must name the setting that is wrong");
    }
}
