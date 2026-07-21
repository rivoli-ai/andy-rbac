using System.Security.Claims;
using Andy.Rbac.Api.Authorization;
using FluentAssertions;
using Xunit;

namespace Andy.Rbac.Api.Tests.Authorization;

public sealed class TrustedCallerIdentityTests
{
    private static ClaimsPrincipal CreateCaller() => new(new ClaimsIdentity(
    [
        new Claim("sub", "shared-id"),
        new Claim("provider", "provider-a"),
        new Claim("groups", "engineering")
    ], "test"));

    [Fact]
    public void GroupsFor_DifferentSelectedProvider_DoesNotReleaseCallerGroups()
    {
        var user = CreateCaller();
        var provider = TrustedCallerIdentity.EffectiveProvider(user, "shared-id", "provider-b");

        provider.Should().Be("provider-b");
        TrustedCallerIdentity.GroupsFor(user, "shared-id", provider).Should().BeNull();
    }

    [Fact]
    public void UnqualifiedSelfCheck_IsPinnedToAuthenticatedProvider()
    {
        var user = CreateCaller();
        var provider = TrustedCallerIdentity.EffectiveProvider(user, "shared-id", null);

        provider.Should().Be("provider-a");
        TrustedCallerIdentity.GroupsFor(user, "shared-id", provider)
            .Should().Equal("engineering");
    }
}
