using Microsoft.AspNetCore.Authorization;

namespace Andy.Rbac.Authorization;

/// <summary>
/// Authorization requirement for a specific permission.
/// </summary>
public class PermissionRequirement : IAuthorizationRequirement
{
    public string Permission { get; }
    public string? ResourceIdParameter { get; }

    public PermissionRequirement(string permission, string? resourceIdParameter = null)
    {
        Permission = permission;
        ResourceIdParameter = resourceIdParameter;
    }
}

/// <summary>
/// Authorization requirement for any of a set of permissions.
/// </summary>
public class AnyPermissionRequirement : IAuthorizationRequirement
{
    public IReadOnlyList<string> Permissions { get; }

    public AnyPermissionRequirement(IEnumerable<string> permissions)
    {
        Permissions = permissions.ToList().AsReadOnly();
    }
}

/// <summary>
/// Authorization requirement for a specific role.
/// </summary>
public class RoleRequirement : IAuthorizationRequirement
{
    public string Role { get; }

    public RoleRequirement(string role)
    {
        Role = role;
    }
}
