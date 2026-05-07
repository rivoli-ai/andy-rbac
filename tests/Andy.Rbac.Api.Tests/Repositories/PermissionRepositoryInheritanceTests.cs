using Andy.Rbac.Infrastructure.Data;
using Andy.Rbac.Infrastructure.Repositories;
using Andy.Rbac.Models;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Andy.Rbac.Api.Tests.Repositories;

/// <summary>
/// Issue #46 — verifies role-permission inheritance walks the parent chain in
/// the correct direction (child inherits from parent) and is cycle-safe.
/// </summary>
public class PermissionRepositoryInheritanceTests
{
    private static (RbacDbContext db, Subject subject, Role parent, Role child, string permissionCode) BuildHierarchy()
    {
        var db = TestDbContextFactory.Create();

        var app = new Application { Id = Guid.NewGuid(), Code = "app", Name = "App" };
        db.Applications.Add(app);

        var resourceType = new ResourceType
        {
            Id = Guid.NewGuid(),
            ApplicationId = app.Id,
            Code = "thing",
            Name = "Thing",
        };
        db.ResourceTypes.Add(resourceType);

        var action = new Andy.Rbac.Models.Action { Id = Guid.NewGuid(), Code = "read", Name = "Read" };
        db.Actions.Add(action);

        var permission = new Permission
        {
            Id = Guid.NewGuid(),
            ResourceTypeId = resourceType.Id,
            ActionId = action.Id,
        };
        db.Permissions.Add(permission);

        var parent = new Role
        {
            Id = Guid.NewGuid(),
            ApplicationId = app.Id,
            Code = "parent",
            Name = "Parent",
        };
        var child = new Role
        {
            Id = Guid.NewGuid(),
            ApplicationId = app.Id,
            Code = "child",
            Name = "Child",
            ParentRoleId = parent.Id,
        };
        db.Roles.AddRange(parent, child);

        // Parent role holds the permission. Child should inherit it.
        db.RolePermissions.Add(new RolePermission { RoleId = parent.Id, PermissionId = permission.Id });

        var subject = new Subject
        {
            Id = Guid.NewGuid(),
            ExternalId = "test-sub",
            Provider = "andy-auth",
            Email = "test@example.com",
        };
        db.Subjects.Add(subject);

        db.SaveChanges();

        var permissionCode = $"{app.Code}:{resourceType.Code}:{action.Code}";
        return (db, subject, parent, child, permissionCode);
    }

    [Fact]
    public async Task SubjectWithChildRole_InheritsParentPermissions()
    {
        // The whole point of role inheritance: assigning the child role to a
        // subject should grant them the parent's permissions.
        var (db, subject, _, child, permissionCode) = BuildHierarchy();
        db.SubjectRoles.Add(new SubjectRole { SubjectId = subject.Id, RoleId = child.Id });
        await db.SaveChangesAsync();

        var repo = new PermissionRepository(db);
        var has = await repo.HasPermissionAsync(subject.Id, permissionCode);
        var perms = await repo.GetPermissionsForSubjectAsync(subject.Id);

        has.Should().BeTrue("child role inherits permissions of its parent");
        perms.Should().Contain(permissionCode);
    }

    [Fact]
    public async Task SubjectWithParentRole_DoesNotGetChildOnlyPermissions()
    {
        // Inversion guard: a subject who only holds the parent role must NOT
        // gain a permission granted to a child of that parent. The previous
        // implementation walked the relationship the wrong way and exposed
        // child permissions to parent-role holders.
        var (db, subject, parent, child, _) = BuildHierarchy();

        // Move the permission from the parent to the child role only.
        // RolePermission's primary key includes RoleId, so we delete-and-add
        // rather than mutating the existing row.
        var oldRp = db.RolePermissions.Single();
        var permissionId = oldRp.PermissionId;
        db.RolePermissions.Remove(oldRp);
        await db.SaveChangesAsync();
        db.RolePermissions.Add(new RolePermission { RoleId = child.Id, PermissionId = permissionId });
        await db.SaveChangesAsync();

        // Subject holds the parent, not the child.
        db.SubjectRoles.Add(new SubjectRole { SubjectId = subject.Id, RoleId = parent.Id });
        await db.SaveChangesAsync();

        var repo = new PermissionRepository(db);
        var perms = await repo.GetPermissionsForSubjectAsync(subject.Id);

        perms.Should().BeEmpty("parent role must not inherit permissions granted to child roles");
    }

    [Fact]
    public async Task GrandchildRole_InheritsGrandparentPermissions()
    {
        // Multi-level chain: grandparent → parent → child. Permission on the
        // grandparent must reach the grandchild. The prior implementation
        // capped at two levels via `ParentRole.ParentRoleId`; this test would
        // catch a regression to that behavior.
        var (db, subject, _, child, permissionCode) = BuildHierarchy();

        var grandchild = new Role
        {
            Id = Guid.NewGuid(),
            ApplicationId = child.ApplicationId,
            Code = "grandchild",
            Name = "Grandchild",
            ParentRoleId = child.Id,
        };
        db.Roles.Add(grandchild);
        db.SubjectRoles.Add(new SubjectRole { SubjectId = subject.Id, RoleId = grandchild.Id });
        await db.SaveChangesAsync();

        var repo = new PermissionRepository(db);
        var has = await repo.HasPermissionAsync(subject.Id, permissionCode);

        has.Should().BeTrue("multi-level chains should resolve permissions all the way to the root");
    }

    [Fact]
    public async Task CyclicParentChain_DoesNotInfinitelyLoop()
    {
        // Defense against a corrupt graph: if A→B→A is somehow persisted
        // (direct DB edit, future buggy update endpoint), permission lookups
        // must terminate. They needn't return correct results — just not hang.
        var (db, subject, parent, child, permissionCode) = BuildHierarchy();

        parent.ParentRoleId = child.Id; // close the cycle
        await db.SaveChangesAsync();

        db.SubjectRoles.Add(new SubjectRole { SubjectId = subject.Id, RoleId = child.Id });
        await db.SaveChangesAsync();

        var repo = new PermissionRepository(db);

        // Run with an explicit cancel-after timeout so the test fails fast
        // rather than hanging if the loop ever regresses.
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var has = await repo.HasPermissionAsync(subject.Id, permissionCode, ct: cts.Token);

        has.Should().BeTrue("the permission is reachable via the (cyclic) parent chain");
    }
}
