using Andy.Rbac.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Andy.Rbac.Api.Controllers;

/// <summary>
/// Permission check endpoints for client applications.
/// </summary>
[ApiController]
[Route("api/[controller]")]
// Note: Auth temporarily disabled for development/testing
// [Authorize]
public class CheckController : ControllerBase
{
    private readonly IPermissionEvaluator _evaluator;
    private readonly ILogger<CheckController> _logger;

    public CheckController(IPermissionEvaluator evaluator, ILogger<CheckController> logger)
    {
        _evaluator = evaluator;
        _logger = logger;
    }

    /// <summary>
    /// Checks if a subject has a specific permission.
    /// Groups are optional and represent group memberships from the token.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(CheckPermissionResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> CheckPermission([FromBody] CheckPermissionRequest request, CancellationToken ct)
    {
        var result = await _evaluator.CheckPermissionAsync(
            request.SubjectId,
            request.Permission,
            request.Groups,
            request.ResourceInstanceId,
            ct);

        return Ok(new CheckPermissionResponse
        {
            Allowed = result.Allowed,
            Reason = result.Reason
        });
    }

    /// <summary>
    /// Checks if a subject has any of the specified permissions.
    /// Groups are optional and represent group memberships from the token.
    /// </summary>
    [HttpPost("any")]
    [ProducesResponseType(typeof(CheckPermissionResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> CheckAnyPermission([FromBody] CheckAnyPermissionRequest request, CancellationToken ct)
    {
        var result = await _evaluator.CheckAnyPermissionAsync(
            request.SubjectId,
            request.Permissions,
            request.Groups,
            request.ResourceInstanceId,
            ct);

        return Ok(new CheckPermissionResponse
        {
            Allowed = result.Allowed,
            Reason = result.Reason
        });
    }

    /// <summary>
    /// Gets all permissions for a subject.
    /// Groups query parameter can be comma-separated list of group codes.
    /// </summary>
    [HttpGet("permissions/{subjectId}")]
    [ProducesResponseType(typeof(GetPermissionsResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPermissions(
        string subjectId,
        [FromQuery] string? groups,
        [FromQuery] string? applicationCode,
        CancellationToken ct)
    {
        var groupList = string.IsNullOrEmpty(groups) ? null : groups.Split(',').Select(g => g.Trim()).ToList();
        var permissions = await _evaluator.GetPermissionsAsync(subjectId, groupList, applicationCode, ct);
        return Ok(new GetPermissionsResponse { Permissions = permissions.ToList() });
    }

    /// <summary>
    /// Gets all roles for a subject.
    /// Groups query parameter can be comma-separated list of group codes.
    /// </summary>
    [HttpGet("roles/{subjectId}")]
    [ProducesResponseType(typeof(GetRolesResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetRoles(
        string subjectId,
        [FromQuery] string? groups,
        [FromQuery] string? applicationCode,
        CancellationToken ct)
    {
        var groupList = string.IsNullOrEmpty(groups) ? null : groups.Split(',').Select(g => g.Trim()).ToList();
        var roles = await _evaluator.GetRolesAsync(subjectId, groupList, applicationCode, ct);
        return Ok(new GetRolesResponse { Roles = roles.ToList() });
    }
}

/// <summary>
/// Request to check a single permission.
/// </summary>
/// <param name="SubjectId">External ID of the subject (user).</param>
/// <param name="Permission">Permission code in format "app:resource:action".</param>
/// <param name="Groups">Optional group codes from token claims. Permissions are checked for subject + all groups.</param>
/// <param name="ResourceInstanceId">Optional resource instance ID for instance-level checks.</param>
public record CheckPermissionRequest(
    string SubjectId,
    string Permission,
    List<string>? Groups = null,
    string? ResourceInstanceId = null);

/// <summary>
/// Request to check if subject has any of multiple permissions.
/// </summary>
public record CheckAnyPermissionRequest(
    string SubjectId,
    List<string> Permissions,
    List<string>? Groups = null,
    string? ResourceInstanceId = null);

public record CheckPermissionResponse { public bool Allowed { get; init; } public string? Reason { get; init; } }
public record GetPermissionsResponse { public List<string> Permissions { get; init; } = []; }
public record GetRolesResponse { public List<string> Roles { get; init; } = []; }
