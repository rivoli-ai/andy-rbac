using Andy.Rbac.Configuration;
using FluentAssertions;
using Xunit;

namespace Andy.Rbac.Api.Tests.Configuration;

public class RbacOptionsTests
{
    [Fact]
    public void SectionName_HasCorrectValue()
    {
        // Assert
        RbacOptions.SectionName.Should().Be("Rbac");
    }

    [Fact]
    public void DefaultValues_AreSetCorrectly()
    {
        // Act
        var options = new RbacOptions { ApplicationCode = "test" };

        // Assert
        options.PreferGrpc.Should().BeTrue();
        options.AutoProvisionSubjects.Should().BeTrue();
        options.SubjectIdClaimType.Should().Be("sub");
        options.ProviderClaimType.Should().Be("iss");
        options.EnableAuditLogging.Should().BeTrue();
        options.ApiBaseUrl.Should().BeNull();
        options.GrpcEndpoint.Should().BeNull();
    }

    [Fact]
    public void Cache_DefaultValues_AreSetCorrectly()
    {
        // Act
        var options = new RbacOptions { ApplicationCode = "test" };

        // Assert
        options.Cache.Should().NotBeNull();
        options.Cache.Enabled.Should().BeTrue();
        options.Cache.Expiration.Should().Be(TimeSpan.FromMinutes(5));
        options.Cache.UseDistributedCache.Should().BeFalse();
        options.Cache.RedisConnectionString.Should().BeNull();
    }

    [Fact]
    public void HttpClient_DefaultValues_AreSetCorrectly()
    {
        // Act
        var options = new RbacOptions { ApplicationCode = "test" };

        // Assert
        options.HttpClient.Should().NotBeNull();
        options.HttpClient.Timeout.Should().Be(TimeSpan.FromSeconds(30));
        options.HttpClient.RetryCount.Should().Be(3);
        options.HttpClient.RetryDelay.Should().Be(TimeSpan.FromMilliseconds(200));
    }

    [Fact]
    public void Properties_CanBeSet()
    {
        // Act
        var options = new RbacOptions
        {
            ApplicationCode = "my-app",
            ApiBaseUrl = "https://rbac.example.com",
            GrpcEndpoint = "https://grpc.example.com",
            PreferGrpc = false,
            AutoProvisionSubjects = false,
            SubjectIdClaimType = "user_id",
            ProviderClaimType = "idp",
            EnableAuditLogging = false
        };

        // Assert
        options.ApplicationCode.Should().Be("my-app");
        options.ApiBaseUrl.Should().Be("https://rbac.example.com");
        options.GrpcEndpoint.Should().Be("https://grpc.example.com");
        options.PreferGrpc.Should().BeFalse();
        options.AutoProvisionSubjects.Should().BeFalse();
        options.SubjectIdClaimType.Should().Be("user_id");
        options.ProviderClaimType.Should().Be("idp");
        options.EnableAuditLogging.Should().BeFalse();
    }
}

public class RbacCacheOptionsTests
{
    [Fact]
    public void Properties_CanBeSet()
    {
        // Act
        var options = new RbacCacheOptions
        {
            Enabled = false,
            Expiration = TimeSpan.FromMinutes(10),
            UseDistributedCache = true,
            RedisConnectionString = "localhost:6379"
        };

        // Assert
        options.Enabled.Should().BeFalse();
        options.Expiration.Should().Be(TimeSpan.FromMinutes(10));
        options.UseDistributedCache.Should().BeTrue();
        options.RedisConnectionString.Should().Be("localhost:6379");
    }
}

public class HttpClientOptionsTests
{
    [Fact]
    public void Properties_CanBeSet()
    {
        // Act
        var options = new HttpClientOptions
        {
            Timeout = TimeSpan.FromSeconds(60),
            RetryCount = 5,
            RetryDelay = TimeSpan.FromMilliseconds(500)
        };

        // Assert
        options.Timeout.Should().Be(TimeSpan.FromSeconds(60));
        options.RetryCount.Should().Be(5);
        options.RetryDelay.Should().Be(TimeSpan.FromMilliseconds(500));
    }
}
