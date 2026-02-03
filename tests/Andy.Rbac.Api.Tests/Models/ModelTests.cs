using Andy.Rbac.Models;
using FluentAssertions;
using Xunit;

namespace Andy.Rbac.Api.Tests.Models;

public class ApplicationTests
{
    [Fact]
    public void Application_DefaultValues_AreSetCorrectly()
    {
        // Act
        var app = new Application
        {
            Code = "test",
            Name = "Test"
        };

        // Assert - EF Core generates IDs, so they start as Guid.Empty
        app.Id.Should().Be(Guid.Empty);
        app.CreatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(1));
        app.ResourceTypes.Should().NotBeNull();
        app.Roles.Should().NotBeNull();
    }
}

public class SubjectTests
{
    [Fact]
    public void Subject_DefaultValues_AreSetCorrectly()
    {
        // Act
        var subject = new Subject
        {
            ExternalId = "user-123",
            Provider = "test-provider"
        };

        // Assert - EF Core generates IDs, so they start as Guid.Empty
        subject.Id.Should().Be(Guid.Empty);
        subject.IsActive.Should().BeTrue();
        subject.CreatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(1));
        subject.LastSeenAt.Should().BeNull();
        subject.Type.Should().Be(SubjectType.User);
    }

    [Fact]
    public void Subject_AllProperties_CanBeSet()
    {
        // Arrange
        var id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        // Act
        var subject = new Subject
        {
            Id = id,
            ExternalId = "user-123",
            Provider = "auth0",
            Email = "user@example.com",
            DisplayName = "John Doe",
            IsActive = false,
            CreatedAt = now,
            LastSeenAt = now,
            Type = SubjectType.Service,
            Metadata = new Dictionary<string, object> { { "key", "value" } }
        };

        // Assert
        subject.Id.Should().Be(id);
        subject.ExternalId.Should().Be("user-123");
        subject.Provider.Should().Be("auth0");
        subject.Email.Should().Be("user@example.com");
        subject.DisplayName.Should().Be("John Doe");
        subject.IsActive.Should().BeFalse();
        subject.Type.Should().Be(SubjectType.Service);
        subject.Metadata.Should().ContainKey("key");
    }
}

public class RoleTests
{
    [Fact]
    public void Role_DefaultValues_AreSetCorrectly()
    {
        // Act
        var role = new Role
        {
            Code = "admin",
            Name = "Administrator"
        };

        // Assert - EF Core generates IDs, so they start as Guid.Empty
        role.Id.Should().Be(Guid.Empty);
        role.IsSystem.Should().BeFalse();
        role.ApplicationId.Should().BeNull();
        role.ParentRoleId.Should().BeNull();
    }

    [Fact]
    public void Role_CanSetApplication()
    {
        // Arrange
        var appId = Guid.NewGuid();

        // Act
        var role = new Role
        {
            Code = "editor",
            Name = "Editor",
            ApplicationId = appId,
            IsSystem = true,
            Description = "Can edit content"
        };

        // Assert
        role.ApplicationId.Should().Be(appId);
        role.IsSystem.Should().BeTrue();
        role.Description.Should().Be("Can edit content");
    }

    [Fact]
    public void Role_CanSetParentRole()
    {
        // Arrange
        var parentId = Guid.NewGuid();

        // Act
        var role = new Role
        {
            Code = "senior-editor",
            Name = "Senior Editor",
            ParentRoleId = parentId
        };

        // Assert
        role.ParentRoleId.Should().Be(parentId);
    }
}

public class TeamTests
{
    [Fact]
    public void Team_DefaultValues_AreSetCorrectly()
    {
        // Act
        var team = new Team
        {
            Code = "engineering",
            Name = "Engineering Team"
        };

        // Assert - EF Core generates IDs, so they start as Guid.Empty
        team.Id.Should().Be(Guid.Empty);
        team.IsActive.Should().BeTrue();
        team.CreatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(1));
        team.Members.Should().NotBeNull();
        team.TeamRoles.Should().NotBeNull();
    }

    [Fact]
    public void Team_CanSetHierarchy()
    {
        // Arrange
        var parentId = Guid.NewGuid();
        var appId = Guid.NewGuid();

        // Act
        var team = new Team
        {
            Code = "frontend",
            Name = "Frontend Team",
            ParentTeamId = parentId,
            ApplicationId = appId,
            Description = "Frontend developers"
        };

        // Assert
        team.ParentTeamId.Should().Be(parentId);
        team.ApplicationId.Should().Be(appId);
        team.Description.Should().Be("Frontend developers");
    }
}

