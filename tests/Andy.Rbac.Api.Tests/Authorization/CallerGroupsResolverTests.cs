using System.Security.Claims;
using Andy.Rbac.Api.Authorization;
using Andy.Rbac.Infrastructure.Data;
using Andy.Rbac.Models;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Andy.Rbac.Api.Tests.Authorization;

/// <summary>
/// Trust rules for caller-asserted external groups.
///
/// Issue #45: a caller must not be able to claim arbitrary groups and collect
/// their mapped permissions. The server therefore derives groups from the
/// validated token for self-checks, and accepts asserted groups only from an
/// active service principal acting on another subject's behalf. Everything else
/// is ignored — and logged, rather than silently dropped, which is what turned
/// this into an undiagnosable false denial in the client.
/// </summary>
public class CallerGroupsResolverTests
{
    private const string Provider = "andy-auth";

    private static CallerGroupsResolver CreateResolver(RbacDbContext db) =>
        new(db, NullLogger<CallerGroupsResolver>.Instance);

    private static ClaimsPrincipal Principal(string sub, string? provider = Provider, params string[] groups)
    {
        var claims = new List<Claim> { new("sub", sub) };
        if (provider is not null)
            claims.Add(new Claim("provider", provider));
        claims.AddRange(groups.Select(g => new Claim("groups", g)));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "Test"));
    }

    private static async Task<Subject> AddSubjectAsync(
        RbacDbContext db, string externalId, SubjectType type, bool isActive = true)
    {
        var subject = new Subject
        {
            Id = Guid.NewGuid(),
            ExternalId = externalId,
            Provider = Provider,
            Type = type,
            IsActive = isActive
        };
        db.Subjects.Add(subject);
        await db.SaveChangesAsync();
        return subject;
    }

    // ---- rule 1: self-check uses the token, never the request -------------

    [Fact]
    public async Task SelfCheck_UsesTokenGroups_NotAssertedGroups()
    {
        using var db = TestDbContextFactory.Create();
        await AddSubjectAsync(db, "user-1", SubjectType.User);
        var user = Principal("user-1", Provider, "from-token");

        var result = await CreateResolver(db).ResolveAsync(
            user, "user-1", Provider, requestedGroups: ["asserted-by-caller"]);

        result.Should().BeEquivalentTo(["from-token"],
            "a caller must not be able to inflate its own group memberships via the request body");
    }

    // ---- rule 2: trusted service principals may assert --------------------

    [Fact]
    public async Task ServicePrincipal_AssertingGroupsForAnotherSubject_IsHonoured()
    {
        using var db = TestDbContextFactory.Create();
        await AddSubjectAsync(db, "svc-conductor", SubjectType.Service);
        await AddSubjectAsync(db, "user-1", SubjectType.User);
        var caller = Principal("svc-conductor");

        var result = await CreateResolver(db).ResolveAsync(
            caller, "user-1", Provider, requestedGroups: ["engineering", "oncall"]);

        result.Should().BeEquivalentTo(["engineering", "oncall"]);
    }

    [Fact]
    public async Task ServicePrincipal_AssertedGroups_AreDeduplicatedAndTrimmedOfBlanks()
    {
        using var db = TestDbContextFactory.Create();
        await AddSubjectAsync(db, "svc-conductor", SubjectType.Service);
        var caller = Principal("svc-conductor");

        var result = await CreateResolver(db).ResolveAsync(
            caller, "user-1", Provider, requestedGroups: ["a", "a", "", "  ", "b"]);

        result.Should().BeEquivalentTo(["a", "b"]);
    }

    [Fact]
    public async Task InactiveServicePrincipal_CannotAssertGroups()
    {
        using var db = TestDbContextFactory.Create();
        await AddSubjectAsync(db, "svc-retired", SubjectType.Service, isActive: false);
        var caller = Principal("svc-retired");

        var result = await CreateResolver(db).ResolveAsync(
            caller, "user-1", Provider, requestedGroups: ["engineering"]);

        result.Should().BeNull("deactivating a service principal must revoke its ability to assert groups");
    }

    // ---- rule 3: everyone else is refused ---------------------------------

    [Fact]
    public async Task HumanUser_AssertingGroupsForAnotherSubject_IsRefused()
    {
        // The #45 escalation: a normal user token claiming another subject's
        // groups to collect their mapped permissions.
        using var db = TestDbContextFactory.Create();
        await AddSubjectAsync(db, "attacker", SubjectType.User);
        await AddSubjectAsync(db, "victim", SubjectType.User);
        var caller = Principal("attacker");

        var result = await CreateResolver(db).ResolveAsync(
            caller, "victim", Provider, requestedGroups: ["administrators"]);

        result.Should().BeNull();
    }

    [Fact]
    public async Task UnknownCaller_AssertingGroups_IsRefused()
    {
        using var db = TestDbContextFactory.Create();
        var caller = Principal("not-provisioned");

        var result = await CreateResolver(db).ResolveAsync(
            caller, "user-1", Provider, requestedGroups: ["administrators"]);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GroupSubjectType_IsNotAServicePrincipal()
    {
        // Only SubjectType.Service is trusted; Group subjects are not.
        using var db = TestDbContextFactory.Create();
        await AddSubjectAsync(db, "some-group", SubjectType.Group);
        var caller = Principal("some-group");

        var result = await CreateResolver(db).ResolveAsync(
            caller, "user-1", Provider, requestedGroups: ["administrators"]);

        result.Should().BeNull();
    }

    [Fact]
    public async Task AnonymousCaller_AssertingGroups_IsRefused()
    {
        using var db = TestDbContextFactory.Create();

        var result = await CreateResolver(db).ResolveAsync(
            user: null, "user-1", Provider, requestedGroups: ["administrators"]);

        result.Should().BeNull();
    }

    [Fact]
    public async Task NoAssertedGroups_ReturnsNull()
    {
        using var db = TestDbContextFactory.Create();
        await AddSubjectAsync(db, "svc-conductor", SubjectType.Service);
        var caller = Principal("svc-conductor");

        var result = await CreateResolver(db).ResolveAsync(
            caller, "user-1", Provider, requestedGroups: null);

        result.Should().BeNull();
    }
}
