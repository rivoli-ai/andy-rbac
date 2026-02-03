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
    /// Adds RBAC services with HTTP client using options delegate.
    /// </summary>
    public static IServiceCollection AddRbacClient(
        this IServiceCollection services,
        Action<RbacOptions> configure)
    {
        // Add core RBAC services
        services.AddRbac(configure);

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
