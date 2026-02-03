namespace Andy.Rbac.Configuration;

/// <summary>
/// Configuration options for the RBAC library.
/// </summary>
public class RbacOptions
{
    public const string SectionName = "Rbac";

    /// <summary>
    /// The application code for this application (e.g., "andy-docs", "andy-cli").
    /// Used to filter permissions and roles.
    /// </summary>
    public required string ApplicationCode { get; set; }

    /// <summary>
    /// Base URL of the RBAC API (e.g., "https://rbac.rivoli.ai").
    /// </summary>
    public string? ApiBaseUrl { get; set; }

    /// <summary>
    /// gRPC endpoint for the RBAC service.
    /// </summary>
    public string? GrpcEndpoint { get; set; }

    /// <summary>
    /// Prefer gRPC over HTTP when available.
    /// </summary>
    public bool PreferGrpc { get; set; } = true;

    /// <summary>
    /// Cache settings.
    /// </summary>
    public RbacCacheOptions Cache { get; set; } = new();

    /// <summary>
    /// Whether to automatically provision subjects on first authentication.
    /// </summary>
    public bool AutoProvisionSubjects { get; set; } = true;

    /// <summary>
    /// Claim type to use for the subject ID (default: "sub").
    /// </summary>
    public string SubjectIdClaimType { get; set; } = "sub";

    /// <summary>
    /// Claim type to use for the provider (default: "iss").
    /// </summary>
    public string ProviderClaimType { get; set; } = "iss";

    /// <summary>
    /// Whether to log permission checks for auditing.
    /// </summary>
    public bool EnableAuditLogging { get; set; } = true;

    /// <summary>
    /// HTTP client settings.
    /// </summary>
    public HttpClientOptions HttpClient { get; set; } = new();
}

public class RbacCacheOptions
{
    /// <summary>
    /// Whether caching is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// How long to cache permission data.
    /// </summary>
    public TimeSpan Expiration { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Whether to use distributed cache (Redis) instead of in-memory.
    /// </summary>
    public bool UseDistributedCache { get; set; } = false;

    /// <summary>
    /// Redis connection string (when UseDistributedCache is true).
    /// </summary>
    public string? RedisConnectionString { get; set; }
}

public class HttpClientOptions
{
    /// <summary>
    /// Timeout for HTTP requests.
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Number of retry attempts for transient failures.
    /// </summary>
    public int RetryCount { get; set; } = 3;

    /// <summary>
    /// Initial delay between retries (with exponential backoff).
    /// </summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromMilliseconds(200);
}
