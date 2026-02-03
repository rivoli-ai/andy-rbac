using System.Security.Claims;
using Andy.Rbac.Abstractions;
using Andy.Rbac.Authorization;
using Andy.Rbac.Configuration;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Andy.Rbac.Api.Tests.Authorization;

public class PermissionAuthorizationHandlerTests
{
    private readonly Mock<IPermissionService> _permissionServiceMock = new();
    private readonly Mock<ICurrentSubjectAccessor> _subjectAccessorMock = new();
    private readonly Mock<IHttpContextAccessor> _httpContextAccessorMock = new();
    private readonly Mock<ILogger<PermissionAuthorizationHandler>> _loggerMock = new();
    private readonly RbacOptions _options = new() { ApplicationCode = "test-app" };
    private readonly PermissionAuthorizationHandler _handler;

    public PermissionAuthorizationHandlerTests()
    {
        _handler = new PermissionAuthorizationHandler(
            _permissionServiceMock.Object,
            _subjectAccessorMock.Object,
            _httpContextAccessorMock.Object,
            Options.Create(_options),
            _loggerMock.Object);
    }

    [Fact]
    public async Task HandleRequirementAsync_NoSubjectId_DoesNotSucceed()
    {
        // Arrange
        _subjectAccessorMock.Setup(x => x.GetSubjectId()).Returns((string?)null);
        var requirement = new PermissionRequirement("document:read");
        var context = CreateAuthorizationContext(requirement);

        // Act
        await _handler.HandleAsync(context);

        // Assert
        context.HasSucceeded.Should().BeFalse();
    }

