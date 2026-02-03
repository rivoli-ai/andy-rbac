using Andy.Rbac.Abstractions;
using Andy.Rbac.Authorization;
using Andy.Rbac.Caching;
using Andy.Rbac.Configuration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Andy.Rbac.DependencyInjection;

/// <summary>
/// Extension methods for registering RBAC services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds RBAC services to the service collection using configuration.
    /// </summary>
    public static IServiceCollection AddRbac(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var section = configuration.GetSection(RbacOptions.SectionName);
        services.Configure<RbacOptions>(section);

        return services.AddRbacCore();
    }

    /// <summary>
    /// Adds RBAC services to the service collection using an options delegate.
    /// </summary>
    public static IServiceCollection AddRbac(
        this IServiceCollection services,
        Action<RbacOptions> configure)
    {
        services.Configure(configure);
        return services.AddRbacCore();
    }

    private static IServiceCollection AddRbacCore(this IServiceCollection services)
    {
        // Add memory cache for local caching
        services.AddMemoryCache();

        // Register cache
        services.AddSingleton<IRbacCache, InMemoryRbacCache>();

        // Register authorization handlers
        services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();
        services.AddScoped<IAuthorizationHandler, AnyPermissionAuthorizationHandler>();
        services.AddScoped<IAuthorizationHandler, RoleAuthorizationHandler>();

        // Register policy provider
        services.AddSingleton<IAuthorizationPolicyProvider, RbacPolicyProvider>();

        // Add HTTP context accessor for getting route values
        services.AddHttpContextAccessor();

        return services;
    }

    /// <summary>
    /// Adds the HTTP client for communicating with the RBAC API.
    /// Use this when not running the RBAC API in-process.
    /// </summary>
    public static IServiceCollection AddRbacHttpClient(this IServiceCollection services)
    {
        // This will be implemented in Andy.Rbac.Client
        return services;
    }

    /// <summary>
    /// Adds in-process RBAC services (when running RBAC API in the same process).
    /// </summary>
    public static IServiceCollection AddRbacInProcess(this IServiceCollection services)
    {
        // This will be implemented to use the local repository directly
        return services;
    }
}
