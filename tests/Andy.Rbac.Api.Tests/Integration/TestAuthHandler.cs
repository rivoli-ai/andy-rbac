using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Andy.Rbac.Api.Tests.Integration;

/// <summary>
/// Test authentication handler — every request authenticates as a fixed
/// in-memory test user. The integration suite exercises domain logic, not
/// auth flows; the per-test auth state can be customized via headers if a
/// test ever needs to.
/// </summary>
public class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "Test";
    public const string TestSub = "test-subject";
    public const string TestEmail = "test@example.com";
    public const string TestName = "Test User";

    public TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var claims = new[]
        {
            new Claim("sub", TestSub),
            new Claim(ClaimTypes.NameIdentifier, TestSub),
            new Claim("email", TestEmail),
            new Claim(ClaimTypes.Email, TestEmail),
            new Claim("name", TestName),
        };
        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
