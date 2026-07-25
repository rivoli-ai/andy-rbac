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
/// Issue #117. Role and team mutations returned prose, and callers branched on
/// whether it started with "Error:" — the controller picked its status code that
/// way and the gRPC service derived its Success flag from it. Rewording a
/// message would silently turn a 400 into a 200 and a failed RPC into a
/// successful one.
///
/// These pin the outcome to the *situation* rather than to any wording, so a
/// message change can no longer move a status code.
/// </summary>
public class MutationOutcomeTests
{
    private readonly Mock<ILogger<RoleService>> _roleLogger = new();
    private readonly Mock<ILogger<TeamService>> _teamLogger = new();

    private RoleService CreateRoleService(RbacDbContext db) =>
        new(db, _roleLogger.Object, new RbacEventPublisher(db));

    private TeamService CreateTeamService(RbacDbContext db) => new(db, _teamLogger.Object);

    [Fact]
    public async Task MissingSubject_IsNotFound()
    {
        using var db = await TestDbContextFactory.CreateWithSeedDataAsync();

        var result = await CreateRoleService(db).AssignToSubjectAsync("no-such-user", "admin", applicationCode: "test-app");

        result.Outcome.Should().Be(MutationOutcome.NotFound);
        result.Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task MissingRole_IsNotFound()
    {
        using var db = await TestDbContextFactory.CreateWithSeedDataAsync();

        var result = await CreateRoleService(db).AssignToSubjectAsync("admin-user", "no-such-role");

        result.Outcome.Should().Be(MutationOutcome.NotFound);
    }

    [Fact]
    public async Task AmbiguousRoleCode_IsAmbiguous()
    {
        using var db = await TestDbContextFactory.CreateWithSeedDataAsync();
        var otherApp = new Application { Id = Guid.NewGuid(), Code = "other-app", Name = "Other" };
        db.Applications.Add(otherApp);
        db.Roles.Add(new Role
        {
            Id = Guid.NewGuid(), ApplicationId = otherApp.Id, Code = "admin", Name = "Other Admin"
        });
        await db.SaveChangesAsync();

        var result = await CreateRoleService(db).AssignToSubjectAsync("admin-user", "admin");

        result.Outcome.Should().Be(MutationOutcome.Ambiguous,
            "an ambiguous code is a different failure from a missing one, and callers may want to say so");
    }

    [Fact]
    public async Task AmbiguousSubject_IsAmbiguous()
    {
        using var db = await TestDbContextFactory.CreateWithSeedDataAsync();
        foreach (var provider in new[] { "provider-a", "provider-b" })
        {
            db.Subjects.Add(new Subject
            {
                Id = Guid.NewGuid(), ExternalId = "shared", Provider = provider, IsActive = true
            });
        }
        db.Teams.Add(new Team { Id = Guid.NewGuid(), Code = "outcome-team", Name = "Outcome Team" });
        await db.SaveChangesAsync();

        var result = await CreateTeamService(db).AddMemberAsync("outcome-team", "shared");

        result.Outcome.Should().Be(MutationOutcome.Ambiguous);
    }

    [Fact]
    public async Task SuccessfulAssignment_IsOk()
    {
        using var db = await TestDbContextFactory.CreateWithSeedDataAsync();

        var result = await CreateRoleService(db).AssignToSubjectAsync(
            "no-role-user", "viewer", applicationCode: "test-app");

        result.Outcome.Should().Be(MutationOutcome.Ok);
        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task IdempotentNoOps_StayOk()
    {
        // Re-assigning a live role, revoking one that isn't assigned, and
        // removing a non-member all leave the desired state holding. These
        // returned 200 under the string contract and must keep doing so —
        // clients call EnsureSuccessStatusCode.
        using var db = await TestDbContextFactory.CreateWithSeedDataAsync();
        var roles = CreateRoleService(db);

        await roles.AssignToSubjectAsync("no-role-user", "viewer", applicationCode: "test-app");
        var reassign = await roles.AssignToSubjectAsync("no-role-user", "viewer", applicationCode: "test-app");
        reassign.Succeeded.Should().BeTrue();
        reassign.Message.Should().Contain("already assigned");

        var revokeUnassigned = await roles.RevokeFromSubjectAsync(
            "admin-user", "viewer", applicationCode: "test-app");
        revokeUnassigned.Succeeded.Should().BeTrue();
        revokeUnassigned.Message.Should().Contain("not assigned");
    }

    [Fact]
    public void OutcomeIsIndependentOfWording()
    {
        // The property that was missing: a message can say anything without
        // changing what the caller decides.
        MutationResult.NotFound("anything at all").Succeeded.Should().BeFalse();
        MutationResult.Ok("Error: this reads like a failure").Succeeded.Should().BeTrue();
        MutationResult.Ambiguous("").Outcome.Should().Be(MutationOutcome.Ambiguous);
    }
}
