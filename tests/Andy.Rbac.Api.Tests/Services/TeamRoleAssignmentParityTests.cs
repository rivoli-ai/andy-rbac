using Andy.Rbac.Api.Services;
using Andy.Rbac.Infrastructure.Data;
using Andy.Rbac.Infrastructure.Messaging;
using Andy.Rbac.Messaging.Events;
using Andy.Rbac.Models;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Andy.Rbac.Api.Tests.Services;

/// <summary>
/// Issue #111. Team-role assignment lagged behind the subject-side path:
/// the duplicate check ignored expiry, so an expired team grant reported
/// "already assigned" forever while the evaluator treated it as dead — an
/// unrenewable grant; there was no way to scope a grant to a resource instance
/// even though that column is in the unique index; and no event was published,
/// so team grants never reached the outbox.
/// </summary>
public class TeamRoleAssignmentParityTests
{
    private readonly Mock<ILogger<RoleService>> _loggerMock = new();

    private RoleService CreateService(RbacDbContext db) =>
        new(db, _loggerMock.Object, new RbacEventPublisher(db));

    private static async Task<(RbacDbContext db, Team team, Role role)> CreateDbWithTeamAndRoleAsync()
    {
        var db = await TestDbContextFactory.CreateWithSeedDataAsync();
        var app = await db.Applications.FirstAsync(a => a.Code == "test-app");

        var team = new Team { Id = Guid.NewGuid(), Code = "parity-team", Name = "Parity Team" };
        var role = new Role
        {
            Id = Guid.NewGuid(),
            ApplicationId = app.Id,
            Code = "parity-role",
            Name = "Parity Role"
        };
        db.Teams.Add(team);
        db.Roles.Add(role);
        await db.SaveChangesAsync();

        return (db, team, role);
    }

    [Fact]
    public async Task ExpiredTeamRole_CanBeReassigned()
    {
        var (db, team, role) = await CreateDbWithTeamAndRoleAsync();
        using var _db = db;

        db.TeamRoles.Add(new TeamRole
        {
            Id = Guid.NewGuid(),
            TeamId = team.Id,
            RoleId = role.Id,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(-1)
        });
        await db.SaveChangesAsync();

        var result = await CreateService(db).AssignToTeamAsync("parity-team", "parity-role", "test-app");

        result.Message.Should().NotContain("already assigned",
            "an expired grant is dead to the evaluator and must be renewable");
        result.Message.Should().StartWith("Successfully");

        var assignment = await db.TeamRoles.SingleAsync(tr => tr.TeamId == team.Id && tr.RoleId == role.Id);
        assignment.ExpiresAt.Should().BeNull("re-assigning without an expiry clears the old one");
    }

    [Fact]
    public async Task LiveTeamRole_IsStillReportedAsAlreadyAssigned()
    {
        var (db, team, role) = await CreateDbWithTeamAndRoleAsync();
        using var _db = db;

        db.TeamRoles.Add(new TeamRole
        {
            Id = Guid.NewGuid(),
            TeamId = team.Id,
            RoleId = role.Id,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(1)
        });
        await db.SaveChangesAsync();

        var result = await CreateService(db).AssignToTeamAsync("parity-team", "parity-role", "test-app");

        result.Message.Should().Contain("already assigned");
    }

    [Fact]
    public async Task InstanceScopedGrant_IsDistinctFromGlobalGrant()
    {
        var (db, team, role) = await CreateDbWithTeamAndRoleAsync();
        using var _db = db;
        var service = CreateService(db);

        await service.AssignToTeamWithExpiryAsync(
            "parity-team", "parity-role", resourceInstanceId: null,
            applicationCode: "test-app", expiresAt: null);
        var scoped = await service.AssignToTeamWithExpiryAsync(
            "parity-team", "parity-role", resourceInstanceId: "doc-1",
            applicationCode: "test-app", expiresAt: null);

        scoped.Message.Should().StartWith("Successfully",
            "the unique index is (TeamId, RoleId, ResourceInstanceId) — these are different grants");

        var assignments = await db.TeamRoles
            .Where(tr => tr.TeamId == team.Id && tr.RoleId == role.Id)
            .ToListAsync();
        assignments.Should().HaveCount(2);
        assignments.Select(a => a.ResourceInstanceId).Should().BeEquivalentTo([null, "doc-1"]);
    }

    [Fact]
    public async Task Assignment_StagesTeamRoleEvent()
    {
        var (db, team, role) = await CreateDbWithTeamAndRoleAsync();
        using var _db = db;

        await CreateService(db).AssignToTeamWithExpiryAsync(
            "parity-team", "parity-role", resourceInstanceId: "doc-1",
            applicationCode: "test-app", expiresAt: null);

        var assignment = await db.TeamRoles.SingleAsync(tr => tr.TeamId == team.Id);
        var entry = await db.Outbox.SingleAsync();

        entry.Subject.Should().Be($"andy.rbac.events.team_role.{assignment.Id}.granted");
        entry.PayloadType.Should().Be(typeof(TeamRoleAssigned).FullName);
        entry.PayloadJson.Should().Contain("parity-team");
        entry.PayloadJson.Should().Contain("parity-role");
        entry.PayloadJson.Should().Contain("doc-1");
    }

    [Fact]
    public async Task TeamRoleEvent_IsNotASubjectRoleEvent()
    {
        // A team grant reaches every current and future member, so a consumer
        // must re-expand membership rather than record one identity. Emitting it
        // on the subject_role subject would have consumers register a subject
        // that does not exist.
        var (db, _, _) = await CreateDbWithTeamAndRoleAsync();
        using var _db = db;

        await CreateService(db).AssignToTeamAsync("parity-team", "parity-role", "test-app");

        var entry = await db.Outbox.SingleAsync();
        entry.Subject.Should().NotContain("subject_role");
        entry.Subject.Should().Contain("team_role");
    }

    [Fact]
    public async Task ExpiredGrantRenewal_ReusesTheAssignmentRow()
    {
        var (db, team, role) = await CreateDbWithTeamAndRoleAsync();
        using var _db = db;

        var originalId = Guid.NewGuid();
        db.TeamRoles.Add(new TeamRole
        {
            Id = originalId,
            TeamId = team.Id,
            RoleId = role.Id,
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(-1)
        });
        await db.SaveChangesAsync();

        var future = DateTimeOffset.UtcNow.AddDays(30);
        await CreateService(db).AssignToTeamWithExpiryAsync(
            "parity-team", "parity-role", resourceInstanceId: null,
            applicationCode: "test-app", expiresAt: future);

        var assignment = await db.TeamRoles.SingleAsync(tr => tr.TeamId == team.Id && tr.RoleId == role.Id);
        assignment.Id.Should().Be(originalId, "renewal updates in place rather than orphaning a row");
        assignment.ExpiresAt.Should().BeCloseTo(future, TimeSpan.FromSeconds(1));
    }
}
