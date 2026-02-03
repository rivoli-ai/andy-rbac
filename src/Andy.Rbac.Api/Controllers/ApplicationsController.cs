using Andy.Rbac.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Andy.Rbac.Api.Controllers;

/// <summary>
/// Application management endpoints.
/// </summary>
[ApiController]
[Route("api/[controller]")]
// Note: Auth temporarily disabled for development/testing
// [Authorize]
public class ApplicationsController : ControllerBase
{
    private readonly IApplicationService _applicationService;

    public ApplicationsController(IApplicationService applicationService)
    {
        _applicationService = applicationService;
    }

    /// <summary>
    /// Gets all registered applications.
    /// </summary>
    [HttpGet]
    [AllowAnonymous] // Allow listing for testing
    [ProducesResponseType(typeof(ApplicationResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetApplications(CancellationToken ct)
    {
        var result = await _applicationService.GetAllAsync(ct);
        return Ok(result.Applications);
    }

    /// <summary>
    /// Gets an application by ID with full details.
    /// </summary>
    [HttpGet("{id:guid}")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApplicationDetail), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetApplication(Guid id, CancellationToken ct)
    {
        var result = await _applicationService.GetByIdAsync(id, ct);
        if (result == null)
            return NotFound();

        return Ok(result.Application);
    }

    /// <summary>
    /// Gets an application by code.
    /// </summary>
    [HttpGet("by-code/{code}")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApplicationDetail), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetApplicationByCode(string code, CancellationToken ct)
    {
        var result = await _applicationService.GetByCodeAsync(code, ct);
        if (result == null)
            return NotFound();

        return Ok(result.Application);
    }

    /// <summary>
    /// Creates a new application.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(ApplicationDetail), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateApplication([FromBody] CreateApplicationRequest request, CancellationToken ct)
    {
        try
        {
            var result = await _applicationService.CreateAsync(request, ct);
            return CreatedAtAction(nameof(GetApplication), new { id = result.Application.Id }, result.Application);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    /// <summary>
    /// Updates an application.
    /// </summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(ApplicationDetail), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateApplication(Guid id, [FromBody] UpdateApplicationRequest request, CancellationToken ct)
    {
        var result = await _applicationService.UpdateAsync(id, request, ct);
        if (result == null)
            return NotFound();

        return Ok(result.Application);
    }

    /// <summary>
    /// Deletes an application and all associated data.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteApplication(Guid id, CancellationToken ct)
    {
        var deleted = await _applicationService.DeleteAsync(id, ct);
        if (!deleted)
            return NotFound();

        return NoContent();
    }

    /// <summary>
    /// Adds a resource type to an application.
    /// </summary>
    [HttpPost("{id:guid}/resource-types")]
    [ProducesResponseType(typeof(ResourceTypeSummary), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> AddResourceType(Guid id, [FromBody] CreateResourceTypeRequest request, CancellationToken ct)
    {
        try
        {
            var result = await _applicationService.AddResourceTypeAsync(id, request, ct);
            if (result == null)
                return NotFound();

            return CreatedAtAction(nameof(GetApplication), new { id }, result.ResourceType);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }
}
