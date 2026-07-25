using Andy.Rbac.Api.Services;
using Andy.Rbac.Api.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Andy.Rbac.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = RbacAuthorizationPolicies.Administrator)]
public sealed class InstancesController : ControllerBase
{
    private readonly IResourceInstanceService _service;

    public InstancesController(IResourceInstanceService service) => _service = service;

    [HttpPost]
    public async Task<IActionResult> Register(RegisterResourceInstanceRequest request, CancellationToken ct)
        => ToActionResult(await _service.RegisterAsync(
            request.ApplicationCode, request.ResourceTypeCode, request.ResourceInstanceId,
            request.OwnerSubjectId, request.OwnerSubjectProvider, request.DisplayName, request.Metadata, ct), created: true);

    [HttpDelete("{resourceTypeCode}/{resourceInstanceId}")]
    public async Task<IActionResult> Remove(
        string resourceTypeCode, string resourceInstanceId,
        [FromQuery] string applicationCode, CancellationToken ct)
        => ToActionResult(await _service.RemoveAsync(
            applicationCode, resourceTypeCode, resourceInstanceId, ActingPrincipal, ct));

    [HttpPost("permissions")]
    public async Task<IActionResult> Grant(GrantInstancePermissionRequest request, CancellationToken ct)
        => ToActionResult(await _service.GrantAsync(
            request.ApplicationCode, request.ResourceTypeCode, request.ResourceInstanceId,
            request.SubjectId, request.SubjectProvider, request.Action, request.ExpiresAt, ct), created: true);

    [HttpDelete("{resourceTypeCode}/{resourceInstanceId}/permissions/{subjectId}/{action}")]
    public async Task<IActionResult> Revoke(
        string resourceTypeCode, string resourceInstanceId, string subjectId, string action,
        [FromQuery] string applicationCode, [FromQuery] string? subjectProvider, CancellationToken ct)
        => ToActionResult(await _service.RevokeAsync(
            applicationCode, resourceTypeCode, resourceInstanceId, subjectId, subjectProvider, action,
            ActingPrincipal, ct));

    /// <summary>
    /// External ID of the caller, recorded as <c>RevokedByPrincipal</c> on the
    /// grant lifecycle events these endpoints stage.
    /// </summary>
    private string? ActingPrincipal => TrustedCallerIdentity.SubjectId(User);

    private IActionResult ToActionResult(ResourceInstanceMutationResult result, bool created = false)
    {
        if (result.NotFound) return NotFound(result.Error);
        if (!result.Success) return BadRequest(result.Error);
        if (created) return StatusCode(StatusCodes.Status201Created, new { result.Id });
        return NoContent();
    }
}

public record RegisterResourceInstanceRequest(
    string ApplicationCode,
    string ResourceTypeCode,
    string ResourceInstanceId,
    string? OwnerSubjectId = null,
    string? OwnerSubjectProvider = null,
    string? DisplayName = null,
    Dictionary<string, object>? Metadata = null);

public record GrantInstancePermissionRequest(
    string ApplicationCode,
    string SubjectId,
    string ResourceTypeCode,
    string ResourceInstanceId,
    string Action,
    string? SubjectProvider = null,
    DateTimeOffset? ExpiresAt = null);
