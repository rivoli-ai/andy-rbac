using System.Security.Claims;
using Andy.Rbac.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Andy.Rbac.Api.Controllers;

/// <summary>
/// Permission check endpoints for client applications.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
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
    /// Group memberships are sourced from the validated JWT (issue #45) when
    /// the request is checking the caller's own subject. For checks targeting
    /// other subjects, group claims from the caller's token do not apply —
    /// only directly-granted permissions are considered. Stored group
    /// memberships per subject are tracked separately (out of scope here).
    /// </summary>
    private List<string>? GroupsForSubject(string subjectExternalId)
    {
        var callerSub = User.FindFirst("sub")?.Value
            ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (callerSub is null || !string.Equals(callerSub, subjectExternalId, StringComparison.Ordinal))
        {
            return null;
        }

        var groups = User.FindAll("groups").Select(c => c.Value).ToList();
        return groups.Count > 0 ? groups : null;
    }

    /// <summary>
    /// Checks if a subject has a specific permission.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(CheckPermissionResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> CheckPermission([FromBody] CheckPermissionRequest request, CancellationToken ct)
    {
        var result = await _evaluator.CheckPermissionAsync(
            request.SubjectId,
            request.Permission,
            GroupsForSubject(request.SubjectId),
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
    /// </summary>
    [HttpPost("any")]
    [ProducesResponseType(typeof(CheckPermissionResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> CheckAnyPermission([FromBody] CheckAnyPermissionRequest request, CancellationToken ct)
    {
        var result = await _evaluator.CheckAnyPermissionAsync(
            request.SubjectId,
            request.Permissions,
            GroupsForSubject(request.SubjectId),
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
    /// </summary>
    [HttpGet("permissions/{subjectId}")]
    [ProducesResponseType(typeof(GetPermissionsResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPermissions(
        string subjectId,
        [FromQuery] string? applicationCode,
        CancellationToken ct)
    {
        var permissions = await _evaluator.GetPermissionsAsync(subjectId, GroupsForSubject(subjectId), applicationCode, ct);
        return Ok(new GetPermissionsResponse { Permissions = permissions.ToList() });
    }

    /// <summary>
    /// Gets all roles for a subject.
    /// </summary>
    [HttpGet("roles/{subjectId}")]
    [ProducesResponseType(typeof(GetRolesResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetRoles(
        string subjectId,
        [FromQuery] string? applicationCode,
        CancellationToken ct)
    {
        var roles = await _evaluator.GetRolesAsync(subjectId, GroupsForSubject(subjectId), applicationCode, ct);
        return Ok(new GetRolesResponse { Roles = roles.ToList() });
    }
}

/// <summary>
/// Request to check a single permission.
/// </summary>
/// <param name="SubjectId">External ID of the subject (user).</param>
/// <param name="Permission">Permission code in format "app:resource:action".</param>
/// <param name="ResourceInstanceId">Optional resource instance ID for instance-level checks.</param>
public record CheckPermissionRequest(
    string SubjectId,
    string Permission,
    string? ResourceInstanceId = null);

/// <summary>
/// Request to check if subject has any of multiple permissions.
/// </summary>
public record CheckAnyPermissionRequest(
    string SubjectId,
    List<string> Permissions,
    string? ResourceInstanceId = null);

public record CheckPermissionResponse { public bool Allowed { get; init; } public string? Reason { get; init; } }
public record GetPermissionsResponse { public List<string> Permissions { get; init; } = []; }
public record GetRolesResponse { public List<string> Roles { get; init; } = []; }
