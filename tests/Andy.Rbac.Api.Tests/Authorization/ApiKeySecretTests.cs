using Andy.Rbac.Api.Authorization;
using FluentAssertions;
using Xunit;

namespace Andy.Rbac.Api.Tests.Authorization;

/// <summary>
/// Key material generation, formatting and verification.
/// </summary>
public class ApiKeySecretTests
{
    [Fact]
    public void Generate_ProducesRoundTrippableKey()
    {
        var generated = ApiKeySecret.Generate();

        ApiKeySecret.TryParse(generated.PlaintextKey, out var prefix, out var secret)
            .Should().BeTrue();
        prefix.Should().Be(generated.Prefix);
        secret.Should().Be(generated.Secret);
        ApiKeySecret.Verify(secret, generated.Hash).Should().BeTrue();
    }

    [Fact]
    public void Generate_ProducesDistinctKeysAndPrefixes()
    {
        var keys = Enumerable.Range(0, 100).Select(_ => ApiKeySecret.Generate()).ToList();

        keys.Select(k => k.Prefix).Distinct().Should().HaveCount(100);
        keys.Select(k => k.Secret).Distinct().Should().HaveCount(100);
    }

    [Fact]
    public void Generate_PrefixIsIdentifiableAndSecretIsNotStored()
    {
        var generated = ApiKeySecret.Generate();

        generated.Prefix.Should().StartWith(ApiKeySecret.LivePrefix);
        generated.Hash.Should().NotContain(generated.Secret,
            "only a hash of the secret may be persisted");
        generated.PlaintextKey.Should().Be($"{generated.Prefix}.{generated.Secret}");
    }

    [Fact]
    public void Verify_RejectsWrongSecret()
    {
        var generated = ApiKeySecret.Generate();
        var other = ApiKeySecret.Generate();

        ApiKeySecret.Verify(other.Secret, generated.Hash).Should().BeFalse();
    }

    [Fact]
    public void Verify_RejectsEmptyStoredHash()
    {
        ApiKeySecret.Verify("anything", string.Empty).Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("no-separator")]
    [InlineData(".leading-separator")]
    [InlineData("rbac_live_abc.")]           // empty secret
    [InlineData("wrong_prefix_abc.secret")]  // not ours
    public void TryParse_RejectsMalformedKeys(string? presented)
    {
        ApiKeySecret.TryParse(presented, out _, out _).Should().BeFalse();
    }

    [Fact]
    public void TryParse_SplitsOnLastSeparator()
    {
        // The prefix contains underscores; only the final '.' separates secret.
        ApiKeySecret.TryParse("rbac_live_abc.def.ghi", out var prefix, out var secret)
            .Should().BeTrue();
        prefix.Should().Be("rbac_live_abc.def");
        secret.Should().Be("ghi");
    }

    [Fact]
    public void GeneratedKey_IsUrlAndHeaderSafe()
    {
        // base64url only — no '+', '/' or '=' that would need quoting in a
        // shell argument, header value or query string.
        foreach (var _ in Enumerable.Range(0, 50))
        {
            var key = ApiKeySecret.Generate().PlaintextKey;
            key.Should().MatchRegex("^[A-Za-z0-9_.-]+$");
        }
    }
}
