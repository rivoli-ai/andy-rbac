using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace Andy.Rbac.Authorization;

/// <summary>
/// Dynamic policy provider that creates policies for permission and role requirements.
/// </summary>
public class RbacPolicyProvider : IAuthorizationPolicyProvider
{
    private const string PermissionPrefix = "Permission:";
    private const string AnyPermissionPrefix = "AnyPermission:";
    private const string RolePrefix = "Role:";

    private readonly DefaultAuthorizationPolicyProvider _fallbackProvider;

    public RbacPolicyProvider(IOptions<AuthorizationOptions> options)
    {
        _fallbackProvider = new DefaultAuthorizationPolicyProvider(options);
    }

    public async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (policyName.StartsWith(PermissionPrefix))
        {
            var permission = policyName[PermissionPrefix.Length..];
            return new AuthorizationPolicyBuilder()
                .AddRequirements(new PermissionRequirement(permission))
                .Build();
        }

        if (policyName.StartsWith(AnyPermissionPrefix))
        {
            var permissions = policyName[AnyPermissionPrefix.Length..].Split(',');
            return new AuthorizationPolicyBuilder()
                .AddRequirements(new AnyPermissionRequirement(permissions))
                .Build();
        }

        if (policyName.StartsWith(RolePrefix))
        {
            var role = policyName[RolePrefix.Length..];
            return new AuthorizationPolicyBuilder()
                .AddRequirements(new RoleRequirement(role))
                .Build();
        }

        return await _fallbackProvider.GetPolicyAsync(policyName);
    }

    public Task<AuthorizationPolicy> GetDefaultPolicyAsync()
    {
        return _fallbackProvider.GetDefaultPolicyAsync();
    }

    public Task<AuthorizationPolicy?> GetFallbackPolicyAsync()
    {
        return _fallbackProvider.GetFallbackPolicyAsync();
    }
}
