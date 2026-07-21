// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Rbac.Api.Services;
using Andy.Rbac.Api.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Andy.Rbac.Api.Controllers;

// SM.2.11 — admin grant management endpoints. The primary entry point is
// DELETE /api/grants/{grantId}, which:
//   1. Removes the InstancePermission row.
//   2. Stages a grant.revoked outbox row in the same transaction.
//   3. The OutboxDispatcher publishes to NATS; Conductor's
//      GrantLifecycleEventSource republishes onto ConductorEvent.grantLifecycle.
//   4. The PermissionGrant aggregate (SM.10) reduces to .revoked WITHOUT
//      waiting for the next gate consultation — closing the stale-grant
//      disagreement class from conductor#1861.

/// <summary>
/// Admin management of instance-level permission grants.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = RbacAuthorizationPolicies.Administrator)]
public class GrantsController : ControllerBase
{
    private readonly IGrantService _grantService;
    private readonly ILogger<GrantsController> _logger;

    public GrantsController(IGrantService grantService, ILogger<GrantsController> logger)
    {
        _grantService = grantService;
        _logger = logger;
    }

    /// <summary>
    /// Revokes an instance-level permission grant by its ID.
    /// Emits grant.revoked to the event bus in the same transaction so
    /// Conductor's PermissionGrant aggregate reconciles immediately.
    /// </summary>
    /// <param name="grantId">InstancePermission.Id to revoke.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpDelete("{grantId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RevokeGrant(Guid grantId, CancellationToken ct)
    {
        var revokedByPrincipal = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value;

        var result = await _grantService.RevokeAsync(grantId, revokedByPrincipal, ct);

        if (!result.Found)
            return NotFound();

        _logger.LogInformation(
            "Admin revoked grant {GrantId} (permission={Permission}, principal={Principal})",
            grantId, result.PermissionCode, result.Principal);

        return NoContent();
    }
}
