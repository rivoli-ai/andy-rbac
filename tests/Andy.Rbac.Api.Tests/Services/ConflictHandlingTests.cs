using Andy.Rbac.Api.Services;
using Andy.Rbac.Infrastructure.Data;
using Andy.Rbac.Infrastructure.Messaging;
using Andy.Rbac.Models;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Andy.Rbac.Api.Tests.Services;

/// <summary>
/// Issue #118. Duplicate codes and restricted deletes reached the database
/// unguarded and surfaced as an unhandled DbUpdateException — HTTP 500 with a
/// stack trace for what is an ordinary, actionable client mistake. They now
/// raise <see cref="ConflictException"/>, which controllers map to 409.
/// </summary>
public class ConflictHandlingTests
{
    private readonly Mock<ILogger<RoleService>> _roleLoggerMock = new();
    private readonly Mock<ILogger<TeamService>> _teamLoggerMock = new();

    private RoleService CreateRoleService(RbacDbContext db) =>
        new(db, _roleLoggerMock.Object, new RbacEventPublisher(db));

    private TeamService CreateTeamService(RbacDbContext db) => new(db, _teamLoggerMock.Object);

    [Fact]
    public async Task CreateRole_DuplicateCodeInSameApplication_ThrowsConflict()
    {
        using var db = await TestDbContextFactory.CreateWithSeedDataAsync();
        var service = CreateRoleService(db);

        // "admin" already exists scoped to test-app in the standard seed.
        var act = () => service.CreateAsync(new CreateRoleRequest("admin", "Duplicate", null, "test-app"));

        await act.Should().ThrowAsync<ConflictException>().WithMessage("*already exists*");
    }

    [Fact]
    public async Task CreateRole_DuplicateGlobalCode_ThrowsConflict()
    {
        using var db = await TestDbContextFactory.CreateWithSeedDataAsync();
        db.Roles.Add(new Role
        {
            Id = Guid.NewGuid(), ApplicationId = null, Code = "super-admin", Name = "Super Admin"
        });
        await db.SaveChangesAsync();
        var service = CreateRoleService(db);

        var act = () => service.CreateAsync(new CreateRoleRequest("super-admin", "Duplicate", null, null));

        await act.Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task CreateRole_SameCodeInAnotherApplication_IsAllowed()
    {
        using var db = await TestDbContextFactory.CreateWithSeedDataAsync();
        db.Applications.Add(new Application
        {
            Id = Guid.NewGuid(), Code = "other-app", Name = "Other App"
        });
        await db.SaveChangesAsync();
        var service = CreateRoleService(db);

        var result = await service.CreateAsync(new CreateRoleRequest("admin", "Other Admin", null, "other-app"));

        result.Role.Code.Should().Be("admin",
            "role codes are unique per application, not globally");
    }

    [Fact]
    public async Task DeleteTeam_WithChildTeams_ThrowsConflict()
    {
        using var db = await TestDbContextFactory.CreateWithSeedDataAsync();
        var parent = new Team { Id = Guid.NewGuid(), Code = "parent-team", Name = "Parent" };
        var child = new Team
        {
            Id = Guid.NewGuid(), Code = "child-team", Name = "Child", ParentTeamId = parent.Id
        };
        db.Teams.AddRange(parent, child);
        await db.SaveChangesAsync();
        var service = CreateTeamService(db);

        // ParentTeam is OnDelete(Restrict), so this would otherwise throw
        // DbUpdateException from SaveChanges.
        var act = () => service.DeleteAsync(parent.Id);

        await act.Should().ThrowAsync<ConflictException>().WithMessage("*child teams*");
    }

    [Fact]
    public async Task DeleteTeam_WithoutChildTeams_Succeeds()
    {
        using var db = await TestDbContextFactory.CreateWithSeedDataAsync();
        var team = new Team { Id = Guid.NewGuid(), Code = "leaf-team", Name = "Leaf" };
        db.Teams.Add(team);
        await db.SaveChangesAsync();

        (await CreateTeamService(db).DeleteAsync(team.Id)).Should().BeTrue();
        (await db.Teams.FindAsync(team.Id)).Should().BeNull();
    }
}