public class TeamMemberTests
{
    [Fact]
    public void TeamMember_DefaultRole_IsMember()
    {
        // Act
        var member = new TeamMember
        {
            TeamId = Guid.NewGuid(),
            SubjectId = Guid.NewGuid()
        };

        // Assert
        member.MembershipRole.Should().Be(TeamMembershipRole.Member);
        member.AddedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void TeamMember_CanSetAdminRole()
    {
        // Act
        var member = new TeamMember
        {
            TeamId = Guid.NewGuid(),
            SubjectId = Guid.NewGuid(),
            MembershipRole = TeamMembershipRole.Admin
        };

        // Assert
        member.MembershipRole.Should().Be(TeamMembershipRole.Admin);
    }
}

public class ResourceTypeTests
{
    [Fact]
    public void ResourceType_DefaultValues_AreSetCorrectly()
    {
        // Act
        var resourceType = new ResourceType
        {
            ApplicationId = Guid.NewGuid(),
            Code = "document",
            Name = "Document"
        };

        // Assert - EF Core generates IDs, so they start as Guid.Empty
        resourceType.Id.Should().Be(Guid.Empty);
        resourceType.SupportsInstances.Should().BeTrue();
    }
}

public class ResourceInstanceTests
{
    [Fact]
    public void ResourceInstance_DefaultValues_AreSetCorrectly()
    {
        // Act
        var instance = new ResourceInstance
        {
            ResourceTypeId = Guid.NewGuid(),
            ExternalId = "doc-123"
        };

        // Assert - EF Core generates IDs, so they start as Guid.Empty
        instance.Id.Should().Be(Guid.Empty);
        instance.CreatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(1));
        instance.OwnerSubjectId.Should().BeNull();
    }

    [Fact]
    public void ResourceInstance_CanSetOwner()
    {
        // Arrange
        var ownerId = Guid.NewGuid();

        // Act
        var instance = new ResourceInstance
        {
            ResourceTypeId = Guid.NewGuid(),
            ExternalId = "doc-123",
            DisplayName = "My Document",
            OwnerSubjectId = ownerId,
            Metadata = new Dictionary<string, object> { { "type", "markdown" } }
        };

        // Assert
        instance.OwnerSubjectId.Should().Be(ownerId);
        instance.DisplayName.Should().Be("My Document");
        instance.Metadata.Should().ContainKey("type");
    }
}

public class SubjectRoleTests
{
    [Fact]
    public void SubjectRole_DefaultValues_AreSetCorrectly()
    {
        // Act
        var assignment = new SubjectRole
        {
            SubjectId = Guid.NewGuid(),
            RoleId = Guid.NewGuid()
        };

        // Assert
        assignment.GrantedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(1));
        assignment.ExpiresAt.Should().BeNull();
        assignment.ResourceInstanceId.Should().BeNull();
    }

    [Fact]
    public void SubjectRole_CanSetExpiry()
    {
        // Arrange
        var expiresAt = DateTimeOffset.UtcNow.AddDays(30);

        // Act
        var assignment = new SubjectRole
        {
            SubjectId = Guid.NewGuid(),
            RoleId = Guid.NewGuid(),
            ExpiresAt = expiresAt
        };

        // Assert
        assignment.ExpiresAt.Should().Be(expiresAt);
    }

    [Fact]
    public void SubjectRole_CanSetResourceScope()
    {
        // Act
        var assignment = new SubjectRole
        {
            SubjectId = Guid.NewGuid(),
            RoleId = Guid.NewGuid(),
            ResourceInstanceId = "doc-123"
        };

        // Assert
        assignment.ResourceInstanceId.Should().Be("doc-123");
    }
}

