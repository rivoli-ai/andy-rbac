using Andy.Rbac.Abstractions;
using Andy.Rbac.Configuration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Andy.Rbac.Authorization;

/// <summary>
/// Authorization handler that checks permissions using the RBAC service.
/// </summary>
public class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
{
    private readonly IPermissionService _permissionService;
    private readonly ICurrentSubjectAccessor _subjectAccessor;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly RbacOptions _options;
    private readonly ILogger<PermissionAuthorizationHandler> _logger;

    public PermissionAuthorizationHandler(
        IPermissionService permissionService,
        ICurrentSubjectAccessor subjectAccessor,
        IHttpContextAccessor httpContextAccessor,
        IOptions<RbacOptions> options,
        ILogger<PermissionAuthorizationHandler> logger)
    {
        _permissionService = permissionService;
        _subjectAccessor = subjectAccessor;
        _httpContextAccessor = httpContextAccessor;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        var subjectId = _subjectAccessor.GetSubjectId();
        if (string.IsNullOrEmpty(subjectId))
        {
            _logger.LogDebug("Permission check failed: no authenticated subject");
            return;
        }

        var permission = NormalizePermission(requirement.Permission);
        var resourceInstanceId = GetResourceInstanceId(requirement.ResourceIdParameter);

        var hasPermission = await _permissionService.HasPermissionAsync(
            subjectId,
            permission,
            resourceInstanceId);

        if (hasPermission)
        {
            _logger.LogDebug(
                "Permission granted: {SubjectId} has {Permission} on {ResourceInstanceId}",
                subjectId, permission, resourceInstanceId ?? "(global)");
            context.Succeed(requirement);
        }
        else
        {
            _logger.LogDebug(
                "Permission denied: {SubjectId} lacks {Permission} on {ResourceInstanceId}",
                subjectId, permission, resourceInstanceId ?? "(global)");
        }
    }

    private string NormalizePermission(string permission)
    {
        // If permission doesn't contain app code, prepend it
        if (!permission.Contains(':') || permission.Split(':').Length < 3)
        {
            return $"{_options.ApplicationCode}:{permission}";
        }
        return permission;
    }

    private string? GetResourceInstanceId(string? parameterName)
    {
        if (string.IsNullOrEmpty(parameterName))
            return null;

        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext == null)
            return null;

        // Try route values first
        if (httpContext.Request.RouteValues.TryGetValue(parameterName, out var routeValue))
        {
            return routeValue?.ToString();
        }

        // Try query string
        if (httpContext.Request.Query.TryGetValue(parameterName, out var queryValue))
        {
            return queryValue.ToString();
        }

        return null;
    }
}

/// <summary>
/// Authorization handler for any-permission requirements.
/// </summary>
public class AnyPermissionAuthorizationHandler : AuthorizationHandler<AnyPermissionRequirement>
{
    private readonly IPermissionService _permissionService;
    private readonly ICurrentSubjectAccessor _subjectAccessor;
    private readonly RbacOptions _options;
    private readonly ILogger<AnyPermissionAuthorizationHandler> _logger;

    public AnyPermissionAuthorizationHandler(
        IPermissionService permissionService,
        ICurrentSubjectAccessor subjectAccessor,
        IOptions<RbacOptions> options,
        ILogger<AnyPermissionAuthorizationHandler> logger)
    {
        _permissionService = permissionService;
        _subjectAccessor = subjectAccessor;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        AnyPermissionRequirement requirement)
    {
        var subjectId = _subjectAccessor.GetSubjectId();
        if (string.IsNullOrEmpty(subjectId))
            return;

        var permissions = requirement.Permissions
            .Select(p => NormalizePermission(p))
            .ToList();

        var hasAny = await _permissionService.HasAnyPermissionAsync(subjectId, permissions);

        if (hasAny)
        {
            context.Succeed(requirement);
        }
    }

    private string NormalizePermission(string permission)
    {
        if (!permission.Contains(':') || permission.Split(':').Length < 3)
        {
            return $"{_options.ApplicationCode}:{permission}";
        }
        return permission;
    }
}

/// <summary>
/// Authorization handler for role requirements.
/// </summary>
public class RoleAuthorizationHandler : AuthorizationHandler<RoleRequirement>
{
    private readonly IPermissionService _permissionService;
    private readonly ICurrentSubjectAccessor _subjectAccessor;
    private readonly ILogger<RoleAuthorizationHandler> _logger;

    public RoleAuthorizationHandler(
        IPermissionService permissionService,
        ICurrentSubjectAccessor subjectAccessor,
        ILogger<RoleAuthorizationHandler> logger)
    {
        _permissionService = permissionService;
        _subjectAccessor = subjectAccessor;
        _logger = logger;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        RoleRequirement requirement)
    {
        var subjectId = _subjectAccessor.GetSubjectId();
        if (string.IsNullOrEmpty(subjectId))
            return;

        var roles = await _permissionService.GetRolesAsync(subjectId);

        if (roles.Contains(requirement.Role, StringComparer.OrdinalIgnoreCase))
        {
            context.Succeed(requirement);
        }
    }
}
