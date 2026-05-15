using Andy.Auth.M2MClient;
using Andy.Rbac.Abstractions;
using Andy.Rbac.Configuration;
using Andy.Rbac.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Extensions.Http;

namespace Andy.Rbac.Client;

/// <summary>
/// Extension methods for registering RBAC client services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds RBAC services with HTTP client for remote RBAC API.
    /// </summary>
    public static IServiceCollection AddRbacClient(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Add core RBAC services
        services.AddRbac(configuration);

        // Register subject accessor
        services.AddScoped<ICurrentSubjectAccessor, HttpContextSubjectAccessor>();

        // Configure HTTP client
        services.AddHttpClient<IRbacClient, RbacHttpClient>((sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<RbacOptions>>().Value;
            if (string.IsNullOrEmpty(options.ApiBaseUrl))
                throw new InvalidOperationException("RbacOptions.ApiBaseUrl must be configured");

            client.BaseAddress = new Uri(options.ApiBaseUrl);
            client.Timeout = options.HttpClient.Timeout;
        })
        .AddPolicyHandler((sp, _) =>
        {
            var options = sp.GetRequiredService<IOptions<RbacOptions>>().Value;
            return GetRetryPolicy(options.HttpClient);
        });

        // Register IPermissionService as the client
        services.AddScoped<IPermissionService>(sp => sp.GetRequiredService<IRbacClient>());

        return services;
    }

    /// <summary>
    /// Registers the RBAC client AND <c>Andy.Auth.M2MClient</c>
    /// infrastructure, then wires the outbound <see cref="RbacHttpClient"/>
    /// through <see cref="ServiceBearerHandler"/> so every request to
    /// <c>/api/Check</c> (and friends) carries an M2M-acquired Bearer.
    ///
    /// Before this overload existed, every consumer of
    /// <see cref="IRbacClient"/> in the embedded-services setup called
    /// the no-auth <see cref="AddRbacClient(IServiceCollection, IConfiguration)"/>
    /// — the resulting <c>HttpClient</c> sent requests with no
    /// Authorization header, andy-rbac's <c>[Authorize]</c> middleware
    /// denied them, the proxy mapped that into <c>502 Bad Gateway</c>,
    /// and downstream services' permission handlers (e.g.
    /// <c>RbacPermissionHandler</c> in andy-agents) caught the exception
    /// and returned <c>Deny</c>. Every Conductor panel that gated on
    /// <c>[RequirePermission(...)]</c> surfaced 403. See
    /// rivoli-ai/andy-rbac#75 for the full diagnosis.
    ///
    /// The host service's <c>AndyAuth</c> configuration section must
    /// define <c>ClientId</c>, <c>ClientSecretEnvVar</c>, and either
    /// <c>TokenEndpoint</c> or <c>Authority</c> — same wire-up
    /// requirement as <c>AddAndySettingsClientWithM2M</c>.
    ///
    /// Idempotent — calling either overload after the other is safe.
    /// </summary>
    public static IServiceCollection AddRbacClientWithM2M(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Brings in IServiceTokenProvider (ClientCredentialsTokenProvider
        // + a coalescing cache) and registers a hosted refresher.
        services.AddAndyAuthM2M(configuration);

        // Add core RBAC services
        services.AddRbac(configuration);

        // Register subject accessor
        services.AddScoped<ICurrentSubjectAccessor, HttpContextSubjectAccessor>();

        // The handler is transient/scoped via AddHttpMessageHandler;
        // it pulls IServiceTokenProvider from the request-scoped DI
        // container on every send so the cached token + 401-retry are
        // honoured per-request.
        services.AddTransient<ServiceBearerHandler>();

        // Configure HTTP client with the bearer handler in the pipeline.
        services.AddHttpClient<IRbacClient, RbacHttpClient>((sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<RbacOptions>>().Value;
            if (string.IsNullOrEmpty(options.ApiBaseUrl))
                throw new InvalidOperationException("RbacOptions.ApiBaseUrl must be configured");

            client.BaseAddress = new Uri(options.ApiBaseUrl);
            client.Timeout = options.HttpClient.Timeout;
        })
        .AddHttpMessageHandler<ServiceBearerHandler>()
        .AddPolicyHandler((sp, _) =>
        {
            var options = sp.GetRequiredService<IOptions<RbacOptions>>().Value;
            return GetRetryPolicy(options.HttpClient);
        });

        // Register IPermissionService as the client
        services.AddScoped<IPermissionService>(sp => sp.GetRequiredService<IRbacClient>());

        return services;
    }

    /// <summary>
    /// Adds RBAC services with HTTP client using options delegate.
    /// </summary>
    public static IServiceCollection AddRbacClient(
        this IServiceCollection services,
        Action<RbacOptions> configure)
    {
        // Add core RBAC services
        services.AddRbac(configure);

        // Register subject accessor
        services.AddScoped<ICurrentSubjectAccessor, HttpContextSubjectAccessor>();

        // Configure HTTP client
        services.AddHttpClient<IRbacClient, RbacHttpClient>((sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<RbacOptions>>().Value;
            if (string.IsNullOrEmpty(options.ApiBaseUrl))
                throw new InvalidOperationException("RbacOptions.ApiBaseUrl must be configured");

            client.BaseAddress = new Uri(options.ApiBaseUrl);
            client.Timeout = options.HttpClient.Timeout;
        })
        .AddPolicyHandler((sp, _) =>
        {
            var options = sp.GetRequiredService<IOptions<RbacOptions>>().Value;
            return GetRetryPolicy(options.HttpClient);
        });

        // Register IPermissionService as the client
        services.AddScoped<IPermissionService>(sp => sp.GetRequiredService<IRbacClient>());

        return services;
    }

    private static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy(HttpClientOptions options)
    {
        return HttpPolicyExtensions
            .HandleTransientHttpError()
            .WaitAndRetryAsync(
                options.RetryCount,
                retryAttempt => TimeSpan.FromMilliseconds(
                    options.RetryDelay.TotalMilliseconds * Math.Pow(2, retryAttempt - 1)));
    }
}