public class InstancePermissionTests
{
    [Fact]
    public void InstancePermission_DefaultValues_AreSetCorrectly()
    {
        // Act
        var permission = new InstancePermission
        {
            SubjectId = Guid.NewGuid(),
            ResourceInstanceId = Guid.NewGuid(),
            PermissionId = Guid.NewGuid()
        };

        // Assert - EF Core generates IDs, so they start as Guid.Empty
        permission.Id.Should().Be(Guid.Empty);
        permission.GrantedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(1));
        permission.ExpiresAt.Should().BeNull();
        permission.GrantedById.Should().BeNull();
    }

    [Fact]
    public void InstancePermission_CanSetExpiry()
    {
        // Arrange
        var expiresAt = DateTimeOffset.UtcNow.AddDays(7);
        var grantedBy = Guid.NewGuid();

        // Act
        var permission = new InstancePermission
        {
            SubjectId = Guid.NewGuid(),
            ResourceInstanceId = Guid.NewGuid(),
            PermissionId = Guid.NewGuid(),
            ExpiresAt = expiresAt,
            GrantedById = grantedBy
        };

        // Assert
        permission.ExpiresAt.Should().Be(expiresAt);
        permission.GrantedById.Should().Be(grantedBy);
    }
}

public class PermissionTests
{
    [Fact]
    public void Permission_Code_CombinesComponents()
    {
        // Arrange
        var app = new Application { Code = "test-app", Name = "Test App" };
        var resourceType = new ResourceType
        {
            ApplicationId = Guid.NewGuid(),
            Code = "document",
            Name = "Document",
            Application = app
        };
        var action = new Andy.Rbac.Models.Action { Code = "read", Name = "Read" };
        var permission = new Permission
        {
            ResourceType = resourceType,
            Action = action
        };

        // Act
        var code = permission.Code;

        // Assert
        code.Should().Be("test-app:document:read");
    }

    [Fact]
    public void Permission_Code_WithNullResourceType_HandlesGracefully()
    {
        // Arrange
        var action = new Andy.Rbac.Models.Action { Code = "read", Name = "Read" };
        var permission = new Permission
        {
            ResourceType = null!,
            Action = action
        };

        // Act
        var code = permission.Code;

        // Assert
        code.Should().Contain("read");
    }

    [Fact]
    public void Permission_DefaultValues_AreSetCorrectly()
    {
        // Act
        var permission = new Permission
        {
            ResourceType = new ResourceType { ApplicationId = Guid.NewGuid(), Code = "doc", Name = "Doc" },
            Action = new Andy.Rbac.Models.Action { Code = "read", Name = "Read" }
        };

        // Assert
        permission.Id.Should().Be(Guid.Empty);
        permission.Description.Should().BeNull();
        permission.RolePermissions.Should().NotBeNull();
        permission.InstancePermissions.Should().NotBeNull();
    }

    [Fact]
    public void Permission_CanSetDescription()
    {
        // Act
        var permission = new Permission
        {
            ResourceType = new ResourceType { ApplicationId = Guid.NewGuid(), Code = "doc", Name = "Doc" },
            Action = new Andy.Rbac.Models.Action { Code = "read", Name = "Read" },
            Description = "Allows reading documents"
        };

        // Assert
        permission.Description.Should().Be("Allows reading documents");
    }
}

public class ActionTests
{
    [Fact]
    public void Action_DefaultValues_AreSetCorrectly()
    {
        // Act
        var action = new Andy.Rbac.Models.Action
        {
            Code = "read",
            Name = "Read"
        };

        // Assert - EF Core generates IDs, so they start as Guid.Empty
        action.Id.Should().Be(Guid.Empty);
        action.SortOrder.Should().Be(0);
    }

    [Fact]
    public void Action_CanSetAllProperties()
    {
        // Act
        var action = new Andy.Rbac.Models.Action
        {
            Code = "admin",
            Name = "Administer",
            Description = "Full administrative access",
            SortOrder = 100
        };

        // Assert
        action.Code.Should().Be("admin");
        action.Name.Should().Be("Administer");
        action.Description.Should().Be("Full administrative access");
        action.SortOrder.Should().Be(100);
    }
}

