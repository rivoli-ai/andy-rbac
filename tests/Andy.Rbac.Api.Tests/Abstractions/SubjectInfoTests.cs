using Andy.Rbac.Abstractions;
using FluentAssertions;
using Xunit;

namespace Andy.Rbac.Api.Tests.Abstractions;

public class SubjectInfoTests
{
    [Fact]
    public void SubjectInfo_CanBeCreated_WithAllRequiredProperties()
    {
        // Arrange
        var id = Guid.NewGuid();
        var createdAt = DateTimeOffset.UtcNow;

        // Act
        var subject = new SubjectInfo(
            id,
            "user-123",
            "auth0",
            "user@example.com",
            "John Doe",
            true,
            createdAt,
            null);

        // Assert
        subject.Id.Should().Be(id);
        subject.ExternalId.Should().Be("user-123");
        subject.Provider.Should().Be("auth0");
        subject.Email.Should().Be("user@example.com");
        subject.DisplayName.Should().Be("John Doe");
        subject.IsActive.Should().BeTrue();
        subject.CreatedAt.Should().Be(createdAt);
        subject.LastSeenAt.Should().BeNull();
    }

    [Fact]
    public void SubjectInfo_WithNullableValues_CanBeNull()
    {
        // Arrange & Act
        var subject = new SubjectInfo(
            Guid.NewGuid(),
            "service-account",
            "internal",
            null,  // Email can be null
            null,  // DisplayName can be null
            true,
            DateTimeOffset.UtcNow,
            null);

        // Assert
        subject.Email.Should().BeNull();
        subject.DisplayName.Should().BeNull();
        subject.LastSeenAt.Should().BeNull();
    }

    [Fact]
    public void SubjectInfo_WithLastSeenAt_StoresValue()
    {
        // Arrange
        var lastSeenAt = DateTimeOffset.UtcNow;

        // Act
        var subject = new SubjectInfo(
            Guid.NewGuid(),
            "user-123",
            "auth0",
            "user@example.com",
            "John Doe",
            true,
            DateTimeOffset.UtcNow.AddDays(-1),
            lastSeenAt);

        // Assert
        subject.LastSeenAt.Should().Be(lastSeenAt);
    }

    [Fact]
    public void SubjectInfo_IsInactive_WhenIsActiveFalse()
    {
        // Act
        var subject = new SubjectInfo(
            Guid.NewGuid(),
            "user-123",
            "auth0",
            null,
            null,
            false,
            DateTimeOffset.UtcNow,
            null);

        // Assert
        subject.IsActive.Should().BeFalse();
    }

    [Fact]
    public void SubjectInfo_Equality_BasedOnAllProperties()
    {
        // Arrange
        var id = Guid.NewGuid();
        var createdAt = DateTimeOffset.UtcNow;
        var lastSeenAt = createdAt.AddMinutes(5);

        var subject1 = new SubjectInfo(id, "user-123", "auth0", "user@example.com", "John", true, createdAt, lastSeenAt);
        var subject2 = new SubjectInfo(id, "user-123", "auth0", "user@example.com", "John", true, createdAt, lastSeenAt);

        // Assert
        subject1.Should().Be(subject2);
        (subject1 == subject2).Should().BeTrue();
        subject1.GetHashCode().Should().Be(subject2.GetHashCode());
    }

    [Fact]
    public void SubjectInfo_Inequality_WhenDifferentId()
    {
        // Arrange
        var createdAt = DateTimeOffset.UtcNow;
        var subject1 = new SubjectInfo(Guid.NewGuid(), "user-123", "auth0", null, null, true, createdAt, null);
        var subject2 = new SubjectInfo(Guid.NewGuid(), "user-123", "auth0", null, null, true, createdAt, null);

        // Assert
        subject1.Should().NotBe(subject2);
        (subject1 != subject2).Should().BeTrue();
    }

    [Fact]
    public void SubjectInfo_Inequality_WhenDifferentExternalId()
    {
        // Arrange
        var id = Guid.NewGuid();
        var createdAt = DateTimeOffset.UtcNow;
        var subject1 = new SubjectInfo(id, "user-123", "auth0", null, null, true, createdAt, null);
        var subject2 = new SubjectInfo(id, "user-456", "auth0", null, null, true, createdAt, null);

        // Assert
        subject1.Should().NotBe(subject2);
    }

    [Fact]
    public void SubjectInfo_RecordWith_CanCreateModifiedCopy()
    {
        // Arrange
        var original = new SubjectInfo(
            Guid.NewGuid(),
            "user-123",
            "auth0",
            "old@example.com",
            "Old Name",
            true,
            DateTimeOffset.UtcNow,
            null);

        // Act
        var modified = original with
        {
            Email = "new@example.com",
            DisplayName = "New Name"
        };

        // Assert
        modified.Email.Should().Be("new@example.com");
        modified.DisplayName.Should().Be("New Name");
        modified.ExternalId.Should().Be(original.ExternalId);
        modified.Id.Should().Be(original.Id);
    }

    [Fact]
    public void SubjectInfo_ToString_ContainsExternalId()
    {
        // Arrange
        var subject = new SubjectInfo(
            Guid.NewGuid(),
            "user-123",
            "auth0",
            null,
            null,
            true,
            DateTimeOffset.UtcNow,
            null);

        // Act
        var str = subject.ToString();

        // Assert
        str.Should().Contain("user-123");
        str.Should().Contain("SubjectInfo");
    }

    [Fact]
    public void SubjectInfo_Deconstruction_Works()
    {
        // Arrange
        var id = Guid.NewGuid();
        var createdAt = DateTimeOffset.UtcNow;
        var subject = new SubjectInfo(id, "user-123", "auth0", "user@example.com", "John", true, createdAt, null);

        // Act
        var (subjId, externalId, provider, email, displayName, isActive, created, lastSeen) = subject;

        // Assert
        subjId.Should().Be(id);
        externalId.Should().Be("user-123");
        provider.Should().Be("auth0");
        email.Should().Be("user@example.com");
        displayName.Should().Be("John");
        isActive.Should().BeTrue();
        created.Should().Be(createdAt);
        lastSeen.Should().BeNull();
    }
}
