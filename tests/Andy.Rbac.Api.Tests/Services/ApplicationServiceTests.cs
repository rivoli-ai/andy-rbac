using Andy.Rbac.Api.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Andy.Rbac.Api.Tests.Services;

public class ApplicationServiceTests
{
    private readonly Mock<ILogger<ApplicationService>> _loggerMock = new();

    [Fact]
    public async Task GetAllAsync_ReturnsAllApplications()
    {
        // Arrange
        using var context = await TestDbContextFactory.CreateWithSeedDataAsync();
        var service = new ApplicationService(context, _loggerMock.Object);

        // Act
        var result = await service.GetAllAsync();

        // Assert
        result.Applications.Should().NotBeEmpty();
        result.Applications.Should().Contain(a => a.Code == "test-app");
    }

    [Fact]
    public async Task GetByIdAsync_WithValidId_ReturnsApplication()
    {
        // Arrange
        using var context = await TestDbContextFactory.CreateWithSeedDataAsync();
        var service = new ApplicationService(context, _loggerMock.Object);
        var appId = Guid.Parse("11111111-1111-1111-1111-111111111111");

        // Act
        var result = await service.GetByIdAsync(appId);

        // Assert
        result.Should().NotBeNull();
        result!.Application.Code.Should().Be("test-app");
        result.Application.Name.Should().Be("Test Application");
    }

    [Fact]
    public async Task GetByIdAsync_WithInvalidId_ReturnsNull()
    {
        // Arrange
        using var context = await TestDbContextFactory.CreateWithSeedDataAsync();
        var service = new ApplicationService(context, _loggerMock.Object);

        // Act
        var result = await service.GetByIdAsync(Guid.NewGuid());

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetByCodeAsync_WithValidCode_ReturnsApplication()
    {
        // Arrange
        using var context = await TestDbContextFactory.CreateWithSeedDataAsync();
        var service = new ApplicationService(context, _loggerMock.Object);

        // Act
        var result = await service.GetByCodeAsync("test-app");

        // Assert
        result.Should().NotBeNull();
        result!.Application.Code.Should().Be("test-app");
    }

    [Fact]
    public async Task GetByCodeAsync_WithInvalidCode_ReturnsNull()
    {
        // Arrange
        using var context = await TestDbContextFactory.CreateWithSeedDataAsync();
        var service = new ApplicationService(context, _loggerMock.Object);

        // Act
        var result = await service.GetByCodeAsync("non-existent");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task CreateAsync_WithValidData_CreatesApplication()
    {
        // Arrange
        using var context = TestDbContextFactory.Create();
        var service = new ApplicationService(context, _loggerMock.Object);
        var request = new CreateApplicationRequest("new-app", "New Application", "Description");

        // Act
        var result = await service.CreateAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.Application.Code.Should().Be("new-app");
        result.Application.Name.Should().Be("New Application");

        // Verify it was persisted
        var retrieved = await service.GetByCodeAsync("new-app");
        retrieved.Should().NotBeNull();
    }

    [Fact]
    public async Task CreateAsync_WithDuplicateCode_ThrowsException()
    {
        // Arrange
        using var context = await TestDbContextFactory.CreateWithSeedDataAsync();
        var service = new ApplicationService(context, _loggerMock.Object);
        var request = new CreateApplicationRequest("test-app", "Duplicate", null);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.CreateAsync(request));
    }

    [Fact]
    public async Task UpdateAsync_WithValidData_UpdatesApplication()
    {
        // Arrange
        using var context = await TestDbContextFactory.CreateWithSeedDataAsync();
        var service = new ApplicationService(context, _loggerMock.Object);
        var appId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var request = new UpdateApplicationRequest("Updated Name", "Updated Description");

        // Act
        var result = await service.UpdateAsync(appId, request);

        // Assert
        result.Should().NotBeNull();
        result!.Application.Name.Should().Be("Updated Name");
        result.Application.Description.Should().Be("Updated Description");
    }

    [Fact]
    public async Task UpdateAsync_WithInvalidId_ReturnsNull()
    {
        // Arrange
        using var context = await TestDbContextFactory.CreateWithSeedDataAsync();
        var service = new ApplicationService(context, _loggerMock.Object);
        var request = new UpdateApplicationRequest("Updated Name");

        // Act
        var result = await service.UpdateAsync(Guid.NewGuid(), request);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task DeleteAsync_WithValidId_DeletesApplication()
    {
        // Arrange
        using var context = TestDbContextFactory.Create();
        var service = new ApplicationService(context, _loggerMock.Object);

        // Create an app to delete
        var created = await service.CreateAsync(new CreateApplicationRequest("to-delete", "To Delete"));

        // Act
        var result = await service.DeleteAsync(created.Application.Id);

        // Assert
        result.Should().BeTrue();
        var retrieved = await service.GetByCodeAsync("to-delete");
        retrieved.Should().BeNull();
    }

    [Fact]
    public async Task DeleteAsync_WithInvalidId_ReturnsFalse()
    {
        // Arrange
        using var context = await TestDbContextFactory.CreateWithSeedDataAsync();
        var service = new ApplicationService(context, _loggerMock.Object);

        // Act
        var result = await service.DeleteAsync(Guid.NewGuid());

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task AddResourceTypeAsync_WithValidData_AddsResourceType()
    {
        // Arrange
        using var context = await TestDbContextFactory.CreateWithSeedDataAsync();
        var service = new ApplicationService(context, _loggerMock.Object);
        var appId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var request = new CreateResourceTypeRequest("new-type", "New Type", "Description", true);

        // Act
        var result = await service.AddResourceTypeAsync(appId, request);

        // Assert
        result.Should().NotBeNull();
        result!.ResourceType.Code.Should().Be("new-type");
        result.ResourceType.SupportsInstances.Should().BeTrue();
    }

    [Fact]
    public async Task AddResourceTypeAsync_WithDuplicateCode_ThrowsException()
    {
        // Arrange
        using var context = await TestDbContextFactory.CreateWithSeedDataAsync();
        var service = new ApplicationService(context, _loggerMock.Object);
        var appId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var request = new CreateResourceTypeRequest("document", "Duplicate Document");

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.AddResourceTypeAsync(appId, request));
    }
}
