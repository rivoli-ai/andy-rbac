using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json.Serialization;

namespace Andy.Rbac.Api.Controllers;

/// <summary>
/// RFC 7591 Dynamic Client Registration Protocol implementation
/// Allows MCP clients like Claude Desktop to dynamically register OAuth clients.
/// This controller proxies registration to Andy.Auth.
/// </summary>
[ApiController]
[Route("register")]
[AllowAnonymous] // DCR must be publicly accessible - clients register BEFORE authenticating
[EnableCors("AllowMcpClients")]
public class DynamicClientRegistrationController : ControllerBase
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<DynamicClientRegistrationController> _logger;

    private string AndyAuthAuthority => _configuration["AndyAuth:Authority"]
        ?? _configuration["Auth:Authority"]
        ?? throw new InvalidOperationException("AndyAuth:Authority not configured");

    public DynamicClientRegistrationController(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<DynamicClientRegistrationController> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// HEAD /register - Check if registration endpoint is available
    /// Some clients send HEAD requests to verify endpoint existence
    /// </summary>
    [HttpHead]
    public IActionResult CheckRegistrationEndpoint()
    {
        _logger.LogInformation("HEAD request to /register - endpoint check");
        return Ok();
    }

    /// <summary>
    /// RFC 7591 Client Registration Endpoint
    /// POST /register - Proxies to Andy.Auth DCR endpoint
    /// </summary>
    [HttpPost]
    [Consumes("application/json")]
    [Produces("application/json")]
    public async Task<IActionResult> RegisterClient([FromBody] ClientRegistrationRequest request)
    {
        _logger.LogInformation("Client registration request received, proxying to Andy.Auth");

        try
        {
            // Validate request
            if (request.RedirectUris == null || request.RedirectUris.Length == 0)
            {
                return BadRequest(new { error = "invalid_client_metadata", error_description = "redirect_uris is required" });
            }

            // Validate redirect URIs
            foreach (var uri in request.RedirectUris)
            {
                if (!Uri.TryCreate(uri, UriKind.Absolute, out _))
                {
                    return BadRequest(new { error = "invalid_redirect_uri", error_description = $"Invalid redirect URI: {uri}" });
                }
            }

            // Proxy to Andy.Auth's DCR endpoint
            var httpClient = _httpClientFactory.CreateClient();
            var andyAuthDcrUrl = $"{AndyAuthAuthority}/connect/register";

            var response = await httpClient.PostAsJsonAsync(andyAuthDcrUrl, request);

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<ClientRegistrationResponse>();
                _logger.LogInformation("Successfully registered client via Andy.Auth: {ClientId}", result?.ClientId);
                return Created($"/register/{result?.ClientId}", result);
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("Andy.Auth DCR returned {StatusCode}: {Error}", response.StatusCode, errorContent);
                return StatusCode((int)response.StatusCode, errorContent);
            }
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to connect to Andy.Auth for DCR");
            return StatusCode(503, new { error = "server_error", error_description = "Authorization server unavailable" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during client registration");
            return StatusCode(500, new { error = "server_error", error_description = "An error occurred during registration" });
        }
    }

    /// <summary>
    /// Get registered client information (proxies to Andy.Auth)
    /// </summary>
    [HttpGet("{clientId}")]
    public async Task<IActionResult> GetClient(string clientId)
    {
        try
        {
            var httpClient = _httpClientFactory.CreateClient();
            var andyAuthUrl = $"{AndyAuthAuthority}/connect/register/{clientId}";

            var response = await httpClient.GetAsync(andyAuthUrl);

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<ClientInfoResponse>();
                return Ok(result);
            }
            else
            {
                return NotFound(new { error = "not_found", error_description = "Client not found" });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching client info");
            return StatusCode(500, new { error = "server_error", error_description = "An error occurred" });
        }
    }
}

/// <summary>
/// RFC 7591 Client Registration Request
/// </summary>
public class ClientRegistrationRequest
{
    [JsonPropertyName("redirect_uris")]
    public string[]? RedirectUris { get; set; }

    [JsonPropertyName("token_endpoint_auth_method")]
    public string? TokenEndpointAuthMethod { get; set; }

    [JsonPropertyName("grant_types")]
    public string[]? GrantTypes { get; set; }

    [JsonPropertyName("response_types")]
    public string[]? ResponseTypes { get; set; }

    [JsonPropertyName("client_name")]
    public string? ClientName { get; set; }

    [JsonPropertyName("client_uri")]
    public string? ClientUri { get; set; }

    [JsonPropertyName("logo_uri")]
    public string? LogoUri { get; set; }

    [JsonPropertyName("scope")]
    public string? Scope { get; set; }

    [JsonPropertyName("contacts")]
    public string[]? Contacts { get; set; }

    [JsonPropertyName("tos_uri")]
    public string? TosUri { get; set; }

    [JsonPropertyName("policy_uri")]
    public string? PolicyUri { get; set; }

    [JsonPropertyName("jwks_uri")]
    public string? JwksUri { get; set; }

    [JsonPropertyName("jwks")]
    public object? Jwks { get; set; }

    [JsonPropertyName("software_id")]
    public string? SoftwareId { get; set; }

    [JsonPropertyName("software_version")]
    public string? SoftwareVersion { get; set; }
}

/// <summary>
/// RFC 7591 Client Registration Response
/// </summary>
public class ClientRegistrationResponse
{
    [JsonPropertyName("client_id")]
    public required string ClientId { get; set; }

    [JsonPropertyName("client_secret")]
    public string? ClientSecret { get; set; }

    [JsonPropertyName("client_id_issued_at")]
    public long? ClientIdIssuedAt { get; set; }

    [JsonPropertyName("client_secret_expires_at")]
    public long? ClientSecretExpiresAt { get; set; } = 0;

    [JsonPropertyName("redirect_uris")]
    public string[]? RedirectUris { get; set; }

    [JsonPropertyName("token_endpoint_auth_method")]
    public string? TokenEndpointAuthMethod { get; set; }

    [JsonPropertyName("grant_types")]
    public string[]? GrantTypes { get; set; }

    [JsonPropertyName("response_types")]
    public string[]? ResponseTypes { get; set; }

    [JsonPropertyName("client_name")]
    public string? ClientName { get; set; }

    [JsonPropertyName("client_uri")]
    public string? ClientUri { get; set; }

    [JsonPropertyName("logo_uri")]
    public string? LogoUri { get; set; }

    [JsonPropertyName("scope")]
    public string? Scope { get; set; }

    [JsonPropertyName("contacts")]
    public string[]? Contacts { get; set; }

    [JsonPropertyName("tos_uri")]
    public string? TosUri { get; set; }

    [JsonPropertyName("policy_uri")]
    public string? PolicyUri { get; set; }

    [JsonPropertyName("registration_access_token")]
    public string? RegistrationAccessToken { get; set; }

    [JsonPropertyName("registration_client_uri")]
    public string? RegistrationClientUri { get; set; }
}

/// <summary>
/// Client information response
/// </summary>
public class ClientInfoResponse
{
    [JsonPropertyName("client_id")]
    public required string ClientId { get; set; }

    [JsonPropertyName("client_name")]
    public string? ClientName { get; set; }

    [JsonPropertyName("redirect_uris")]
    public string[]? RedirectUris { get; set; }

    [JsonPropertyName("token_endpoint_auth_method")]
    public string? TokenEndpointAuthMethod { get; set; }

    [JsonPropertyName("grant_types")]
    public string[]? GrantTypes { get; set; }

    [JsonPropertyName("response_types")]
    public string[]? ResponseTypes { get; set; }

    [JsonPropertyName("client_uri")]
    public string? ClientUri { get; set; }

    [JsonPropertyName("logo_uri")]
    public string? LogoUri { get; set; }

    [JsonPropertyName("tos_uri")]
    public string? TosUri { get; set; }

    [JsonPropertyName("policy_uri")]
    public string? PolicyUri { get; set; }

    [JsonPropertyName("client_id_issued_at")]
    public long? ClientIdIssuedAt { get; set; }
}
