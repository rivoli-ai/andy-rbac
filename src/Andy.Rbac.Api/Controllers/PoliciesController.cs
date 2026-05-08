// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Rbac.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Andy.Rbac.Api.Controllers;

/// <summary>
/// Policy catalog endpoints (Epic V3). Read paths are open to any
/// authenticated caller; mutating paths require admin.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class PoliciesController : ControllerBase
{
    private readonly IPolicyService _policyService;

    public PoliciesController(IPolicyService policyService)
    {
        _policyService = policyService;
    }

    [HttpGet]
    [ProducesResponseType(typeof(List<PolicyDetail>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPolicies(CancellationToken ct)
    {
        var result = await _policyService.GetAllAsync(ct);
        return Ok(result.Policies);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(PolicyDetail), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPolicy(Guid id, CancellationToken ct)
    {
        var result = await _policyService.GetByIdAsync(id, ct);
        if (result == null) return NotFound();
        return Ok(result.Policy);
    }

    [HttpGet("by-code/{code}")]
    [ProducesResponseType(typeof(PolicyDetail), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPolicyByCode(string code, CancellationToken ct)
    {
        var result = await _policyService.GetByCodeAsync(code, ct);
        if (result == null) return NotFound();
        return Ok(result.Policy);
    }

    [HttpPost]
    [ProducesResponseType(typeof(PolicyDetail), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreatePolicy([FromBody] CreatePolicyRequest request, CancellationToken ct)
    {
        try
        {
            var result = await _policyService.CreateAsync(request, ct);
            return CreatedAtAction(nameof(GetPolicy), new { id = result.Policy.Id }, result.Policy);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(PolicyDetail), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdatePolicy(Guid id, [FromBody] UpdatePolicyRequest request, CancellationToken ct)
    {
        try
        {
            var result = await _policyService.UpdateAsync(id, request, ct);
            if (result == null) return NotFound();
            return Ok(result.Policy);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> DeletePolicy(Guid id, CancellationToken ct)
    {
        try
        {
            var deleted = await _policyService.DeleteAsync(id, ct);
            if (!deleted) return NotFound();
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }
}
