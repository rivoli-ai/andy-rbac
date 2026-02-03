using Andy.Rbac.Infrastructure.Data;
using Andy.Rbac.Models;
using Microsoft.EntityFrameworkCore;

namespace Andy.Rbac.Api.Tests;

/// <summary>
/// Factory for creating in-memory database contexts for testing.
/// </summary>
public static class TestDbContextFactory
{
    public static RbacDbContext Create(string? dbName = null)
    {
        var options = new DbContextOptionsBuilder<RbacDbContext>()
            .UseInMemoryDatabase(dbName ?? Guid.NewGuid().ToString())
            .Options;

        var context = new RbacDbContext(options);
        context.Database.EnsureCreated();
        return context;
    }

    public static async Task<RbacDbContext> CreateWithSeedDataAsync(string? dbName = null)
    {
        var context = Create(dbName);
        await SeedTestDataAsync(context);
        return context;
    }

    public static async Task SeedTestDataAsync(RbacDbContext context)
    {
        // Create test application
        var app = new Application
        {
            Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Code = "test-app",
            Name = "Test Application",
            Description = "Application for testing"
        };
        context.Applications.Add(app);

        // Create resource types
        var documentType = new ResourceType
        {
            Id = Guid.Parse("22222222-2222-2222-2222-222222222222"),
            ApplicationId = app.Id,
            Code = "document",
            Name = "Document",
            SupportsInstances = true
        };
        context.ResourceTypes.Add(documentType);

        // Create actions
        var readAction = new Andy.Rbac.Models.Action
        {
            Id = Guid.Parse("33333333-3333-3333-3333-333333333333"),
            Code = "read",
            Name = "Read"
        };
        var writeAction = new Andy.Rbac.Models.Action
        {
            Id = Guid.Parse("33333333-3333-3333-3333-333333333334"),
            Code = "write",
            Name = "Write"
        };
        var deleteAction = new Andy.Rbac.Models.Action
        {
            Id = Guid.Parse("33333333-3333-3333-3333-333333333335"),
            Code = "delete",
            Name = "Delete"
        };
        context.Actions.AddRange(readAction, writeAction, deleteAction);

        // Create permissions
        var readPermission = new Permission
        {
            Id = Guid.Parse("44444444-4444-4444-4444-444444444444"),
            ResourceTypeId = documentType.Id,
            ActionId = readAction.Id
        };
        var writePermission = new Permission
        {
            Id = Guid.Parse("44444444-4444-4444-4444-444444444445"),
            ResourceTypeId = documentType.Id,
            ActionId = writeAction.Id
        };
        var deletePermission = new Permission
        {
            Id = Guid.Parse("44444444-4444-4444-4444-444444444446"),
            ResourceTypeId = documentType.Id,
            ActionId = deleteAction.Id
        };
        context.Permissions.AddRange(readPermission, writePermission, deletePermission);

        // Create roles
        var adminRole = new Role
        {
            Id = Guid.Parse("55555555-5555-5555-5555-555555555555"),
            ApplicationId = app.Id,
            Code = "admin",
            Name = "Administrator",
            IsSystem = true
        };
        var editorRole = new Role
        {
            Id = Guid.Parse("55555555-5555-5555-5555-555555555556"),
            ApplicationId = app.Id,
            Code = "editor",
            Name = "Editor",
            IsSystem = false
        };
        var viewerRole = new Role
        {
            Id = Guid.Parse("55555555-5555-5555-5555-555555555557"),
            ApplicationId = app.Id,
            Code = "viewer",
            Name = "Viewer",
            IsSystem = false
        };
        context.Roles.AddRange(adminRole, editorRole, viewerRole);

        // Create role permissions
        context.RolePermissions.AddRange(
            // Admin has all permissions
            new RolePermission { RoleId = adminRole.Id, PermissionId = readPermission.Id },
            new RolePermission { RoleId = adminRole.Id, PermissionId = writePermission.Id },
            new RolePermission { RoleId = adminRole.Id, PermissionId = deletePermission.Id },
            // Editor has read and write
            new RolePermission { RoleId = editorRole.Id, PermissionId = readPermission.Id },
            new RolePermission { RoleId = editorRole.Id, PermissionId = writePermission.Id },
            // Viewer has only read
            new RolePermission { RoleId = viewerRole.Id, PermissionId = readPermission.Id }
        );

        // Create test subjects
        var adminUser = new Subject
        {
            Id = Guid.Parse("66666666-6666-6666-6666-666666666666"),
            ExternalId = "admin-user",
            Provider = "test-provider",
            Email = "admin@test.com",
            DisplayName = "Admin User"
        };
        var editorUser = new Subject
        {
            Id = Guid.Parse("66666666-6666-6666-6666-666666666667"),
            ExternalId = "editor-user",
            Provider = "test-provider",
            Email = "editor@test.com",
            DisplayName = "Editor User"
        };
        var viewerUser = new Subject
        {
            Id = Guid.Parse("66666666-6666-6666-6666-666666666668"),
            ExternalId = "viewer-user",
            Provider = "test-provider",
            Email = "viewer@test.com",
            DisplayName = "Viewer User"
        };
        var noRoleUser = new Subject
        {
            Id = Guid.Parse("66666666-6666-6666-6666-666666666669"),
            ExternalId = "no-role-user",
            Provider = "test-provider",
            Email = "norole@test.com",
            DisplayName = "No Role User"
        };
        context.Subjects.AddRange(adminUser, editorUser, viewerUser, noRoleUser);

        // Assign roles to subjects
        context.SubjectRoles.AddRange(
            new SubjectRole { SubjectId = adminUser.Id, RoleId = adminRole.Id },
            new SubjectRole { SubjectId = editorUser.Id, RoleId = editorRole.Id },
            new SubjectRole { SubjectId = viewerUser.Id, RoleId = viewerRole.Id }
        );

        // Create a test team
        var team = new Team
        {
            Id = Guid.Parse("77777777-7777-7777-7777-777777777777"),
            Code = "test-team",
            Name = "Test Team"
        };
        context.Teams.Add(team);

        // Add editor to team
        context.TeamMembers.Add(new TeamMember
        {
            TeamId = team.Id,
            SubjectId = editorUser.Id,
            MembershipRole = TeamMembershipRole.Member
        });

        await context.SaveChangesAsync();
    }
}
