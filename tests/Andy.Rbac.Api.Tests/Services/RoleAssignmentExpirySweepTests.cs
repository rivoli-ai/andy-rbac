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
/// Issue #121. The server-side expiry sweep covered InstancePermission only.
/// Time-boxed role assignments — the expiresAt on POST /api/roles/assign and
/// IRbacClient.AssignRoleAsync — were honoured lazily by the evaluator but
/// never announced, so consumers holding cached permissions kept authorising
/// until their own TTL lapsed, and dead rows accumulated indefinitely.
/// </summary>
public class RoleAssignmentExpirySweepTests
{
    private static readonly Guid AdminSubjectId = Guid.Parse("66666666-6666-6666-6666-666666666666");

    private static GrantService CreateService(RbacDbContext db) =>
        new(db, new RbacEventPublisher(db), Mock.Of<ILogger<GrantService>>());

    private static async Task<(RbacDbContext db, Role role, Team team)> CreateDbAsync()
    {
        var db = await TestDbContextFactory.CreateWithSeedDataAsync();
        var app = await db.Applications.FirstAsync(a => a.Code == "test-app");

        var role = new Role
        {
            Id = Guid.NewGuid(), ApplicationId = app.Id, Code = "temp-role", Name = "Temp Role"
        };
        var team = new Team { Id = Guid.NewGuid(), Code = "sweep-team", Name = "Sweep Team" };
        db.Roles.Add(role);
        db.Teams.Add(team);
        await db.SaveChangesAsync();

        return (db, role, team);
    }

    [Fact]
    public async Task ExpiredSubjectRole_IsRemovedAndAnnounced()
    {
        var (db, role, _) = await CreateDbAsync();
        using var _db = db;

        var expiresAt = DateTimeOffset.UtcNow.AddHours(-1);
        var assignment = new SubjectRole
        {
            Id = Guid.NewGuid(), SubjectId = AdminSubjectId, RoleId = role.Id, ExpiresAt = expiresAt
        };
        db.SubjectRoles.Add(assignment);
        await db.SaveChangesAsync();

        var swept = await CreateService(db).SweepExpiredRoleAssignmentsAsync();

        swept.Should().Be(1);
        (await db.SubjectRoles.FindAsync(assignment.Id)).Should().BeNull();

        var entry = await db.Outbox.SingleAsync();
        entry.Subject.Should().Be($"andy.rbac.events.subject_role.{assignment.Id}.expired");
        entry.PayloadType.Should().Be(typeof(RoleExpired).FullName);
        entry.PayloadJson.Should().Contain("temp-role");
    }

    [Fact]
    public async Task ExpiredTeamRole_IsRemovedAndAnnounced()
    {
        var (db, role, team) = await CreateDbAsync();
        using var _db = db;

        var assignment = new TeamRole
        {
            Id = Guid.NewGuid(), TeamId = team.Id, RoleId = role.Id,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-5)
        };
        db.TeamRoles.Add(assignment);
        await db.SaveChangesAsync();

        var swept = await CreateService(db).SweepExpiredRoleAssignmentsAsync();

        swept.Should().Be(1);
        (await db.TeamRoles.FindAsync(assignment.Id)).Should().BeNull();

        var entry = await db.Outbox.SingleAsync();
        entry.Subject.Should().Be($"andy.rbac.events.team_role.{assignment.Id}.expired");
        entry.PayloadType.Should().Be(typeof(TeamRoleExpired).FullName);
        entry.PayloadJson.Should().Contain("sweep-team");
    }

    [Fact]
    public async Task UnexpiredAndNonExpiringAssignments_AreLeftAlone()
    {
        var (db, role, team) = await CreateDbAsync();
        using var _db = db;

        var future = new SubjectRole
        {
            Id = Guid.NewGuid(), SubjectId = AdminSubjectId, RoleId = role.Id,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(1)
        };
        var permanent = new TeamRole
        {
            Id = Guid.NewGuid(), TeamId = team.Id, RoleId = role.Id, ExpiresAt = null
        };
        db.SubjectRoles.Add(future);
        db.TeamRoles.Add(permanent);
        await db.SaveChangesAsync();

        var swept = await CreateService(db).SweepExpiredRoleAssignmentsAsync();

        swept.Should().Be(0);
        (await db.SubjectRoles.FindAsync(future.Id)).Should().NotBeNull();
        (await db.TeamRoles.FindAsync(permanent.Id)).Should().NotBeNull();
        (await db.Outbox.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task ExpiryEvent_IsDistinctFromRevocation()
    {
        // "an admin took this away" and "your temporary grant lapsed" are
        // different facts — the same distinction GrantExpired draws against
        // GrantRevoked.
        var (db, role, _) = await CreateDbAsync();
        using var _db = db;

        db.SubjectRoles.Add(new SubjectRole
        {
            Id = Guid.NewGuid(), SubjectId = AdminSubjectId, RoleId = role.Id,
            ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(-1)
        });
        await db.SaveChangesAsync();

        await CreateService(db).SweepExpiredRoleAssignmentsAsync();

        var entry = await db.Outbox.SingleAsync();
        entry.Subject.Should().EndWith(".expired");
        entry.Subject.Should().NotContain(".revoked");
    }

    [Fact]
    public async Task SweepIsIdempotent()
    {
        var (db, role, _) = await CreateDbAsync();
        using var _db = db;

        db.SubjectRoles.Add(new SubjectRole
        {
            Id = Guid.NewGuid(), SubjectId = AdminSubjectId, RoleId = role.Id,
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(-2)
        });
        await db.SaveChangesAsync();
        var service = CreateService(db);

        (await service.SweepExpiredRoleAssignmentsAsync()).Should().Be(1);
        (await service.SweepExpiredRoleAssignmentsAsync()).Should().Be(0,
            "a second tick must not re-announce what it already swept");

        (await db.Outbox.CountAsync()).Should().Be(1);
    }
}
