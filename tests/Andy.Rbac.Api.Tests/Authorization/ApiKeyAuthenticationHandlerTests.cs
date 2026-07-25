using System.Security.Claims;
using System.Text.Encodings.Web;
using Andy.Rbac.Api.Authorization;
using Andy.Rbac.Infrastructure.Data;
using Andy.Rbac.Infrastructure.Repositories;
using Andy.Rbac.Models;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Andy.Rbac.Api.Tests.Authorization;

/// <summary>
/// Authentication via the <c>X-API-Key</c> header.
///
/// The CLI has always sent this header while no server-side scheme read it, so
/// every command 401'd and the ApiKey table went unused. These tests pin the
/// credential's behaviour: it authenticates as its owning subject, carries only
/// roles read from the store (never self-asserted), and stops working the
/// moment it is revoked, expires, or its owner is deactivated.
/// </summary>
public class ApiKeyAuthenticationHandlerTests
{
    private static readonly Guid AdminSubjectId = Guid.Parse("66666666-6666-6666-6666-666666666666");

    private sealed class StubOptionsMonitor : IOptionsMonitor<AuthenticationSchemeOptions>
    {
        private readonly AuthenticationSchemeOptions _options = new();
        public AuthenticationSchemeOptions CurrentValue => _options;
        public AuthenticationSchemeOptions Get(string? name) => _options;
        public IDisposable? OnChange(Action<AuthenticationSchemeOptions, string?> listener) => null;
    }

    /// <summary>Runs the handler against a request carrying the given header value.</summary>
    private static async Task<AuthenticateResult> AuthenticateAsync(
        RbacDbContext db, string? headerValue)
    {
        var handler = new ApiKeyAuthenticationHandler(
            new StubOptionsMonitor(),
            NullLoggerFactory.Instance,
            UrlEncoder.Default,
            db,
            new PermissionRepository(db));

        var context = new DefaultHttpContext();
        if (headerValue is not null)
            context.Request.Headers[ApiKeyAuthenticationHandler.HeaderName] = headerValue;

        await handler.InitializeAsync(
            new AuthenticationScheme(
                ApiKeyAuthenticationHandler.SchemeName, null, typeof(ApiKeyAuthenticationHandler)),
            context);

        return await handler.AuthenticateAsync();
    }

    /// <summary>Mints a key for the seeded admin-user subject.</summary>
    private static async Task<(RbacDbContext db, string plaintext, ApiKey stored)> CreateDbWithKeyAsync(
        Action<ApiKey>? customize = null)
    {
        var db = await TestDbContextFactory.CreateWithSeedDataAsync();
        var generated = ApiKeySecret.Generate();

        var apiKey = new ApiKey
        {
            Id = Guid.NewGuid(),
            Name = "CLI key",
            KeyHash = generated.Hash,
            KeyPrefix = generated.Prefix,
            SubjectId = AdminSubjectId,
            IsActive = true
        };
        customize?.Invoke(apiKey);

        db.ApiKeys.Add(apiKey);
        await db.SaveChangesAsync();

        return (db, generated.PlaintextKey, apiKey);
    }

    // ---- happy path -------------------------------------------------------

    [Fact]
    public async Task ValidKey_AuthenticatesAsOwningSubject()
    {
        var (db, plaintext, _) = await CreateDbWithKeyAsync();
        using var _db = db;

        var result = await AuthenticateAsync(db, plaintext);

        result.Succeeded.Should().BeTrue();
        result.Principal!.FindFirst("sub")!.Value.Should().Be("admin-user");
        result.Principal.FindFirst("provider")!.Value.Should().Be("test-provider");
    }

    [Fact]
    public async Task ValidKey_CarriesRolesFromTheStore()
    {
        var (db, plaintext, _) = await CreateDbWithKeyAsync();
        using var _db = db;

        var result = await AuthenticateAsync(db, plaintext);

        result.Principal!.FindAll(ClaimTypes.Role).Select(c => c.Value)
            .Should().Contain("admin",
                "a key has no issuer to assert roles, so they are read from the RBAC store");
    }

    [Fact]
    public async Task Scopes_NarrowTheRolesPresented_NeverWiden()
    {
        var (db, plaintext, _) = await CreateDbWithKeyAsync(
            key => key.Scopes = ["some-role-the-owner-does-not-hold"]);
        using var _db = db;

        var result = await AuthenticateAsync(db, plaintext);

        result.Succeeded.Should().BeTrue();
        result.Principal!.FindAll(ClaimTypes.Role).Should().BeEmpty(
            "Scopes intersect the owner's roles; they cannot grant new ones");
    }

    [Fact]
    public async Task ValidKey_RecordsLastUsed()
    {
        var (db, plaintext, stored) = await CreateDbWithKeyAsync();
        using var _db = db;

        await AuthenticateAsync(db, plaintext);

        (await db.ApiKeys.FindAsync(stored.Id))!.LastUsedAt.Should().NotBeNull();
    }

    // ---- rejection paths --------------------------------------------------

    [Fact]
    public async Task NoHeader_ReturnsNoResult_SoBearerCanRun()
    {
        var (db, _, _) = await CreateDbWithKeyAsync();
        using var _db = db;

        var result = await AuthenticateAsync(db, headerValue: null);

        result.None.Should().BeTrue();
        result.Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task RevokedKey_IsRejected()
    {
        var (db, plaintext, _) = await CreateDbWithKeyAsync(key => key.IsActive = false);
        using var _db = db;

        (await AuthenticateAsync(db, plaintext)).Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task ExpiredKey_IsRejected()
    {
        var (db, plaintext, _) = await CreateDbWithKeyAsync(
            key => key.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1));
        using var _db = db;

        (await AuthenticateAsync(db, plaintext)).Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task KeyForDeactivatedSubject_IsRejected()
    {
        var (db, plaintext, _) = await CreateDbWithKeyAsync();
        using var _db = db;
        var subject = await db.Subjects.FindAsync(AdminSubjectId);
        subject!.IsActive = false;
        await db.SaveChangesAsync();

        (await AuthenticateAsync(db, plaintext)).Succeeded.Should().BeFalse(
            "deactivating a subject must revoke every credential acting as it");
    }

    [Fact]
    public async Task WrongSecretForKnownPrefix_IsRejected()
    {
        var (db, _, stored) = await CreateDbWithKeyAsync();
        using var _db = db;

        var forged = $"{stored.KeyPrefix}.{ApiKeySecret.Generate().Secret}";

        (await AuthenticateAsync(db, forged)).Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task UnknownPrefix_IsRejected()
    {
        var (db, _, _) = await CreateDbWithKeyAsync();
        using var _db = db;

        (await AuthenticateAsync(db, ApiKeySecret.Generate().PlaintextKey))
            .Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task MalformedHeader_IsRejected()
    {
        var (db, _, _) = await CreateDbWithKeyAsync();
        using var _db = db;

        (await AuthenticateAsync(db, "not-a-key")).Succeeded.Should().BeFalse();
    }
}
