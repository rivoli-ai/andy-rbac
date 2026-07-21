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
            var encoded = policyName[PermissionPrefix.Length..];
            var segments = encoded.Split('|');
            var permission = segments[0];
            string? resource = null;
            string? bodyPath = null;
            var fromBody = false;
            foreach (var segment in segments.Skip(1))
            {
                var pair = segment.Split('=', 2);
                if (pair.Length != 2) continue;
                if (pair[0] == "resource" && pair[1].Length > 0) resource = Uri.UnescapeDataString(pair[1]);
                if (pair[0] == "path" && pair[1].Length > 0) bodyPath = Uri.UnescapeDataString(pair[1]);
                if (pair[0] == "body") bool.TryParse(pair[1], out fromBody);
            }
            return new AuthorizationPolicyBuilder()
                .AddRequirements(new PermissionRequirement(permission, resource, fromBody, bodyPath))
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
