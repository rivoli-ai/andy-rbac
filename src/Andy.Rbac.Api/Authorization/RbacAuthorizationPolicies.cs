namespace Andy.Rbac.Api.Authorization;

public static class RbacAuthorizationPolicies
{
    public const string Administrator = "RbacAdministrator";

    // The claim-only IsAdministrator helper that used to live here was the sole
    // determinant of administrator authority, and it was unscoped: any token
    // carrying a generic `admin` role — including one meant for a different
    // application — granted full global RBAC administration. Authority now
    // comes from andy-rbac's own role store via IAdministratorAuthority, with
    // the claim retained only as a configurable bootstrap. See #114.
}
