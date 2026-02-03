using System.Security.Claims;
using Andy.Rbac.Client;
using Andy.Rbac.Configuration;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Andy.Rbac.Client.Tests;

public class HttpContextSubjectAccessorTests
{
    private readonly Mock<IHttpContextAccessor> _httpContextAccessorMock = new();
    private readonly RbacOptions _options = new() { ApplicationCode = "test-app" };
    private readonly HttpContextSubjectAccessor _accessor;

    public HttpContextSubjectAccessorTests()
    {
        _accessor = new HttpContextSubjectAccessor(
            _httpContextAccessorMock.Object,
            Options.Create(_options));
    }

    [Fact]
    public void IsAuthenticated_WithAuthenticatedUser_ReturnsTrue()
    {
        // Arrange
        var identity = new ClaimsIdentity(new[] { new Claim("sub", "user-123") }, "Bearer");
        var principal = new ClaimsPrincipal(identity);
        var httpContext = new DefaultHttpContext { User = principal };
        _httpContextAccessorMock.Setup(x => x.HttpContext).Returns(httpContext);

        // Act
        var result = _accessor.IsAuthenticated;

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void IsAuthenticated_WithUnauthenticatedUser_ReturnsFalse()
    {
        // Arrange
        var identity = new ClaimsIdentity(); // No authentication type
        var principal = new ClaimsPrincipal(identity);
        var httpContext = new DefaultHttpContext { User = principal };
        _httpContextAccessorMock.Setup(x => x.HttpContext).Returns(httpContext);

        // Act
        var result = _accessor.IsAuthenticated;

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void IsAuthenticated_WithNullHttpContext_ReturnsFalse()
    {
        // Arrange
        _httpContextAccessorMock.Setup(x => x.HttpContext).Returns((HttpContext?)null);

        // Act
        var result = _accessor.IsAuthenticated;

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void GetSubjectId_WithSubClaim_ReturnsSubjectId()
    {
        // Arrange
        _options.SubjectIdClaimType = "sub";
        var identity = new ClaimsIdentity(new[] { new Claim("sub", "user-123") }, "Bearer");
        var principal = new ClaimsPrincipal(identity);
        var httpContext = new DefaultHttpContext { User = principal };
        _httpContextAccessorMock.Setup(x => x.HttpContext).Returns(httpContext);

        // Act
        var result = _accessor.GetSubjectId();

        // Assert
        result.Should().Be("user-123");
    }

    [Fact]
    public void GetSubjectId_WithNameIdentifierClaim_FallsBackToNameIdentifier()
    {
        // Arrange
        _options.SubjectIdClaimType = "sub"; // Not present
        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "name-id-123")
        }, "Bearer");
        var principal = new ClaimsPrincipal(identity);
        var httpContext = new DefaultHttpContext { User = principal };
        _httpContextAccessorMock.Setup(x => x.HttpContext).Returns(httpContext);

        // Act
        var result = _accessor.GetSubjectId();

        // Assert
        result.Should().Be("name-id-123");
    }