public class RbacAuditLogTests
{
    [Fact]
    public void RbacAuditLog_DefaultValues_AreSetCorrectly()
    {
        // Act
        var log = new RbacAuditLog
        {
            EventType = AuditEventTypes.PermissionCheck
        };

        // Assert - EF Core generates IDs, so they start as Guid.Empty
        log.Id.Should().Be(Guid.Empty);
        log.Timestamp.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void RbacAuditLog_CanSetAllProperties()
    {
        // Act
        var log = new RbacAuditLog
        {
            EventType = AuditEventTypes.RoleAssigned,
            SubjectId = Guid.NewGuid(),
            ResourceType = "document",
            ResourceInstanceId = "doc-123",
            PermissionCode = "test-app:document:read",
            Result = "allowed",
            IpAddress = "192.168.1.1",
            UserAgent = "Mozilla/5.0",
            Context = new Dictionary<string, object> { { "reason", "promotion" } }
        };

        // Assert
        log.EventType.Should().Be(AuditEventTypes.RoleAssigned);
        log.Result.Should().Be("allowed");
        log.IpAddress.Should().Be("192.168.1.1");
        log.UserAgent.Should().Be("Mozilla/5.0");
        log.Context.Should().ContainKey("reason");
    }
}

public class ApiKeyTests
{
    [Fact]
    public void ApiKey_DefaultValues_AreSetCorrectly()
    {
        // Act
        var apiKey = new ApiKey
        {
            Name = "Test Key",
            KeyHash = "hash123",
            KeyPrefix = "rbac_test_",
            SubjectId = Guid.NewGuid()
        };

        // Assert - EF Core generates IDs, so they start as Guid.Empty
        apiKey.Id.Should().Be(Guid.Empty);
        apiKey.IsActive.Should().BeTrue();
        apiKey.CreatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(1));
        apiKey.ExpiresAt.Should().BeNull();
        apiKey.LastUsedAt.Should().BeNull();
    }

    [Fact]
    public void ApiKey_CanSetScopes()
    {
        // Act
        var apiKey = new ApiKey
        {
            Name = "Limited Key",
            KeyHash = "hash456",
            KeyPrefix = "rbac_test_",
            SubjectId = Guid.NewGuid(),
            Scopes = new List<string> { "read", "write" },
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(30)
        };

        // Assert
        apiKey.Scopes.Should().HaveCount(2);
        apiKey.Scopes.Should().Contain("read");
        apiKey.ExpiresAt.Should().NotBeNull();
    }
}

public class ExternalGroupMappingTests
{
    [Fact]
    public void ExternalGroupMapping_CanSetAllProperties()
    {
        // Act
        var mapping = new ExternalGroupMapping
        {
            Provider = "azure-ad",
            ExternalGroupId = "group-123",
            ExternalGroupName = "Engineering",
            RoleId = Guid.NewGuid()
        };

        // Assert - EF Core generates IDs, so they start as Guid.Empty
        mapping.Id.Should().Be(Guid.Empty);
        mapping.Provider.Should().Be("azure-ad");
        mapping.ExternalGroupId.Should().Be("group-123");
        mapping.ExternalGroupName.Should().Be("Engineering");
    }
}

public class TeamMembershipRoleTests
{
    [Fact]
    public void TeamMembershipRole_HasExpectedValues()
    {
        // Assert
        Enum.GetValues<TeamMembershipRole>().Should().HaveCount(3);
        TeamMembershipRole.Member.Should().Be(0);
        TeamMembershipRole.Admin.Should().Be((TeamMembershipRole)1);
        TeamMembershipRole.Owner.Should().Be((TeamMembershipRole)2);
    }
}

public class SubjectTypeTests
{
    [Fact]
    public void SubjectType_HasExpectedValues()
    {
        // Assert
        Enum.GetValues<SubjectType>().Should().HaveCount(3);
        SubjectType.User.Should().Be(0);
        SubjectType.Service.Should().Be((SubjectType)1);
        SubjectType.Group.Should().Be((SubjectType)2);
    }
}
