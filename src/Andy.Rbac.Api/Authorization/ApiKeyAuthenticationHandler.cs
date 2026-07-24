using System.Security.Claims;
using System.Text.Encodings.Web;
using Andy.Rbac.Infrastructure.Data;
using Andy.Rbac.Infrastructure.Repositories;
using Andy.Rbac.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Andy.Rbac.Api.Authorization;

/// <summary>
/// Authenticates a request presenting an <c>X-API-Key</c> header.
///
/// The CLI has always sent this header, but no server-side scheme ever read it
/// — every command 401'd — while the <see cref="ApiKey"/> table sat mapped and
/// migrated but entirely unused. This handler closes that gap.
///
/// A valid key authenticates as its owning <see cref="Subject"/>: the principal
/// carries the same <c>sub</c> and <c>provider</c> claims a bearer token would,
/// so every downstream check (<see cref="TrustedCallerIdentity"/>,
/// <c>EnsureSubjectMiddleware</c>, the permission evaluator) behaves
/// identically no matter how the caller authenticated.
///
/// Role claims are read from the RBAC store rather than from a token, because
/// there is no issuer to assert them. <see cref="ApiKey.Scopes"/>, when
/// non-empty, narrows that set — a key may present fewer roles than its owner
/// holds, never more, so a leaked key is bounded by its declared scope.
/// </summary>
public sealed class ApiKeyAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "ApiKey";
    public const string HeaderName = "X-API-Key";

    /// <summary>Bounds write load from LastUsedAt tracking on hot keys.</summary>
    private static readonly TimeSpan LastUsedRefreshInterval = TimeSpan.FromMinutes(5);

    private readonly RbacDbContext _db;
    private readonly IPermissionRepository _permissions;

    public ApiKeyAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        RbacDbContext db,
        IPermissionRepository permissions)
        : base(options, logger, encoder)
    {
        _db = db;
        _permissions = permissions;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(HeaderName, out var presented))
        {
            // Not our scheme's business — let the bearer path run.
            return AuthenticateResult.NoResult();
        }

        if (!ApiKeySecret.TryParse(presented.ToString(), out var prefix, out var secret))
            return AuthenticateResult.Fail("Malformed API key.");

        var apiKey = await _db.ApiKeys
            .Include(k => k.Subject)
            .Include(k => k.Application)
            .FirstOrDefaultAsync(k => k.KeyPrefix == prefix);

        // Verify the secret even when the prefix is unknown would require a
        // dummy hash; the prefix is not secret, so a miss here leaks nothing
        // beyond "no such key".
        if (apiKey is null)
            return AuthenticateResult.Fail("Unknown API key.");

        if (!ApiKeySecret.Verify(secret, apiKey.KeyHash))
            return AuthenticateResult.Fail("Invalid API key.");

        if (!apiKey.IsActive)
            return AuthenticateResult.Fail("API key has been revoked.");

        if (apiKey.ExpiresAt is { } expiresAt && expiresAt <= DateTimeOffset.UtcNow)
            return AuthenticateResult.Fail("API key has expired.");

        if (apiKey.Subject is null || !apiKey.Subject.IsActive)
            return AuthenticateResult.Fail("API key owner is inactive.");

        var principal = await BuildPrincipalAsync(apiKey);
        await TouchLastUsedAsync(apiKey);

        return AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName));
    }

    private async Task<ClaimsPrincipal> BuildPrincipalAsync(ApiKey apiKey)
    {
        var subject = apiKey.Subject;

        var claims = new List<Claim>
        {
            new("sub", subject.ExternalId),
            new(ClaimTypes.NameIdentifier, subject.ExternalId),
            new("provider", subject.Provider),
            new("api_key_id", apiKey.Id.ToString()),
        };

        if (!string.IsNullOrEmpty(subject.Email))
            claims.Add(new Claim("email", subject.Email));

        // Roles come from the store; a key cannot assert its own authority.
        var roles = await _permissions.GetRolesForSubjectAsync(
            subject.Id, apiKey.Application?.Code);

        // An empty Scopes list means "everything the owner has". A non-empty
        // one is an intersection, never a union — the key is a restriction.
        if (apiKey.Scopes.Count > 0)
        {
            var allowed = apiKey.Scopes.ToHashSet(StringComparer.OrdinalIgnoreCase);
            roles = roles.Where(allowed.Contains).ToList();
        }

        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));

        return new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
    }

    private async Task TouchLastUsedAsync(ApiKey apiKey)
    {
        var now = DateTimeOffset.UtcNow;
        if (apiKey.LastUsedAt is { } lastUsed && now - lastUsed < LastUsedRefreshInterval)
            return;

        apiKey.LastUsedAt = now;
        apiKey.LastUsedIp = Context.Connection.RemoteIpAddress?.ToString();

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException ex)
        {
            // Usage telemetry must never cost a caller its request.
            Logger.LogWarning(ex, "Failed to record API key usage for {KeyPrefix}", apiKey.KeyPrefix);
        }
    }
}
