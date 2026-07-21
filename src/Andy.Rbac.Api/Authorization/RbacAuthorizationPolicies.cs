using System.Security.Claims;

namespace Andy.Rbac.Api.Authorization;

public static class RbacAuthorizationPolicies
{
    public const string Administrator = "RbacAdministrator";

    public static bool IsAdministrator(ClaimsPrincipal user)
    {
        if (user.IsInRole("super-admin") || user.IsInRole("admin"))
            return true;

        return user.Claims
            .Where(c => c.Type is "role" or "roles" or ClaimTypes.Role)
            .SelectMany(c => c.Value.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries))
            .Any(value => value.Equals("super-admin", StringComparison.OrdinalIgnoreCase)
                || value.Equals("admin", StringComparison.OrdinalIgnoreCase));
    }
}
