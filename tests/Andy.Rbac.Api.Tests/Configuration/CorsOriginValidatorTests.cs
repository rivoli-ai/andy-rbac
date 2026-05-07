using Andy.Rbac.Api.Configuration;
using FluentAssertions;
using Xunit;

namespace Andy.Rbac.Api.Tests.Configuration;

/// <summary>
/// Issue #50 — startup-time guard that catches CORS origin misconfigurations
/// that are silently dangerous when the policy also calls AllowCredentials.
/// </summary>
public class CorsOriginValidatorTests
{
    [Fact]
    public void Validate_WithExactOrigins_DoesNotThrow()
    {
        var origins = new[] { "https://app.example.com", "http://localhost:5180" };
        var act = () => CorsOriginValidator.Validate(origins, isDevelopment: false);
        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_WithWildcardOrigin_Throws()
    {
        var origins = new[] { "*" };
        var act = () => CorsOriginValidator.Validate(origins, isDevelopment: false);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*wildcard*");
    }

    [Fact]
    public void Validate_WithSubdomainWildcard_Throws()
    {
        // ASP.NET's WithOrigins doesn't actually support subdomain wildcards,
        // and even if it did, mixing them with AllowCredentials is unsafe.
        var origins = new[] { "https://*.example.com" };
        var act = () => CorsOriginValidator.Validate(origins, isDevelopment: false);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*wildcard*");
    }

    [Fact]
    public void Validate_WithBlankEntry_Throws()
    {
        var origins = new[] { "https://app.example.com", "" };
        var act = () => CorsOriginValidator.Validate(origins, isDevelopment: false);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*blank*");
    }

    [Fact]
    public void Validate_EmptyInProduction_Throws()
    {
        var origins = Array.Empty<string>();
        var act = () => CorsOriginValidator.Validate(origins, isDevelopment: false);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*non-Development*");
    }

    [Fact]
    public void Validate_EmptyInDevelopment_DoesNotThrow()
    {
        // Local dev convenience — Program.cs falls back to a localhost origin
        // when no config is present.
        var origins = Array.Empty<string>();
        var act = () => CorsOriginValidator.Validate(origins, isDevelopment: true);
        act.Should().NotThrow();
    }
}
