using System.Security.Claims;
using Andy.Rbac.Abstractions;
using Andy.Rbac.Configuration;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Andy.Rbac.Client;

/// <summary>
/// Subject accessor that reads from HttpContext.
/// </summary>
public class HttpContextSubjectAccessor : ICurrentSubjectAccessor
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly RbacOptions _options;

    public HttpContextSubjectAccessor(
        IHttpContextAccessor httpContextAccessor,
        IOptions<RbacOptions> options)
    {
        _httpContextAccessor = httpContextAccessor;
        _options = options.Value;
    }

    public bool IsAuthenticated =>
        _httpContextAccessor.HttpContext?.User.Identity?.IsAuthenticated ?? false;

    public string? GetSubjectId()
    {
        return GetClaim(_options.SubjectIdClaimType)
            ?? GetClaim(ClaimTypes.NameIdentifier);
    }

    public string? GetProvider()
    {
        return GetClaim(_options.ProviderClaimType)
            ?? GetClaim("iss");
    }

    public string? GetEmail()
    {
        return GetClaim(ClaimTypes.Email)
            ?? GetClaim("email");
    }

    public string? GetDisplayName()
    {
        return GetClaim(ClaimTypes.Name)
            ?? GetClaim("name")
            ?? GetClaim("preferred_username");
    }

    public IReadOnlyDictionary<string, string> GetClaims()
    {
        var user = _httpContextAccessor.HttpContext?.User;
        if (user == null)
            return new Dictionary<string, string>();

        return user.Claims
            .GroupBy(c => c.Type)
            .ToDictionary(g => g.Key, g => g.First().Value);
    }

    private string? GetClaim(string type)
    {
        return _httpContextAccessor.HttpContext?.User.FindFirst(type)?.Value;
    }
}
