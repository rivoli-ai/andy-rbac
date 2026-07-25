using Andy.Rbac.Api.Services;
using Andy.Rbac.Infrastructure.Data;
using Andy.Rbac.Models;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Andy.Rbac.Api.Tests.Services;

/// <summary>
/// Issue #123. Subject external IDs are unique only per provider — the key is
/// (Provider, ExternalId) — so every mutation path grew a provider-aware
/// overload. TeamService was missed: both member operations hard-coded
/// provider: null and then told the caller to "specify its provider", with no
/// parameter to specify it through. A subject whose external ID existed under
/// two providers could not be added to or removed from a team at all.
/// </summary>
public class TeamMemberProviderScopeTests
{
    private readonly Mock<ILogger<TeamService>> _loggerMock = new();

    private TeamService CreateService(RbacDbContext db) => new(db, _loggerMock.Object);

    /// <summary>Seeds one external ID under two providers, plus a team.</summary>
    private static async Task<(RbacDbContext db, Subject a, Subject b)> CreateDbWithAmbiguousSubjectAsync()
    {
        var db = await TestDbContextFactory.CreateWithSeedDataAsync();

        var a = new Subject
        {
            Id = Guid.NewGuid(), ExternalId = "shared-id", Provider = "provider-a", IsActive = true
        };
        var b = new Subject
        {
            Id = Guid.NewGuid(), ExternalId = "shared-id", Provider = "provider-b", IsActive = true
        };
        db.Subjects.AddRange(a, b);
        db.Teams.Add(new Team { Id = Guid.NewGuid(), Code = "ambig-team", Name = "Ambiguous Team" });
        await db.SaveChangesAsync();

        return (db, a, b);
    }

    [Fact]
    public async Task AddMember_WithoutProvider_StillReportsAmbiguity()
    {
        var (db, _, _) = await CreateDbWithAmbiguousSubjectAsync();
        using var _db = db;

        var result = await CreateService(db).AddMemberAsync("ambig-team", "shared-id");

        result.Outcome.Should().Be(MutationOutcome.Ambiguous);
        result.Message.Should().Contain("ambiguous");
    }

    [Fact]
    public async Task AddMember_WithProvider_BindsTheRequestedSubject()
    {
        var (db, _, b) = await CreateDbWithAmbiguousSubjectAsync();
        using var _db = db;

        var result = await CreateService(db).AddMemberAsync(
            "ambig-team", "shared-id", TeamMembershipRole.Member, subjectProvider: "provider-b");

        result.Message.Should().StartWith("Successfully");

        var team = await db.Teams.FirstAsync(t => t.Code == "ambig-team");
        var members = await db.TeamMembers.Where(tm => tm.TeamId == team.Id).ToListAsync();
        members.Should().ContainSingle().Which.SubjectId.Should().Be(b.Id,
            "the provider selects which of the two same-coded subjects joins");
    }

    [Fact]
    public async Task RemoveMember_WithProvider_RemovesOnlyThatSubject()
    {
        var (db, a, b) = await CreateDbWithAmbiguousSubjectAsync();
        using var _db = db;
        var service = CreateService(db);

        await service.AddMemberAsync("ambig-team", "shared-id", TeamMembershipRole.Member, "provider-a");
        await service.AddMemberAsync("ambig-team", "shared-id", TeamMembershipRole.Member, "provider-b");

        var result = await service.RemoveMemberAsync("ambig-team", "shared-id", subjectProvider: "provider-a");

        result.Message.Should().StartWith("Successfully");

        var team = await db.Teams.FirstAsync(t => t.Code == "ambig-team");
        var remaining = await db.TeamMembers.Where(tm => tm.TeamId == team.Id).ToListAsync();
        remaining.Should().ContainSingle().Which.SubjectId.Should().Be(b.Id);
    }

    [Fact]
    public async Task AddMember_WithWrongProvider_ReportsNotFound()
    {
        var (db, _, _) = await CreateDbWithAmbiguousSubjectAsync();
        using var _db = db;

        var result = await CreateService(db).AddMemberAsync(
            "ambig-team", "shared-id", TeamMembershipRole.Member, subjectProvider: "provider-c");

        result.Outcome.Should().Be(MutationOutcome.NotFound);
        result.Message.Should().Contain("not found");
    }

    [Fact]
    public async Task AddMember_UnambiguousSubject_StillWorksWithoutProvider()
    {
        var (db, _, _) = await CreateDbWithAmbiguousSubjectAsync();
        using var _db = db;

        // viewer-user comes from the standard seed under a single provider.
        var result = await CreateService(db).AddMemberAsync("ambig-team", "viewer-user");

        result.Message.Should().StartWith("Successfully",
            "the provider argument is optional, not required");
    }
}
