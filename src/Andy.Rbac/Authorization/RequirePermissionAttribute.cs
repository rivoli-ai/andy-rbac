using Microsoft.AspNetCore.Authorization;

namespace Andy.Rbac.Authorization;

/// <summary>
/// Requires the user to have a specific permission.
/// Can be applied to controllers or actions.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public class RequirePermissionAttribute : AuthorizeAttribute, IAuthorizationRequirement
{
    /// <summary>
    /// The permission code required (e.g., "andy-docs:document:read").
    /// Can use short form if application code is configured (e.g., "document:read").
    /// </summary>
    public string Permission { get; }

    /// <summary>
    /// Name of the route parameter containing the resource instance ID.
    /// When specified, permission is checked against this specific instance.
    /// </summary>
    public string? ResourceIdParameter { get; set; }

    /// <summary>
    /// Whether the resource ID comes from the request body instead of route parameters.
    /// </summary>
    public bool ResourceIdFromBody { get; set; }

    /// <summary>
    /// Property path within the request body for the resource ID (when ResourceIdFromBody is true).
    /// </summary>
    public string? ResourceIdBodyPath { get; set; }

    public RequirePermissionAttribute(string permission)
    {
        Permission = permission;
        Policy = $"Permission:{permission}";
    }
}

/// <summary>
/// Requires the user to have any of the specified permissions.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public class RequireAnyPermissionAttribute : AuthorizeAttribute, IAuthorizationRequirement
{
    public string[] Permissions { get; }

    public RequireAnyPermissionAttribute(params string[] permissions)
    {
        Permissions = permissions;
        Policy = $"AnyPermission:{string.Join(",", permissions)}";
    }
}

/// <summary>
/// Requires the user to have a specific role.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public class RequireRoleAttribute : AuthorizeAttribute, IAuthorizationRequirement
{
    public string Role { get; }

    public RequireRoleAttribute(string role)
    {
        Role = role;
        Policy = $"Role:{role}";
    }
}
