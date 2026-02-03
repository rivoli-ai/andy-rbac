using Andy.Rbac.Api.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Andy.Rbac.Api.Tests.Services;

public class SubjectServiceTests
{
    private readonly Mock<ILogger<SubjectService>> _loggerMock = new();

    [Fact]
    public async Task SearchAsync_WithNoQuery_ReturnsAllSubjects()
    {
        // Arrange
        using var context = await TestDbContextFactory.CreateWithSeedDataAsync();
        var service = new SubjectService(context, _loggerMock.Object);

        // Act
        var result = await service.SearchAsync();

        // Assert
        result.Subjects.Should().HaveCount(4); // admin, editor, viewer, no-role
    }

    [Fact]
    public async Task SearchAsync_WithQuery_ReturnsMatchingSubjects()
    {
        // Arrange
        using var context = await TestDbContextFactory.CreateWithSeedDataAsync();
        var service = new SubjectService(context, _loggerMock.Object);

        // Act
        var result = await service.SearchAsync("admin");

        // Assert
        result.Subjects.Should().ContainSingle();
        result.Subjects[0].ExternalId.Should().Be("admin-user");
    }

    [Fact]
    public async Task SearchAsync_WithEmailQuery_ReturnsMatchingSubjects()
    {
        // Arrange
        using var context = await TestDbContextFactory.CreateWithSeedDataAsync();
        var service = new SubjectService(context, _loggerMock.Object);

        // Act
        var result = await service.SearchAsync("editor@test.com");

        // Assert
        result.Subjects.Should().ContainSingle();
        result.Subjects[0].Email.Should().Be("editor@test.com");
    }

