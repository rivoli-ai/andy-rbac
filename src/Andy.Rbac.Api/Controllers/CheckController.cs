using System.Security.Claims;
using Andy.Rbac.Api.Services;
using Andy.Rbac.Api.Authorization;
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
    private readonly ICallerGroupsResolver _groupsResolver;
    private readonly ILogger<CheckController> _logger;

    public CheckController(
        IPermissionEvaluator evaluator,
        ICallerGroupsResolver groupsResolver,
        ILogger<CheckController> logger)
    {
        _evaluator = evaluator;
        _groupsResolver = groupsResolver;
        _logger = logger;
    }

    /// <summary>
    /// Group memberships are sourced from the validated JWT (issue #45) when
    /// the request is checking the caller's own subject. A caller checking
    /// another subject may assert that subject's groups only if it is an active
    /// service principal — see <see cref="ICallerGroupsResolver"/>. Assertions
    /// from anyone else are ignored and logged rather than silently dropped.
    /// </summary>
    private string? EffectiveProvider(string subjectExternalId, string? requestedProvider) =>
        TrustedCallerIdentity.EffectiveProvider(User, subjectExternalId, requestedProvider);

    private Task<IReadOnlyList<string>?> GroupsForSubjectAsync(
        string subjectExternalId, string? selectedProvider, IEnumerable<string>? requestedGroups, CancellationToken ct) =>
        _groupsResolver.ResolveAsync(User, subjectExternalId, selectedProvider, requestedGroups, ct);

    /// <summary>
    /// Checks if a subject has a specific permission.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(CheckPermissionResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> CheckPermission([FromBody] CheckPermissionRequest request, CancellationToken ct)
    {
        var provider = EffectiveProvider(request.SubjectId, request.SubjectProvider);
        var groups = await GroupsForSubjectAsync(request.SubjectId, provider, request.Groups, ct);
        var result = string.IsNullOrWhiteSpace(provider)
            ? await _evaluator.CheckPermissionAsync(
                request.SubjectId, request.Permission, groups, request.ResourceInstanceId, ct)
            : await _evaluator.CheckPermissionForProviderAsync(
                request.SubjectId, provider, request.Permission,
                groups, request.ResourceInstanceId, ct);

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
        var provider = EffectiveProvider(request.SubjectId, request.SubjectProvider);
        var groups = await GroupsForSubjectAsync(request.SubjectId, provider, request.Groups, ct);
        var result = string.IsNullOrWhiteSpace(provider)
            ? await _evaluator.CheckAnyPermissionAsync(
                request.SubjectId, request.Permissions, groups, request.ResourceInstanceId, ct)
            : await _evaluator.CheckAnyPermissionForProviderAsync(
                request.SubjectId, provider, request.Permissions,
                groups, request.ResourceInstanceId, ct);

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
        [FromQuery] string? subjectProvider,
        [FromQuery] string? groups,
        CancellationToken ct)
    {
        var provider = EffectiveProvider(subjectId, subjectProvider);
        var effectiveGroups = await GroupsForSubjectAsync(subjectId, provider, SplitGroups(groups), ct);
        var permissions = string.IsNullOrWhiteSpace(provider)
            ? await _evaluator.GetPermissionsAsync(subjectId, effectiveGroups, applicationCode, ct)
            : await _evaluator.GetPermissionsForProviderAsync(subjectId, provider, effectiveGroups, applicationCode, ct);
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
        [FromQuery] string? subjectProvider,
        [FromQuery] string? groups,
        CancellationToken ct)
    {
        var provider = EffectiveProvider(subjectId, subjectProvider);
        var effectiveGroups = await GroupsForSubjectAsync(subjectId, provider, SplitGroups(groups), ct);
        var roles = string.IsNullOrWhiteSpace(provider)
            ? await _evaluator.GetRolesAsync(subjectId, effectiveGroups, applicationCode, ct)
            : await _evaluator.GetRolesForProviderAsync(subjectId, provider, effectiveGroups, applicationCode, ct);
        return Ok(new GetRolesResponse { Roles = roles.ToList() });
    }

    /// <summary>
    /// The clients pass groups on these GET endpoints as a single
    /// comma-separated query value (see <c>RbacHttpClient.GetPermissionsAsync</c>).
    /// </summary>
    private static IEnumerable<string>? SplitGroups(string? groups) =>
        string.IsNullOrWhiteSpace(groups)
            ? null
            : groups.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

/// <summary>
/// Request to check a single permission.
/// </summary>
/// <param name="SubjectId">External ID of the subject (user).</param>
/// <param name="Permission">Permission code in format "app:resource:action".</param>
/// <param name="ResourceInstanceId">Optional resource instance ID for instance-level checks.</param>
/// <param name="SubjectProvider">Optional identity provider used to disambiguate the subject.</param>
/// <param name="Groups">
/// External group memberships asserted for the subject. Honoured only when the
/// caller is that subject (in which case the token's own claims are used
/// instead) or an active service principal — see
/// <see cref="Andy.Rbac.Api.Authorization.ICallerGroupsResolver"/>. Assertions
/// from any other caller are ignored and logged.
/// </param>
public record CheckPermissionRequest(
    string SubjectId,
    string Permission,
    string? ResourceInstanceId = null,
    string? SubjectProvider = null,
    List<string>? Groups = null);

/// <summary>
/// Request to check if subject has any of multiple permissions.
/// </summary>
/// <param name="SubjectId">External ID of the subject (user).</param>
/// <param name="Permissions">Permission codes in format "app:resource:action".</param>
/// <param name="ResourceInstanceId">Optional resource instance ID for instance-level checks.</param>
/// <param name="SubjectProvider">Optional identity provider used to disambiguate the subject.</param>
/// <param name="Groups">See <see cref="CheckPermissionRequest.Groups"/>.</param>
public record CheckAnyPermissionRequest(
    string SubjectId,
    List<string> Permissions,
    string? ResourceInstanceId = null,
    string? SubjectProvider = null,
    List<string>? Groups = null);

public record CheckPermissionResponse { public bool Allowed { get; init; } public string? Reason { get; init; } }
public record GetPermissionsResponse { public List<string> Permissions { get; init; } = []; }
public record GetRolesResponse { public List<string> Roles { get; init; } = []; }
