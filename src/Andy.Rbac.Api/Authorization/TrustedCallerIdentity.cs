using System.Security.Claims;

namespace Andy.Rbac.Api.Authorization;

/// <summary>
/// Extracts subject identity and groups only from the validated principal.
/// Andy.Auth subjects use the canonical <c>andy-auth</c> provider unless the
/// token explicitly supplies a provider claim.
/// </summary>
public static class TrustedCallerIdentity
{
    public static string? SubjectId(ClaimsPrincipal user) =>
        user.FindFirst("sub")?.Value ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;

    public static string? Provider(ClaimsPrincipal user)
    {
        var provider = user.FindFirst("provider")?.Value
            ?? user.FindFirst("idp")?.Value
            ?? user.FindFirst("identity_provider")?.Value;
        return !string.IsNullOrWhiteSpace(provider)
            ? provider
            : user.Identity?.IsAuthenticated == true ? "andy-auth" : null;
    }

    public static string? EffectiveProvider(
        ClaimsPrincipal user,
        string subjectExternalId,
        string? requestedProvider)
    {
        if (!string.IsNullOrWhiteSpace(requestedProvider))
            return requestedProvider;

        return string.Equals(SubjectId(user), subjectExternalId, StringComparison.Ordinal)
            ? Provider(user)
            : null;
    }

    public static IReadOnlyList<string>? GroupsFor(
        ClaimsPrincipal user,
        string subjectExternalId,
        string? selectedProvider)
    {
        if (!string.Equals(SubjectId(user), subjectExternalId, StringComparison.Ordinal))
            return null;
        if (string.IsNullOrWhiteSpace(selectedProvider) ||
            !string.Equals(Provider(user), selectedProvider, StringComparison.OrdinalIgnoreCase))
            return null;

        var groups = user.FindAll("groups").Select(claim => claim.Value).ToList();
        return groups.Count == 0 ? null : groups;
    }
}
