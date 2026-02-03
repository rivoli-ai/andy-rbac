using Andy.Rbac.Abstractions;
using Andy.Rbac.Authorization;
using Andy.Rbac.Caching;
using Andy.Rbac.Configuration;
using Andy.Rbac.DependencyInjection;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Andy.Rbac.Api.Tests.DependencyInjection;

public class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddRbac_WithConfiguration_RegistersOptions()
    {
        // Arrange
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Rbac:ApplicationCode"] = "test-app",
                ["Rbac:SubjectIdClaimType"] = "sub"
            })
            .Build();

        // Act
        services.AddRbac(configuration);
        var provider = services.BuildServiceProvider();

        // Assert
        var options = provider.GetRequiredService<IOptions<RbacOptions>>().Value;
        options.ApplicationCode.Should().Be("test-app");
        options.SubjectIdClaimType.Should().Be("sub");
    }

    [Fact]
    public void AddRbac_WithDelegate_RegistersOptions()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddRbac(options =>
        {
            options.ApplicationCode = "my-app";
            options.ProviderClaimType = "idp";
        });
        var provider = services.BuildServiceProvider();

        // Assert
        var options = provider.GetRequiredService<IOptions<RbacOptions>>().Value;
        options.ApplicationCode.Should().Be("my-app");
        options.ProviderClaimType.Should().Be("idp");
    }

    [Fact]
    public void AddRbac_RegistersMemoryCache()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddRbac(options => { });
        var provider = services.BuildServiceProvider();

        // Assert
        var memoryCache = provider.GetService<IMemoryCache>();
        memoryCache.Should().NotBeNull();
    }

    [Fact]
    public void AddRbac_RegistersRbacCache()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddRbac(options => { });
        var provider = services.BuildServiceProvider();

        // Assert
        var rbacCache = provider.GetService<IRbacCache>();
        rbacCache.Should().NotBeNull();
        rbacCache.Should().BeOfType<InMemoryRbacCache>();
    }

    [Fact]
    public void AddRbac_RegistersRbacCacheAsSingleton()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddRbac(options => { });
        var provider = services.BuildServiceProvider();

        // Assert
        var cache1 = provider.GetRequiredService<IRbacCache>();
        var cache2 = provider.GetRequiredService<IRbacCache>();
        cache1.Should().BeSameAs(cache2);
    }

    [Fact]
    public void AddRbac_RegistersAuthorizationPolicyProvider()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddRbac(options => { });
        var provider = services.BuildServiceProvider();

        // Assert
        var policyProvider = provider.GetService<IAuthorizationPolicyProvider>();
        policyProvider.Should().NotBeNull();
        policyProvider.Should().BeOfType<RbacPolicyProvider>();
    }

    [Fact]
    public void AddRbac_RegistersAuthorizationPolicyProviderAsSingleton()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddRbac(options => { });
        var provider = services.BuildServiceProvider();

        // Assert
        var provider1 = provider.GetRequiredService<IAuthorizationPolicyProvider>();
        var provider2 = provider.GetRequiredService<IAuthorizationPolicyProvider>();
        provider1.Should().BeSameAs(provider2);
    }

    [Fact]
    public void AddRbac_RegistersPermissionAuthorizationHandler()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IPermissionService>(new MockPermissionService());
        services.AddSingleton<ICurrentSubjectAccessor>(new MockSubjectAccessor());

        // Act
        services.AddRbac(options => { });
        var provider = services.BuildServiceProvider();

        // Assert
        var handlers = provider.GetServices<IAuthorizationHandler>();
        handlers.Should().ContainSingle(h => h is PermissionAuthorizationHandler);
    }

    [Fact]
    public void AddRbac_RegistersAnyPermissionAuthorizationHandler()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IPermissionService>(new MockPermissionService());
        services.AddSingleton<ICurrentSubjectAccessor>(new MockSubjectAccessor());

        // Act
        services.AddRbac(options => { });
        var provider = services.BuildServiceProvider();

        // Assert
        var handlers = provider.GetServices<IAuthorizationHandler>();
        handlers.Should().ContainSingle(h => h is AnyPermissionAuthorizationHandler);
    }

    [Fact]
    public void AddRbac_RegistersRoleAuthorizationHandler()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IPermissionService>(new MockPermissionService());
        services.AddSingleton<ICurrentSubjectAccessor>(new MockSubjectAccessor());

        // Act
        services.AddRbac(options => { });
        var provider = services.BuildServiceProvider();

        // Assert
        var handlers = provider.GetServices<IAuthorizationHandler>();
        handlers.Should().ContainSingle(h => h is RoleAuthorizationHandler);
    }

    [Fact]
    public void AddRbacHttpClient_ReturnsServiceCollection()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var result = services.AddRbacHttpClient();

        // Assert
        result.Should().BeSameAs(services);
    }

    [Fact]
    public void AddRbacInProcess_ReturnsServiceCollection()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var result = services.AddRbacInProcess();

        // Assert
        result.Should().BeSameAs(services);
    }

    [Fact]
    public void AddRbac_WithConfiguration_ReturnsSameServiceCollection()
    {
        // Arrange
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();

        // Act
        var result = services.AddRbac(configuration);

        // Assert
        result.Should().BeSameAs(services);
    }

    [Fact]
    public void AddRbac_WithDelegate_ReturnsSameServiceCollection()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var result = services.AddRbac(options => { });

        // Assert
        result.Should().BeSameAs(services);
    }

    private class MockPermissionService : IPermissionService
    {
        public Task<bool> HasPermissionAsync(string subjectId, string permission, string? resourceInstanceId = null, CancellationToken ct = default)
            => Task.FromResult(false);

        public Task<bool> HasAnyPermissionAsync(string subjectId, IEnumerable<string> permissions, string? resourceInstanceId = null, CancellationToken ct = default)
            => Task.FromResult(false);

        public Task<bool> HasAllPermissionsAsync(string subjectId, IEnumerable<string> permissions, string? resourceInstanceId = null, CancellationToken ct = default)
            => Task.FromResult(false);

        public Task<IReadOnlyList<string>> GetPermissionsAsync(string subjectId, string? applicationCode = null, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());

        public Task<IReadOnlyList<string>> GetRolesAsync(string subjectId, string? applicationCode = null, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
    }

    private class MockSubjectAccessor : ICurrentSubjectAccessor
    {
        public bool IsAuthenticated => false;
        public string? GetSubjectId() => null;
        public string? GetProvider() => null;
        public string? GetEmail() => null;
        public string? GetDisplayName() => null;
        public IReadOnlyDictionary<string, string> GetClaims() => new Dictionary<string, string>();
    }
}
