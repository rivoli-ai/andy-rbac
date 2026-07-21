using Andy.Rbac.Infrastructure.Repositories;
using Andy.Rbac.Models;
using FluentAssertions;
using Xunit;

namespace Andy.Rbac.Api.Tests.Repositories;

public sealed class PermissionRepositoryTeamRoleTests
{
    private static readonly Guid SubjectId = Guid.Parse("66666666-6666-6666-6666-666666666669");
    private static readonly Guid ViewerRoleId = Guid.Parse("55555555-5555-5555-5555-555555555557");

    [Fact]
    public async Task ActiveTeamRole_ContributesToEffectivePermissionsAndRoles()
    {
        using var db = await TestDbContextFactory.CreateWithSeedDataAsync();
        var team = db.Teams.Single();
        db.TeamMembers.Add(new TeamMember { TeamId = team.Id, SubjectId = SubjectId });
        db.TeamRoles.Add(new TeamRole { TeamId = team.Id, RoleId = ViewerRoleId });
        await db.SaveChangesAsync();

        var repository = new PermissionRepository(db);

        (await repository.HasPermissionAsync(SubjectId, "test-app:document:read")).Should().BeTrue();
        (await repository.GetPermissionsForSubjectAsync(SubjectId)).Should().Contain("test-app:document:read");
        (await repository.GetRolesForSubjectAsync(SubjectId)).Should().Contain("viewer");
    }

    [Fact]
    public async Task ChildMembership_InheritsActiveParentTeamRole_ButNotThroughInactiveChild()
    {
        using var db = await TestDbContextFactory.CreateWithSeedDataAsync();
        var parent = db.Teams.Single();
        var child = new Team { Id = Guid.NewGuid(), Code = "child", Name = "Child", ParentTeamId = parent.Id };
        db.Teams.Add(child);
        db.TeamMembers.Add(new TeamMember { TeamId = child.Id, SubjectId = SubjectId });
        db.TeamRoles.Add(new TeamRole { TeamId = parent.Id, RoleId = ViewerRoleId });
        await db.SaveChangesAsync();

        var repository = new PermissionRepository(db);
        (await repository.HasPermissionAsync(SubjectId, "test-app:document:read")).Should().BeTrue();

        child.IsActive = false;
        await db.SaveChangesAsync();
        (await repository.HasPermissionAsync(SubjectId, "test-app:document:read")).Should().BeFalse();
    }

    [Fact]
    public async Task ExpiredOrWrongInstanceTeamRole_DoesNotGrantPermission()
    {
        using var db = await TestDbContextFactory.CreateWithSeedDataAsync();
        var team = db.Teams.Single();
        db.TeamMembers.Add(new TeamMember { TeamId = team.Id, SubjectId = SubjectId });
        db.TeamRoles.Add(new TeamRole
        {
            TeamId = team.Id,
            RoleId = ViewerRoleId,
            ResourceInstanceId = "doc-1",
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1)
        });
        await db.SaveChangesAsync();

        var repository = new PermissionRepository(db);
        (await repository.HasPermissionAsync(
            SubjectId, "test-app:document:read", "doc-1")).Should().BeFalse();
    }
}
