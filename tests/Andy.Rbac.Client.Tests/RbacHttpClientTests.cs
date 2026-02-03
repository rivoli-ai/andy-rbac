using System.Net;
using System.Net.Http.Json;
using Andy.Rbac.Abstractions;
using Andy.Rbac.Client;
using Andy.Rbac.Configuration;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using Xunit;

namespace Andy.Rbac.Client.Tests;

public class RbacHttpClientTests
{
    private readonly Mock<IRbacCache> _cacheMock = new();
    private readonly Mock<ILogger<RbacHttpClient>> _loggerMock = new();
    private readonly RbacOptions _options = new() { ApplicationCode = "test-app" };

    private RbacHttpClient CreateClient(HttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
        return new RbacHttpClient(httpClient, _cacheMock.Object, Options.Create(_options), _loggerMock.Object);
    }

    private Mock<HttpMessageHandler> CreateHandlerMock(HttpStatusCode statusCode, object? responseContent = null)
    {
        var handlerMock = new Mock<HttpMessageHandler>();
        var response = new HttpResponseMessage(statusCode);
        if (responseContent != null)
        {
            response.Content = JsonContent.Create(responseContent);
        }

        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(response);

        return handlerMock;
    }

    [Fact]
    public async Task HasPermissionAsync_WithCacheHit_ReturnsCachedResult()
    {
        // Arrange
        _options.Cache.Enabled = true;
        _cacheMock.Setup(c => c.GetPermissionsAsync("user-123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "test-app:document:read" });

        var handlerMock = CreateHandlerMock(HttpStatusCode.OK);
        var client = CreateClient(handlerMock.Object);

        // Act
        var result = await client.HasPermissionAsync("user-123", "test-app:document:read");

        // Assert
        result.Should().BeTrue();
        // Verify no HTTP call was made
        handlerMock.Protected().Verify(
            "SendAsync",
            Times.Never(),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task HasPermissionAsync_WithCacheMiss_CallsApi()
    {
        // Arrange
        _options.Cache.Enabled = true;
        _cacheMock.Setup(c => c.GetPermissionsAsync("user-123", It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<string>?)null);

        var handlerMock = CreateHandlerMock(HttpStatusCode.OK, new { Allowed = true, Reason = (string?)null });
        var client = CreateClient(handlerMock.Object);

        // Act
        var result = await client.HasPermissionAsync("user-123", "test-app:document:read");

        // Assert
        result.Should().BeTrue();
        handlerMock.Protected().Verify(
            "SendAsync",
            Times.Once(),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task HasPermissionAsync_WithCacheDisabled_AlwaysCallsApi()
    {
        // Arrange
        _options.Cache.Enabled = false;

        var handlerMock = CreateHandlerMock(HttpStatusCode.OK, new { Allowed = false, Reason = "Denied" });
        var client = CreateClient(handlerMock.Object);

        // Act
        var result = await client.HasPermissionAsync("user-123", "test-app:document:delete");

        // Assert
        result.Should().BeFalse();
        handlerMock.Protected().Verify(
            "SendAsync",
            Times.Once(),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task HasPermissionAsync_WithResourceInstance_BypassesCache()
    {
        // Arrange
        _options.Cache.Enabled = true;
        _cacheMock.Setup(c => c.GetPermissionsAsync("user-123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "test-app:document:read" });

        var handlerMock = CreateHandlerMock(HttpStatusCode.OK, new { Allowed = true, Reason = (string?)null });
        var client = CreateClient(handlerMock.Object);

        // Act
        var result = await client.HasPermissionAsync("user-123", "test-app:document:read", "doc-123");

        // Assert
        result.Should().BeTrue();
        // Should call API even with cache because resourceInstanceId is specified
        handlerMock.Protected().Verify(
            "SendAsync",
            Times.Once(),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task HasPermissionAsync_NormalizesShortPermission()
    {
        // Arrange
        _options.Cache.Enabled = false;
        var capturedRequest = (HttpRequestMessage?)null;

        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, ct) => capturedRequest = req)
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { Allowed = true, Reason = (string?)null })
            });

        var client = CreateClient(handlerMock.Object);

        // Act - use short permission format
        await client.HasPermissionAsync("user-123", "document:read");

        // Assert - permission should be normalized to include application code
        capturedRequest.Should().NotBeNull();
        var body = await capturedRequest!.Content!.ReadAsStringAsync();
        body.Should().Contain("test-app:document:read");
    }

    [Fact]
    public async Task HasAnyPermissionAsync_WithCacheHit_ReturnsTrueForMatching()
    {
        // Arrange
        _options.Cache.Enabled = true;
        _cacheMock.Setup(c => c.GetPermissionsAsync("user-123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "test-app:document:read" });

        var handlerMock = CreateHandlerMock(HttpStatusCode.OK);
        var client = CreateClient(handlerMock.Object);

        // Act
        var result = await client.HasAnyPermissionAsync("user-123", new[] { "test-app:document:write", "test-app:document:read" });

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task HasAnyPermissionAsync_WithNoMatching_ReturnsFalse()
    {
        // Arrange
        _options.Cache.Enabled = false;

        var handlerMock = CreateHandlerMock(HttpStatusCode.OK, new { Allowed = false, Reason = "Denied" });
        var client = CreateClient(handlerMock.Object);

        // Act
        var result = await client.HasAnyPermissionAsync("user-123", new[] { "test-app:document:delete" });

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task HasAllPermissionsAsync_WithAllCached_ReturnsTrue()
    {
        // Arrange
        _options.Cache.Enabled = true;
        _cacheMock.Setup(c => c.GetPermissionsAsync("user-123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "test-app:document:read", "test-app:document:write" });

        var handlerMock = CreateHandlerMock(HttpStatusCode.OK);
        var client = CreateClient(handlerMock.Object);

        // Act
        var result = await client.HasAllPermissionsAsync("user-123", new[] { "test-app:document:read", "test-app:document:write" });

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task HasAllPermissionsAsync_WithOneMissing_ReturnsFalse()
    {
        // Arrange
        _options.Cache.Enabled = true;
        _cacheMock.Setup(c => c.GetPermissionsAsync("user-123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "test-app:document:read" });

        var handlerMock = CreateHandlerMock(HttpStatusCode.OK);
        var client = CreateClient(handlerMock.Object);

        // Act
        var result = await client.HasAllPermissionsAsync("user-123", new[] { "test-app:document:read", "test-app:document:write" });

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task GetPermissionsAsync_WithCacheHit_ReturnsCached()
    {
        // Arrange
        _options.Cache.Enabled = true;
        var cachedPermissions = new List<string> { "test-app:document:read" };
        _cacheMock.Setup(c => c.GetPermissionsAsync("user-123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(cachedPermissions);

        var handlerMock = CreateHandlerMock(HttpStatusCode.OK);
        var client = CreateClient(handlerMock.Object);

        // Act
        var result = await client.GetPermissionsAsync("user-123");

        // Assert
        result.Should().BeEquivalentTo(cachedPermissions);
    }

    [Fact]
    public async Task GetPermissionsAsync_WithCacheMiss_CallsApiAndCaches()
    {
        // Arrange
        _options.Cache.Enabled = true;
        _cacheMock.Setup(c => c.GetPermissionsAsync("user-123", It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<string>?)null);

        var permissions = new List<string> { "test-app:document:read", "test-app:document:write" };
        var handlerMock = CreateHandlerMock(HttpStatusCode.OK, new { Permissions = permissions });
        var client = CreateClient(handlerMock.Object);

        // Act
        var result = await client.GetPermissionsAsync("user-123");

        // Assert
        result.Should().BeEquivalentTo(permissions);
        _cacheMock.Verify(c => c.SetPermissionsAsync("user-123", It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetPermissionsAsync_WithApplicationFilter_BypassesCache()
    {
        // Arrange
        _options.Cache.Enabled = true;
        var permissions = new List<string> { "other-app:doc:read" };
        var handlerMock = CreateHandlerMock(HttpStatusCode.OK, new { Permissions = permissions });
        var client = CreateClient(handlerMock.Object);

        // Act
        var result = await client.GetPermissionsAsync("user-123", "other-app");

        // Assert
        result.Should().BeEquivalentTo(permissions);
        // Cache should not be checked or set when applicationCode is specified
        _cacheMock.Verify(c => c.GetPermissionsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetRolesAsync_WithCacheHit_ReturnsCached()
    {
        // Arrange
        _options.Cache.Enabled = true;
        var cachedRoles = new List<string> { "admin", "editor" };
        _cacheMock.Setup(c => c.GetRolesAsync("user-123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(cachedRoles);

        var handlerMock = CreateHandlerMock(HttpStatusCode.OK);
        var client = CreateClient(handlerMock.Object);

        // Act
        var result = await client.GetRolesAsync("user-123");

        // Assert
        result.Should().BeEquivalentTo(cachedRoles);
    }

    [Fact]
    public async Task GetRolesAsync_WithCacheMiss_CallsApiAndCaches()
    {
        // Arrange
        _options.Cache.Enabled = true;
        _cacheMock.Setup(c => c.GetRolesAsync("user-123", It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<string>?)null);

        var roles = new List<string> { "admin" };
        var handlerMock = CreateHandlerMock(HttpStatusCode.OK, new { Roles = roles });
        var client = CreateClient(handlerMock.Object);

        // Act
        var result = await client.GetRolesAsync("user-123");

        // Assert
        result.Should().BeEquivalentTo(roles);
        _cacheMock.Verify(c => c.SetRolesAsync("user-123", It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AssignRoleAsync_CallsApiAndInvalidatesCache()
    {
        // Arrange
        var handlerMock = CreateHandlerMock(HttpStatusCode.OK);
        var client = CreateClient(handlerMock.Object);

        // Act
        await client.AssignRoleAsync("user-123", "admin");

        // Assert
        handlerMock.Protected().Verify(
            "SendAsync",
            Times.Once(),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>());
        _cacheMock.Verify(c => c.InvalidateAsync("user-123", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RevokeRoleAsync_CallsApiAndInvalidatesCache()
    {
        // Arrange
        var handlerMock = CreateHandlerMock(HttpStatusCode.OK);
        var client = CreateClient(handlerMock.Object);

        // Act
        await client.RevokeRoleAsync("user-123", "admin");

        // Assert
        _cacheMock.Verify(c => c.InvalidateAsync("user-123", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProvisionSubjectAsync_CallsApiAndReturnsSubjectInfo()
    {
        // Arrange
        var subjectResponse = new
        {
            Id = Guid.NewGuid(),
            ExternalId = "new-user",
            Provider = "test-provider",
            Email = "new@test.com",
            DisplayName = "New User",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            LastSeenAt = (DateTimeOffset?)null
        };
        var handlerMock = CreateHandlerMock(HttpStatusCode.OK, subjectResponse);
        var client = CreateClient(handlerMock.Object);

        // Act
        var result = await client.ProvisionSubjectAsync("new-user", "test-provider", "new@test.com", "New User");

        // Assert
        result.ExternalId.Should().Be("new-user");
        result.Provider.Should().Be("test-provider");
        result.Email.Should().Be("new@test.com");
    }

    [Fact]
    public async Task GrantInstancePermissionAsync_CallsApiAndInvalidatesCache()
    {
        // Arrange
        var handlerMock = CreateHandlerMock(HttpStatusCode.OK);
        var client = CreateClient(handlerMock.Object);

        // Act
        await client.GrantInstancePermissionAsync("user-123", "document", "doc-456", "read");

        // Assert
        _cacheMock.Verify(c => c.InvalidateAsync("user-123", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RevokeInstancePermissionAsync_CallsApiAndInvalidatesCache()
    {
        // Arrange
        var handlerMock = CreateHandlerMock(HttpStatusCode.OK);
        var client = CreateClient(handlerMock.Object);

        // Act
        await client.RevokeInstancePermissionAsync("user-123", "document", "doc-456", "read");

        // Assert
        _cacheMock.Verify(c => c.InvalidateAsync("user-123", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RegisterResourceInstanceAsync_CallsApi()
    {
        // Arrange
        var handlerMock = CreateHandlerMock(HttpStatusCode.OK);
        var client = CreateClient(handlerMock.Object);

        // Act
        await client.RegisterResourceInstanceAsync("document", "doc-456", "user-123", "My Document");

        // Assert
        handlerMock.Protected().Verify(
            "SendAsync",
            Times.Once(),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task RemoveResourceInstanceAsync_CallsApi()
    {
        // Arrange
        var handlerMock = CreateHandlerMock(HttpStatusCode.OK);
        var client = CreateClient(handlerMock.Object);

        // Act
        await client.RemoveResourceInstanceAsync("document", "doc-456");

        // Assert
        handlerMock.Protected().Verify(
            "SendAsync",
            Times.Once(),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>());
    }
}
