using Andy.Rbac.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Andy.Rbac.Api.Controllers;

/// <summary>
/// Role management endpoints.
/// </summary>
[ApiController]
[Route("api/[controller]")]
// Note: Auth temporarily disabled for development/testing
// [Authorize]
public class RolesController : ControllerBase
{
    private readonly IRoleService _roleService;

    public RolesController(IRoleService roleService)
    {
        _roleService = roleService;
    }

    /// <summary>
    /// Gets all roles, optionally filtered by application.
    /// </summary>
    [HttpGet]
    [AllowAnonymous]
    [ProducesResponseType(typeof(List<RoleDetail>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetRoles([FromQuery] string? applicationCode, CancellationToken ct)
    {
        var result = await _roleService.GetAllAsync(applicationCode, ct);
        return Ok(result.Roles);
    }

    /// <summary>
    /// Gets a role by ID.
    /// </summary>
    [HttpGet("{id:guid}")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(RoleDetail), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetRole(Guid id, CancellationToken ct)
    {
        var result = await _roleService.GetByIdAsync(id, ct);
        if (result == null)
            return NotFound();

        return Ok(result.Role);
    }

    /// <summary>
    /// Gets a role by code.
    /// </summary>
    [HttpGet("by-code/{code}")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(RoleDetail), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetRoleByCode(string code, [FromQuery] string? applicationCode, CancellationToken ct)
    {
        var result = await _roleService.GetByCodeAsync(code, applicationCode, ct);
        if (result == null)
            return NotFound();

        return Ok(result.Role);
    }

    /// <summary>
    /// Creates a new role.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(RoleDetail), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateRole([FromBody] CreateRoleRequest request, CancellationToken ct)
    {
        try
        {
            var result = await _roleService.CreateAsync(request, ct);
            return CreatedAtAction(nameof(GetRole), new { id = result.Role.Id }, result.Role);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    /// <summary>
    /// Deletes a role.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> DeleteRole(Guid id, CancellationToken ct)
    {
        try
        {
            var deleted = await _roleService.DeleteAsync(id, ct);
            if (!deleted)
                return NotFound();

            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    /// <summary>
    /// Assigns a role to a user.
    /// </summary>
    [HttpPost("assign")]
    [ProducesResponseType(typeof(string), StatusCodes.Status200OK)]
    public async Task<IActionResult> AssignRole([FromBody] AssignRoleRequest request, CancellationToken ct)
    {
        var result = await _roleService.AssignToSubjectAsync(
            request.SubjectExternalId,
            request.RoleCode,
            request.ResourceInstanceId,
            ct);

        if (result.StartsWith("Error:"))
            return BadRequest(result);

        return Ok(result);
    }

    /// <summary>
    /// Revokes a role from a user.
    /// </summary>
    [HttpPost("revoke")]
    [ProducesResponseType(typeof(string), StatusCodes.Status200OK)]
    public async Task<IActionResult> RevokeRole([FromBody] AssignRoleRequest request, CancellationToken ct)
    {
        var result = await _roleService.RevokeFromSubjectAsync(
            request.SubjectExternalId,
            request.RoleCode,
            request.ResourceInstanceId,
            ct);

        if (result.StartsWith("Error:"))
            return BadRequest(result);

        return Ok(result);
    }
}

public record AssignRoleRequest(
    string SubjectExternalId,
    string RoleCode,
    string? ResourceInstanceId = null);
