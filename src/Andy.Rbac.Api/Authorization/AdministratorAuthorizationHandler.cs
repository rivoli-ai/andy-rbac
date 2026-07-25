using Microsoft.AspNetCore.Authorization;

namespace Andy.Rbac.Api.Authorization;

/// <summary>
/// Requirement backing <see cref="RbacAuthorizationPolicies.Administrator"/>.
/// </summary>
public sealed class AdministratorRequirement : IAuthorizationRequirement;

/// <summary>
/// Resolves <see cref="AdministratorRequirement"/> through
/// <see cref="IAdministratorAuthority"/>, so the policy consults andy-rbac's
/// own role store rather than trusting an unscoped token claim.
///
/// A handler is needed rather than the previous <c>RequireAssertion</c> because
/// the check is now asynchronous and needs scoped services.
/// </summary>
public sealed class AdministratorAuthorizationHandler
    : AuthorizationHandler<AdministratorRequirement>
{
    private readonly IAdministratorAuthority _authority;

    public AdministratorAuthorizationHandler(IAdministratorAuthority authority)
    {
        _authority = authority;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        AdministratorRequirement requirement)
    {
        if (await _authority.IsAdministratorAsync(context.User))
            context.Succeed(requirement);
    }
}