    [Fact]
    public async Task HandleRequirementAsync_WithPermission_Succeeds()
    {
        // Arrange
        _subjectAccessorMock.Setup(x => x.GetSubjectId()).Returns("user-123");
        _permissionServiceMock
            .Setup(x => x.HasPermissionAsync("user-123", "test-app:document:read", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var requirement = new PermissionRequirement("document:read");
        var context = CreateAuthorizationContext(requirement);

        // Act
        await _handler.HandleAsync(context);

        // Assert
        context.HasSucceeded.Should().BeTrue();
    }

    [Fact]
    public async Task HandleRequirementAsync_WithoutPermission_DoesNotSucceed()
    {
        // Arrange
        _subjectAccessorMock.Setup(x => x.GetSubjectId()).Returns("user-123");
        _permissionServiceMock
            .Setup(x => x.HasPermissionAsync("user-123", "test-app:document:delete", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var requirement = new PermissionRequirement("document:delete");
        var context = CreateAuthorizationContext(requirement);

        // Act
        await _handler.HandleAsync(context);

        // Assert
        context.HasSucceeded.Should().BeFalse();
    }

    [Fact]
    public async Task HandleRequirementAsync_FullPermission_DoesNotNormalize()
    {
        // Arrange
        _subjectAccessorMock.Setup(x => x.GetSubjectId()).Returns("user-123");
        _permissionServiceMock
            .Setup(x => x.HasPermissionAsync("user-123", "other-app:document:read", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var requirement = new PermissionRequirement("other-app:document:read");
        var context = CreateAuthorizationContext(requirement);

        // Act
        await _handler.HandleAsync(context);

        // Assert
        context.HasSucceeded.Should().BeTrue();
        _permissionServiceMock.Verify(
            x => x.HasPermissionAsync("user-123", "other-app:document:read", null, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleRequirementAsync_WithResourceIdParameter_ExtractsFromRoute()
    {
        // Arrange
        _subjectAccessorMock.Setup(x => x.GetSubjectId()).Returns("user-123");

        var httpContext = new DefaultHttpContext();
        httpContext.Request.RouteValues["documentId"] = "doc-456";
        _httpContextAccessorMock.Setup(x => x.HttpContext).Returns(httpContext);

        _permissionServiceMock
            .Setup(x => x.HasPermissionAsync("user-123", "test-app:document:read", "doc-456", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var requirement = new PermissionRequirement("document:read", "documentId");
        var context = CreateAuthorizationContext(requirement);

        // Act
        await _handler.HandleAsync(context);

        // Assert
        context.HasSucceeded.Should().BeTrue();
        _permissionServiceMock.Verify(
            x => x.HasPermissionAsync("user-123", "test-app:document:read", "doc-456", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleRequirementAsync_WithResourceIdParameter_ExtractsFromQueryString()
    {
        // Arrange
        _subjectAccessorMock.Setup(x => x.GetSubjectId()).Returns("user-123");

        var httpContext = new DefaultHttpContext();
        httpContext.Request.QueryString = new QueryString("?documentId=doc-789");
        _httpContextAccessorMock.Setup(x => x.HttpContext).Returns(httpContext);

        _permissionServiceMock
            .Setup(x => x.HasPermissionAsync("user-123", "test-app:document:read", "doc-789", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var requirement = new PermissionRequirement("document:read", "documentId");
        var context = CreateAuthorizationContext(requirement);

        // Act
        await _handler.HandleAsync(context);

        // Assert
        context.HasSucceeded.Should().BeTrue();
    }

    [Fact]
    public async Task HandleRequirementAsync_WithMissingResourceIdParameter_PassesNull()
    {
        // Arrange
        _subjectAccessorMock.Setup(x => x.GetSubjectId()).Returns("user-123");

        var httpContext = new DefaultHttpContext();
        _httpContextAccessorMock.Setup(x => x.HttpContext).Returns(httpContext);

        _permissionServiceMock
            .Setup(x => x.HasPermissionAsync("user-123", "test-app:document:read", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var requirement = new PermissionRequirement("document:read", "missingParam");
        var context = CreateAuthorizationContext(requirement);

        // Act
        await _handler.HandleAsync(context);

        // Assert
        context.HasSucceeded.Should().BeTrue();
    }

    private AuthorizationHandlerContext CreateAuthorizationContext(IAuthorizationRequirement requirement)
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "user-123")
        }, "test"));

        return new AuthorizationHandlerContext(
            new[] { requirement },
            user,
            null);
    }
}

public class AnyPermissionAuthorizationHandlerTests
{
    private readonly Mock<IPermissionService> _permissionServiceMock = new();
    private readonly Mock<ICurrentSubjectAccessor> _subjectAccessorMock = new();
    private readonly Mock<ILogger<AnyPermissionAuthorizationHandler>> _loggerMock = new();
    private readonly RbacOptions _options = new() { ApplicationCode = "test-app" };
    private readonly AnyPermissionAuthorizationHandler _handler;

    public AnyPermissionAuthorizationHandlerTests()
    {
        _handler = new AnyPermissionAuthorizationHandler(
            _permissionServiceMock.Object,
            _subjectAccessorMock.Object,
            Options.Create(_options),
            _loggerMock.Object);
    }

    [Fact]
    public async Task HandleRequirementAsync_NoSubjectId_DoesNotSucceed()
    {
        // Arrange
        _subjectAccessorMock.Setup(x => x.GetSubjectId()).Returns((string?)null);
        var requirement = new AnyPermissionRequirement(new[] { "document:read" });
        var context = CreateAuthorizationContext(requirement);

        // Act
        await _handler.HandleAsync(context);

        // Assert
        context.HasSucceeded.Should().BeFalse();
    }

    [Fact]
    public async Task HandleRequirementAsync_WithAnyPermission_Succeeds()
    {
        // Arrange
        _subjectAccessorMock.Setup(x => x.GetSubjectId()).Returns("user-123");
        _permissionServiceMock
            .Setup(x => x.HasAnyPermissionAsync(
                "user-123",
                It.Is<IEnumerable<string>>(p => p.Contains("test-app:document:read")),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var requirement = new AnyPermissionRequirement(new[] { "document:read", "document:write" });
        var context = CreateAuthorizationContext(requirement);

        // Act
        await _handler.HandleAsync(context);

        // Assert
        context.HasSucceeded.Should().BeTrue();
    }

    [Fact]
    public async Task HandleRequirementAsync_WithoutAnyPermission_DoesNotSucceed()
    {
        // Arrange
        _subjectAccessorMock.Setup(x => x.GetSubjectId()).Returns("user-123");
        _permissionServiceMock
            .Setup(x => x.HasAnyPermissionAsync("user-123", It.IsAny<IEnumerable<string>>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var requirement = new AnyPermissionRequirement(new[] { "document:delete" });
        var context = CreateAuthorizationContext(requirement);

        // Act
        await _handler.HandleAsync(context);

        // Assert
        context.HasSucceeded.Should().BeFalse();
    }

    private AuthorizationHandlerContext CreateAuthorizationContext(IAuthorizationRequirement requirement)
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "user-123")
        }, "test"));

        return new AuthorizationHandlerContext(
            new[] { requirement },
            user,
            null);
    }
}

public class RoleAuthorizationHandlerTests
{
    private readonly Mock<IPermissionService> _permissionServiceMock = new();
    private readonly Mock<ICurrentSubjectAccessor> _subjectAccessorMock = new();
    private readonly Mock<ILogger<RoleAuthorizationHandler>> _loggerMock = new();
    private readonly RoleAuthorizationHandler _handler;

    public RoleAuthorizationHandlerTests()
    {
        _handler = new RoleAuthorizationHandler(
            _permissionServiceMock.Object,
            _subjectAccessorMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task HandleRequirementAsync_NoSubjectId_DoesNotSucceed()
    {
        // Arrange
        _subjectAccessorMock.Setup(x => x.GetSubjectId()).Returns((string?)null);
        var requirement = new RoleRequirement("admin");
        var context = CreateAuthorizationContext(requirement);

        // Act
        await _handler.HandleAsync(context);

        // Assert
        context.HasSucceeded.Should().BeFalse();
    }

    [Fact]
    public async Task HandleRequirementAsync_WithRole_Succeeds()
    {
        // Arrange
        _subjectAccessorMock.Setup(x => x.GetSubjectId()).Returns("user-123");
        _permissionServiceMock
            .Setup(x => x.GetRolesAsync("user-123", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "admin", "editor" });

        var requirement = new RoleRequirement("admin");
        var context = CreateAuthorizationContext(requirement);

        // Act
        await _handler.HandleAsync(context);

        // Assert
        context.HasSucceeded.Should().BeTrue();
    }

    [Fact]
    public async Task HandleRequirementAsync_WithoutRole_DoesNotSucceed()
    {
        // Arrange
        _subjectAccessorMock.Setup(x => x.GetSubjectId()).Returns("user-123");
        _permissionServiceMock
            .Setup(x => x.GetRolesAsync("user-123", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "viewer" });

        var requirement = new RoleRequirement("admin");
        var context = CreateAuthorizationContext(requirement);

        // Act
        await _handler.HandleAsync(context);

        // Assert
        context.HasSucceeded.Should().BeFalse();
    }

    [Fact]
    public async Task HandleRequirementAsync_RoleMatchIsCaseInsensitive()
    {
        // Arrange
        _subjectAccessorMock.Setup(x => x.GetSubjectId()).Returns("user-123");
        _permissionServiceMock
            .Setup(x => x.GetRolesAsync("user-123", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "ADMIN" });

        var requirement = new RoleRequirement("admin");
        var context = CreateAuthorizationContext(requirement);

        // Act
        await _handler.HandleAsync(context);

        // Assert
        context.HasSucceeded.Should().BeTrue();
    }

    private AuthorizationHandlerContext CreateAuthorizationContext(IAuthorizationRequirement requirement)
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "user-123")
        }, "test"));

        return new AuthorizationHandlerContext(
            new[] { requirement },
            user,
            null);
    }
}
