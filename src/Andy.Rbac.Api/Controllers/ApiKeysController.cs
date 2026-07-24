using Andy.Rbac.Api.Authorization;
using Andy.Rbac.Api.Services;
using Andy.Rbac.Infrastructure.Data;
using Andy.Rbac.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Andy.Rbac.Api.Controllers;

/// <summary>
/// API key lifecycle. Keys authenticate the andy-rbac CLI and automation as an
/// existing <see cref="Subject"/> — see <see cref="ApiKeyAuthenticationHandler"/>.
///
/// Administrator-only: a key carries its owner's authority (optionally narrowed
/// by <see cref="ApiKey.Scopes"/>), so minting one is equivalent to issuing a
/// long-lived credential for that subject.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = RbacAuthorizationPolicies.Administrator)]
public sealed class ApiKeysController : ControllerBase
{
    private readonly RbacDbContext _db;
    private readonly ILogger<ApiKeysController> _logger;

    public ApiKeysController(RbacDbContext db, ILogger<ApiKeysController> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// Lists key metadata. The secret is never returned — it exists in plaintext
    /// only in the response to <see cref="CreateApiKey"/>.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(List<ApiKeyDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetApiKeys([FromQuery] string? subjectExternalId, CancellationToken ct)
    {
        var query = _db.ApiKeys
            .Include(k => k.Subject)
            .Include(k => k.Application)
            .AsNoTracking();

        if (!string.IsNullOrWhiteSpace(subjectExternalId))
            query = query.Where(k => k.Subject.ExternalId == subjectExternalId);

        var keys = await query
            .OrderByDescending(k => k.CreatedAt)
            .Select(k => new ApiKeyDto(
                k.Id,
                k.Name,
                k.KeyPrefix,
                k.Subject.ExternalId,
                k.Application != null ? k.Application.Code : null,
                k.Scopes,
                k.IsActive,
                k.CreatedAt,
                k.ExpiresAt,
                k.LastUsedAt))
            .ToListAsync(ct);

        return Ok(keys);
    }

    /// <summary>
    /// Mints a key for a subject. The plaintext key is returned exactly once;
    /// only its SHA-256 hash is persisted, so it cannot be recovered later.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(CreatedApiKeyDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CreateApiKey([FromBody] CreateApiKeyRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest("Name is required.");

        if (request.ExpiresAt is { } expiresAt && expiresAt <= DateTimeOffset.UtcNow)
            return BadRequest("ExpiresAt must be in the future.");

        var resolution = await SubjectResolver.ResolveAsync(
            _db, request.SubjectExternalId, request.SubjectProvider, tracking: false, ct);
        if (resolution.IsAmbiguous)
            return BadRequest("Subject provider is required for an ambiguous external ID.");
        if (resolution.Subject is null)
            return NotFound($"Subject '{request.SubjectExternalId}' not found.");

        Guid? applicationId = null;
        if (!string.IsNullOrWhiteSpace(request.ApplicationCode))
        {
            var application = await _db.Applications
                .FirstOrDefaultAsync(a => a.Code == request.ApplicationCode, ct);
            if (application is null)
                return NotFound($"Application '{request.ApplicationCode}' not found.");
            applicationId = application.Id;
        }

        var generated = ApiKeySecret.Generate();
        var apiKey = new ApiKey
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            KeyHash = generated.Hash,
            KeyPrefix = generated.Prefix,
            SubjectId = resolution.Subject.Id,
            ApplicationId = applicationId,
            Scopes = request.Scopes ?? [],
            ExpiresAt = request.ExpiresAt,
            IsActive = true
        };

        _db.ApiKeys.Add(apiKey);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Minted API key {KeyPrefix} ({Name}) for subject {SubjectExternalId}",
            apiKey.KeyPrefix, apiKey.Name, resolution.Subject.ExternalId);

        return StatusCode(StatusCodes.Status201Created, new CreatedApiKeyDto(
            apiKey.Id,
            apiKey.Name,
            apiKey.KeyPrefix,
            generated.PlaintextKey,
            resolution.Subject.ExternalId,
            request.ApplicationCode,
            apiKey.Scopes,
            apiKey.ExpiresAt));
    }

    /// <summary>
    /// Revokes a key. Deactivation rather than deletion, so the audit trail and
    /// last-used record survive. Takes effect on the next request.
    /// </summary>
    [HttpPost("{id:guid}/revoke")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RevokeApiKey(Guid id, CancellationToken ct)
    {
        var apiKey = await _db.ApiKeys.FirstOrDefaultAsync(k => k.Id == id, ct);
        if (apiKey is null)
            return NotFound();

        if (apiKey.IsActive)
        {
            apiKey.IsActive = false;
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("Revoked API key {KeyPrefix}", apiKey.KeyPrefix);
        }

        return NoContent();
    }
}

/// <param name="SubjectExternalId">External ID of the subject the key acts as.</param>
/// <param name="Name">Human-readable label for the key.</param>
/// <param name="SubjectProvider">Identity provider, when the external ID is ambiguous.</param>
/// <param name="ApplicationCode">Optional application scope for role resolution.</param>
/// <param name="Scopes">
/// Role codes this key may present. Empty means the owner's full role set;
/// a non-empty list is an intersection, never a union.
/// </param>
/// <param name="ExpiresAt">Optional expiry. Null means the key does not expire.</param>
public record CreateApiKeyRequest(
    string SubjectExternalId,
    string Name,
    string? SubjectProvider = null,
    string? ApplicationCode = null,
    List<string>? Scopes = null,
    DateTimeOffset? ExpiresAt = null);

public record ApiKeyDto(
    Guid Id,
    string Name,
    string KeyPrefix,
    string SubjectExternalId,
    string? ApplicationCode,
    List<string> Scopes,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? LastUsedAt);

/// <remarks>
/// <c>Key</c> is the plaintext key and is returned only here — only its hash is
/// stored, so it cannot be recovered afterwards.
/// </remarks>
public record CreatedApiKeyDto(
    Guid Id,
    string Name,
    string KeyPrefix,
    string Key,
    string SubjectExternalId,
    string? ApplicationCode,
    List<string> Scopes,
    DateTimeOffset? ExpiresAt);
