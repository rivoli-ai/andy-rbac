using Microsoft.Extensions.Hosting;

namespace Andy.Rbac.Api;

// Named accessors for the three deployment modes (Development / Docker
// / Embedded). See andy-service-template/docs/ports.md. Duplicated per
// service — keep EmbeddedEnvironmentName in lock-step with the Swift
// constant ServiceEnvironment.embeddedEnvironmentName.
public static class HostEnvironmentExtensions
{
    public const string EmbeddedEnvironmentName = "Embedded";
    public const string DockerEnvironmentName = "Docker";

    public static bool IsEmbedded(this IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        return environment.IsEnvironment(EmbeddedEnvironmentName);
    }

    public static bool IsDocker(this IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        return environment.IsEnvironment(DockerEnvironmentName);
    }

    public static bool IsLocalOrEmbedded(this IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        return !environment.IsProduction();
    }
}
