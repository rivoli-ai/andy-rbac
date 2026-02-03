using Andy.Rbac.Api.Services;
using FluentAssertions;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

// Use explicit type aliases for gRPC types to avoid conflicts with Client types
using CheckPermissionReq = Andy.Rbac.Grpc.CheckPermissionRequest;
using CheckAnyPermissionReq = Andy.Rbac.Grpc.CheckAnyPermissionRequest;
using GetPermissionsReq = Andy.Rbac.Grpc.GetPermissionsRequest;
using GetRolesReq = Andy.Rbac.Grpc.GetRolesRequest;

namespace Andy.Rbac.Api.Tests.Grpc;

public class RbacGrpcServiceTests
{
    private readonly Mock<IPermissionEvaluator> _evaluatorMock = new();
    private readonly Mock<ILogger<RbacGrpcService>> _loggerMock = new();
    private readonly RbacGrpcService _service;

    public RbacGrpcServiceTests()
    {
        _service = new RbacGrpcService(_evaluatorMock.Object, _loggerMock.Object);
    }

    [Fact]
    public async Task CheckPermission_WithAllowedPermission_ReturnsAllowed()
    {
        // Arrange
        _evaluatorMock
            .Setup(x => x.CheckPermissionAsync("user-123", "test-app:document:read", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PermissionCheckResult(true, null));

        var request = new CheckPermissionReq
        {
            SubjectId = "user-123",
            Permission = "test-app:document:read"
        };

        // Act
        var response = await _service.CheckPermission(request, CreateServerCallContext());

        // Assert
        response.Allowed.Should().BeTrue();
        response.Reason.Should().BeEmpty();
    }

    [Fact]
    public async Task CheckPermission_WithDeniedPermission_ReturnsDenied()
    {
        // Arrange
        _evaluatorMock
            .Setup(x => x.CheckPermissionAsync("user-123", "test-app:document:delete", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PermissionCheckResult(false, "Permission denied"));

        var request = new CheckPermissionReq
        {
            SubjectId = "user-123",
            Permission = "test-app:document:delete"
        };

        // Act
        var response = await _service.CheckPermission(request, CreateServerCallContext());

        // Assert
        response.Allowed.Should().BeFalse();
        response.Reason.Should().Be("Permission denied");
    }

    [Fact]
    public async Task CheckPermission_WithResourceInstanceId_PassesToEvaluator()
    {
        // Arrange
        _evaluatorMock
            .Setup(x => x.CheckPermissionAsync("user-123", "test-app:document:read", "doc-456", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PermissionCheckResult(true, null));

        var request = new CheckPermissionReq
        {
            SubjectId = "user-123",
            Permission = "test-app:document:read",
            ResourceInstanceId = "doc-456"
        };

        // Act
        var response = await _service.CheckPermission(request, CreateServerCallContext());

        // Assert
        response.Allowed.Should().BeTrue();
        _evaluatorMock.Verify(
            x => x.CheckPermissionAsync("user-123", "test-app:document:read", "doc-456", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task CheckAnyPermission_WithOneAllowed_ReturnsAllowed()
    {
        // Arrange
        var permissions = new[] { "test-app:document:read", "test-app:document:write" };
        _evaluatorMock
            .Setup(x => x.CheckAnyPermissionAsync("user-123", permissions, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PermissionCheckResult(true, null));

        var request = new CheckAnyPermissionReq
        {
            SubjectId = "user-123"
        };
        request.Permissions.AddRange(permissions);

        // Act
        var response = await _service.CheckAnyPermission(request, CreateServerCallContext());

        // Assert
        response.Allowed.Should().BeTrue();
    }

    [Fact]
    public async Task CheckAnyPermission_WithNoneAllowed_ReturnsDenied()
    {
        // Arrange
        var permissions = new[] { "test-app:document:delete" };
        _evaluatorMock
            .Setup(x => x.CheckAnyPermissionAsync("user-123", permissions, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PermissionCheckResult(false, "None of the required permissions found"));

        var request = new CheckAnyPermissionReq
        {
            SubjectId = "user-123"
        };
        request.Permissions.AddRange(permissions);

        // Act
        var response = await _service.CheckAnyPermission(request, CreateServerCallContext());

        // Assert
        response.Allowed.Should().BeFalse();
        response.Reason.Should().Contain("permissions");
    }

    [Fact]
    public async Task CheckAnyPermission_WithResourceInstanceId_PassesToEvaluator()
    {
        // Arrange
        var permissions = new[] { "test-app:document:read" };
        _evaluatorMock
            .Setup(x => x.CheckAnyPermissionAsync("user-123", permissions, "doc-456", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PermissionCheckResult(true, null));

        var request = new CheckAnyPermissionReq
        {
            SubjectId = "user-123",
            ResourceInstanceId = "doc-456"
        };
        request.Permissions.AddRange(permissions);

        // Act
        var response = await _service.CheckAnyPermission(request, CreateServerCallContext());

        // Assert
        response.Allowed.Should().BeTrue();
        _evaluatorMock.Verify(
            x => x.CheckAnyPermissionAsync("user-123", permissions, "doc-456", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetPermissions_ReturnsAllPermissions()
    {
        // Arrange
        var permissions = new List<string>
        {
            "test-app:document:read",
            "test-app:document:write"
        };
        _evaluatorMock
            .Setup(x => x.GetPermissionsAsync("user-123", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(permissions);

        var request = new GetPermissionsReq
        {
            SubjectId = "user-123"
        };

        // Act
        var response = await _service.GetPermissions(request, CreateServerCallContext());

        // Assert
        response.Permissions.Should().HaveCount(2);
        response.Permissions.Should().Contain("test-app:document:read");
        response.Permissions.Should().Contain("test-app:document:write");
    }

    [Fact]
    public async Task GetPermissions_WithApplicationCode_PassesToEvaluator()
    {
        // Arrange
        var permissions = new List<string> { "test-app:document:read" };
        _evaluatorMock
            .Setup(x => x.GetPermissionsAsync("user-123", "test-app", It.IsAny<CancellationToken>()))
            .ReturnsAsync(permissions);

        var request = new GetPermissionsReq
        {
            SubjectId = "user-123",
            ApplicationCode = "test-app"
        };

        // Act
        var response = await _service.GetPermissions(request, CreateServerCallContext());

        // Assert
        response.Permissions.Should().ContainSingle();
        _evaluatorMock.Verify(
            x => x.GetPermissionsAsync("user-123", "test-app", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetPermissions_WithEmptyResult_ReturnsEmptyList()
    {
        // Arrange
        _evaluatorMock
            .Setup(x => x.GetPermissionsAsync("user-123", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());

        var request = new GetPermissionsReq
        {
            SubjectId = "user-123"
        };

        // Act
        var response = await _service.GetPermissions(request, CreateServerCallContext());

        // Assert
        response.Permissions.Should().BeEmpty();
    }

    [Fact]
    public async Task GetRoles_ReturnsAllRoles()
    {
        // Arrange
        var roles = new List<string> { "admin", "editor" };
        _evaluatorMock
            .Setup(x => x.GetRolesAsync("user-123", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(roles);

        var request = new GetRolesReq
        {
            SubjectId = "user-123"
        };

        // Act
        var response = await _service.GetRoles(request, CreateServerCallContext());

        // Assert
        response.Roles.Should().HaveCount(2);
        response.Roles.Should().Contain("admin");
        response.Roles.Should().Contain("editor");
    }

    [Fact]
    public async Task GetRoles_WithApplicationCode_PassesToEvaluator()
    {
        // Arrange
        var roles = new List<string> { "admin" };
        _evaluatorMock
            .Setup(x => x.GetRolesAsync("user-123", "test-app", It.IsAny<CancellationToken>()))
            .ReturnsAsync(roles);

        var request = new GetRolesReq
        {
            SubjectId = "user-123",
            ApplicationCode = "test-app"
        };

        // Act
        var response = await _service.GetRoles(request, CreateServerCallContext());

        // Assert
        response.Roles.Should().ContainSingle();
        _evaluatorMock.Verify(
            x => x.GetRolesAsync("user-123", "test-app", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetRoles_WithEmptyResult_ReturnsEmptyList()
    {
        // Arrange
        _evaluatorMock
            .Setup(x => x.GetRolesAsync("user-123", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());

        var request = new GetRolesReq
        {
            SubjectId = "user-123"
        };

        // Act
        var response = await _service.GetRoles(request, CreateServerCallContext());

        // Assert
        response.Roles.Should().BeEmpty();
    }

    private static ServerCallContext CreateServerCallContext()
    {
        return new TestServerCallContext();
    }

    // Minimal implementation of ServerCallContext for testing
    private class TestServerCallContext : ServerCallContext
    {
        protected override string MethodCore => "TestMethod";
        protected override string HostCore => "localhost";
        protected override string PeerCore => "peer";
        protected override DateTime DeadlineCore => DateTime.MaxValue;
        protected override Metadata RequestHeadersCore => new();
        protected override CancellationToken CancellationTokenCore => CancellationToken.None;
        protected override Metadata ResponseTrailersCore => new();
        protected override Status StatusCore { get; set; }
        protected override WriteOptions? WriteOptionsCore { get; set; }
        protected override AuthContext AuthContextCore => new(null, new Dictionary<string, List<AuthProperty>>());

        protected override ContextPropagationToken CreatePropagationTokenCore(ContextPropagationOptions? options)
            => throw new NotImplementedException();

        protected override Task WriteResponseHeadersAsyncCore(Metadata responseHeaders)
            => Task.CompletedTask;
    }
}
