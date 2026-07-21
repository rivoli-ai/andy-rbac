using Microsoft.AspNetCore.Authorization;

namespace Andy.Rbac.Authorization;

/// <summary>
/// Requires the user to have a specific permission.
/// Can be applied to controllers or actions.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public class RequirePermissionAttribute : AuthorizeAttribute, IAuthorizationRequirement
{
    private string? _resourceIdParameter;
    private bool _resourceIdFromBody;
    private string? _resourceIdBodyPath;

    /// <summary>
    /// The permission code required (e.g., "andy-docs:document:read").
    /// Can use short form if application code is configured (e.g., "document:read").
    /// </summary>
    public string Permission { get; }

    /// <summary>
    /// Name of the route parameter containing the resource instance ID.
    /// When specified, permission is checked against this specific instance.
    /// </summary>
    public string? ResourceIdParameter
    {
        get => _resourceIdParameter;
        set { _resourceIdParameter = value; UpdatePolicy(); }
    }

    /// <summary>
    /// Whether the resource ID comes from the request body instead of route parameters.
    /// </summary>
    public bool ResourceIdFromBody
    {
        get => _resourceIdFromBody;
        set { _resourceIdFromBody = value; UpdatePolicy(); }
    }

    /// <summary>
    /// Property path within the request body for the resource ID (when ResourceIdFromBody is true).
    /// </summary>
    public string? ResourceIdBodyPath
    {
        get => _resourceIdBodyPath;
        set { _resourceIdBodyPath = value; UpdatePolicy(); }
    }

    public RequirePermissionAttribute(string permission)
    {
        Permission = permission;
        UpdatePolicy();
    }

    private void UpdatePolicy()
    {
        if (string.IsNullOrEmpty(_resourceIdParameter) &&
            !_resourceIdFromBody &&
            string.IsNullOrEmpty(_resourceIdBodyPath))
        {
            Policy = $"Permission:{Permission}";
            return;
        }

        var resource = Uri.EscapeDataString(_resourceIdParameter ?? string.Empty);
        var bodyPath = Uri.EscapeDataString(_resourceIdBodyPath ?? string.Empty);
        Policy = $"Permission:{Permission}|resource={resource}|body={_resourceIdFromBody}|path={bodyPath}";
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