    [Fact]
    public async Task SearchAsync_WithLimit_RespectsLimit()
    {
        // Arrange
        using var context = await TestDbContextFactory.CreateWithSeedDataAsync();
        var service = new SubjectService(context, _loggerMock.Object);

        // Act
        var result = await service.SearchAsync(limit: 2);

        // Assert
        result.Subjects.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetByIdAsync_WithValidId_ReturnsSubjectWithRoles()
    {
        // Arrange
        using var context = await TestDbContextFactory.CreateWithSeedDataAsync();
        var service = new SubjectService(context, _loggerMock.Object);
        var adminId = Guid.Parse("66666666-6666-6666-6666-666666666666");

        // Act
        var result = await service.GetByIdAsync(adminId);

        // Assert
        result.Should().NotBeNull();
        result!.Subject.ExternalId.Should().Be("admin-user");
        result.Subject.Roles.Should().ContainSingle();
        result.Subject.Roles[0].RoleCode.Should().Be("admin");
    }

    [Fact]
    public async Task GetByIdAsync_WithInvalidId_ReturnsNull()
    {
        // Arrange
        using var context = await TestDbContextFactory.CreateWithSeedDataAsync();
        var service = new SubjectService(context, _loggerMock.Object);

        // Act
        var result = await service.GetByIdAsync(Guid.NewGuid());

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetByExternalIdAsync_WithValidId_ReturnsSubject()
    {
        // Arrange
        using var context = await TestDbContextFactory.CreateWithSeedDataAsync();
        var service = new SubjectService(context, _loggerMock.Object);

        // Act
        var result = await service.GetByExternalIdAsync("admin-user");

        // Assert
        result.Should().NotBeNull();
        result!.Subject.Email.Should().Be("admin@test.com");
    }

    [Fact]
    public async Task GetByExternalIdAsync_WithInvalidId_ReturnsNull()
    {
        // Arrange
        using var context = await TestDbContextFactory.CreateWithSeedDataAsync();
        var service = new SubjectService(context, _loggerMock.Object);

        // Act
        var result = await service.GetByExternalIdAsync("non-existent");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task CreateAsync_WithValidData_CreatesSubject()
    {
        // Arrange
        using var context = TestDbContextFactory.Create();
        var service = new SubjectService(context, _loggerMock.Object);
        var request = new CreateSubjectRequest("new-user", "test-provider", "new@test.com", "New User");

        // Act
        var result = await service.CreateAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.Subject.ExternalId.Should().Be("new-user");
        result.Subject.Provider.Should().Be("test-provider");
        result.Subject.Email.Should().Be("new@test.com");
        result.Subject.DisplayName.Should().Be("New User");
        result.Subject.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task CreateAsync_WithDuplicateExternalId_ThrowsException()
    {
        // Arrange
        using var context = await TestDbContextFactory.CreateWithSeedDataAsync();
        var service = new SubjectService(context, _loggerMock.Object);
        var request = new CreateSubjectRequest("admin-user", "test-provider");

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.CreateAsync(request));
    }

    [Fact]
    public async Task UpdateAsync_WithValidData_UpdatesSubject()
    {
        // Arrange
        using var context = await TestDbContextFactory.CreateWithSeedDataAsync();
        var service = new SubjectService(context, _loggerMock.Object);
        var adminId = Guid.Parse("66666666-6666-6666-6666-666666666666");
        var request = new UpdateSubjectRequest("updated@test.com", "Updated Name", false);

        // Act
        var result = await service.UpdateAsync(adminId, request);

        // Assert
        result.Should().NotBeNull();
        result!.Subject.Email.Should().Be("updated@test.com");
        result.Subject.DisplayName.Should().Be("Updated Name");
        result.Subject.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task UpdateAsync_WithInvalidId_ReturnsNull()
    {
        // Arrange
        using var context = await TestDbContextFactory.CreateWithSeedDataAsync();
        var service = new SubjectService(context, _loggerMock.Object);
        var request = new UpdateSubjectRequest("updated@test.com");

        // Act
        var result = await service.UpdateAsync(Guid.NewGuid(), request);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task DeleteAsync_WithValidId_DeletesSubject()
    {
        // Arrange
        using var context = TestDbContextFactory.Create();
        var service = new SubjectService(context, _loggerMock.Object);

        // Create a subject to delete
        var created = await service.CreateAsync(new CreateSubjectRequest("to-delete", "test-provider"));

        // Act
        var result = await service.DeleteAsync(created.Subject.Id);

        // Assert
        result.Should().BeTrue();
        var retrieved = await service.GetByExternalIdAsync("to-delete");
        retrieved.Should().BeNull();
    }

    [Fact]
    public async Task DeleteAsync_WithInvalidId_ReturnsFalse()
    {
        // Arrange
        using var context = await TestDbContextFactory.CreateWithSeedDataAsync();
        var service = new SubjectService(context, _loggerMock.Object);

        // Act
        var result = await service.DeleteAsync(Guid.NewGuid());

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task GetOrCreateAsync_WithExistingSubject_ReturnsExisting()
    {
        // Arrange
        using var context = await TestDbContextFactory.CreateWithSeedDataAsync();
        var service = new SubjectService(context, _loggerMock.Object);

        // Act
        var result = await service.GetOrCreateAsync("admin-user", "test-provider", "different@test.com", "Different Name");

        // Assert
        result.Should().NotBeNull();
        result.Subject.Email.Should().Be("admin@test.com"); // Original email, not the new one
    }

    [Fact]
    public async Task GetOrCreateAsync_WithNewSubject_CreatesNew()
    {
        // Arrange
        using var context = TestDbContextFactory.Create();
        var service = new SubjectService(context, _loggerMock.Object);

        // Act
        var result = await service.GetOrCreateAsync("new-user", "new-provider", "new@test.com", "New User");

        // Assert
        result.Should().NotBeNull();
        result.Subject.ExternalId.Should().Be("new-user");
        result.Subject.Email.Should().Be("new@test.com");
    }

    [Fact]
    public async Task GetByIdAsync_IncludesTeamMemberships()
    {
        // Arrange
        using var context = await TestDbContextFactory.CreateWithSeedDataAsync();
        var service = new SubjectService(context, _loggerMock.Object);
        var editorId = Guid.Parse("66666666-6666-6666-6666-666666666667");

        // Act
        var result = await service.GetByIdAsync(editorId);

        // Assert
        result.Should().NotBeNull();
        result!.Subject.Teams.Should().ContainSingle();
        result.Subject.Teams[0].TeamCode.Should().Be("test-team");
    }
}