    [Fact]
    public void GetSubjectId_WithNoClaims_ReturnsNull()
    {
        // Arrange
        var identity = new ClaimsIdentity(Array.Empty<Claim>(), "Bearer");
        var principal = new ClaimsPrincipal(identity);
        var httpContext = new DefaultHttpContext { User = principal };
        _httpContextAccessorMock.Setup(x => x.HttpContext).Returns(httpContext);

        // Act
        var result = _accessor.GetSubjectId();

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void GetProvider_WithIssClaim_ReturnsProvider()
    {
        // Arrange
        var identity = new ClaimsIdentity(new[] { new Claim("iss", "https://auth.example.com") }, "Bearer");
        var principal = new ClaimsPrincipal(identity);
        var httpContext = new DefaultHttpContext { User = principal };
        _httpContextAccessorMock.Setup(x => x.HttpContext).Returns(httpContext);

        // Act
        var result = _accessor.GetProvider();

        // Assert
        result.Should().Be("https://auth.example.com");
    }

    [Fact]
    public void GetProvider_WithCustomProviderClaimType_ReturnsCustomValue()
    {
        // Arrange
        _options.ProviderClaimType = "idp";
        var accessor = new HttpContextSubjectAccessor(
            _httpContextAccessorMock.Object,
            Options.Create(_options));

        var identity = new ClaimsIdentity(new[] { new Claim("idp", "custom-provider") }, "Bearer");
        var principal = new ClaimsPrincipal(identity);
        var httpContext = new DefaultHttpContext { User = principal };
        _httpContextAccessorMock.Setup(x => x.HttpContext).Returns(httpContext);

        // Act
        var result = accessor.GetProvider();

        // Assert
        result.Should().Be("custom-provider");
    }

    [Fact]
    public void GetEmail_WithEmailClaim_ReturnsEmail()
    {
        // Arrange
        var identity = new ClaimsIdentity(new[] { new Claim(ClaimTypes.Email, "user@example.com") }, "Bearer");
        var principal = new ClaimsPrincipal(identity);
        var httpContext = new DefaultHttpContext { User = principal };
        _httpContextAccessorMock.Setup(x => x.HttpContext).Returns(httpContext);

        // Act
        var result = _accessor.GetEmail();

        // Assert
        result.Should().Be("user@example.com");
    }

    [Fact]
    public void GetEmail_WithShortEmailClaim_ReturnsEmail()
    {
        // Arrange
        var identity = new ClaimsIdentity(new[] { new Claim("email", "user@example.com") }, "Bearer");
        var principal = new ClaimsPrincipal(identity);
        var httpContext = new DefaultHttpContext { User = principal };
        _httpContextAccessorMock.Setup(x => x.HttpContext).Returns(httpContext);

        // Act
        var result = _accessor.GetEmail();

        // Assert
        result.Should().Be("user@example.com");
    }

    [Fact]
    public void GetDisplayName_WithNameClaim_ReturnsDisplayName()
    {
        // Arrange
        var identity = new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "John Doe") }, "Bearer");
        var principal = new ClaimsPrincipal(identity);
        var httpContext = new DefaultHttpContext { User = principal };
        _httpContextAccessorMock.Setup(x => x.HttpContext).Returns(httpContext);

        // Act
        var result = _accessor.GetDisplayName();

        // Assert
        result.Should().Be("John Doe");
    }

    [Fact]
    public void GetDisplayName_WithShortNameClaim_ReturnsDisplayName()
    {
        // Arrange
        var identity = new ClaimsIdentity(new[] { new Claim("name", "Jane Doe") }, "Bearer");
        var principal = new ClaimsPrincipal(identity);
        var httpContext = new DefaultHttpContext { User = principal };
        _httpContextAccessorMock.Setup(x => x.HttpContext).Returns(httpContext);

        // Act
        var result = _accessor.GetDisplayName();

        // Assert
        result.Should().Be("Jane Doe");
    }

    [Fact]
    public void GetDisplayName_WithPreferredUsernameClaim_FallsBackToPreferredUsername()
    {
        // Arrange
        var identity = new ClaimsIdentity(new[] { new Claim("preferred_username", "johnd") }, "Bearer");
        var principal = new ClaimsPrincipal(identity);
        var httpContext = new DefaultHttpContext { User = principal };
        _httpContextAccessorMock.Setup(x => x.HttpContext).Returns(httpContext);

        // Act
        var result = _accessor.GetDisplayName();

        // Assert
        result.Should().Be("johnd");
    }

    [Fact]
    public void GetClaims_WithMultipleClaims_ReturnsAllClaims()
    {
        // Arrange
        var identity = new ClaimsIdentity(new[]
        {
            new Claim("sub", "user-123"),
            new Claim("email", "user@example.com"),
            new Claim("name", "John Doe")
        }, "Bearer");
        var principal = new ClaimsPrincipal(identity);
        var httpContext = new DefaultHttpContext { User = principal };
        _httpContextAccessorMock.Setup(x => x.HttpContext).Returns(httpContext);

        // Act
        var result = _accessor.GetClaims();

        // Assert
        result.Should().HaveCount(3);
        result.Should().ContainKey("sub");
        result.Should().ContainKey("email");
        result.Should().ContainKey("name");
        result["sub"].Should().Be("user-123");
    }

    [Fact]
    public void GetClaims_WithDuplicateClaimTypes_ReturnsFirstValue()
    {
        // Arrange
        var identity = new ClaimsIdentity(new[]
        {
            new Claim("role", "admin"),
            new Claim("role", "editor") // Duplicate
        }, "Bearer");
        var principal = new ClaimsPrincipal(identity);
        var httpContext = new DefaultHttpContext { User = principal };
        _httpContextAccessorMock.Setup(x => x.HttpContext).Returns(httpContext);

        // Act
        var result = _accessor.GetClaims();

        // Assert
        result.Should().ContainSingle();
        result["role"].Should().Be("admin"); // First value
    }

    [Fact]
    public void GetClaims_WithNullHttpContext_ReturnsEmptyDictionary()
    {
        // Arrange
        _httpContextAccessorMock.Setup(x => x.HttpContext).Returns((HttpContext?)null);

        // Act
        var result = _accessor.GetClaims();

        // Assert
        result.Should().BeEmpty();
    }
}
