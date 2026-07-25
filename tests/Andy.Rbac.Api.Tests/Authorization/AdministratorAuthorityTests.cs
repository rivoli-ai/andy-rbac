using System.Security.Claims;
using Andy.Rbac.Api.Authorization;
using Andy.Rbac.Infrastructure.Data;
using Andy.Rbac.Infrastructure.Repositories;
using Andy.Rbac.Models;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Andy.Rbac.Api.Tests.Authorization;

/// <summary>
/// Issue #114. Administrator authority used to be read straight off the token:
/// any principal carrying a generic `admin` role claim — including one scoped
/// to a different application — got full global RBAC administration, able to
/// create roles, assign super-admin to anyone and deactivate subjects. The
/// service that owns the authorization model did not apply it to its own
/// privileged surface.
///
/// Authority now comes from andy-rbac's own store. The claim survives as an
/// explicitly-configurable bootstrap, because the first administrator must be
/// able to grant themselves a role before any store-backed grant exists.
/// </summary>
public class AdministratorAuthorityTests
{
    private const string Provider = "test-provider";

    private static AdministratorAuthority CreateAuthority(
        RbacDbContext db, AdministratorAuthorityOptions? options = null) =>
        new(db,
            new PermissionRepository(db),
            Options.Create(options ?? new AdministratorAuthorityOptions()),
            NullLogger<AdministratorAuthority>.Instance);

    private static ClaimsPrincipal Principal(string sub, params string[] roles)
    {
        var claims = new List<Claim> { new("sub", sub), new("provider", Provider) };
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "Test"));
    }

    private static ClaimsPrincipal Anonymous() => new(new ClaimsIdentity());

    /// <summary>Grants the seeded no-role-user a global role of the given code.</summary>
    private static async Task<Subject> GrantGlobalRoleAsync(RbacDbContext db, string externalId, string roleCode)
    {
        var subject = new Subject
        {
            Id = Guid.NewGuid(), ExternalId = externalId, Provider = Provider, IsActive = true
        };
        var role = new Role
        {
            Id = Guid.NewGuid(), ApplicationId = null, Code = roleCode, Name = roleCode, IsSystem = true
        };
        db.Subjects.Add(subject);
        db.Roles.Add(role);
        db.SubjectRoles.Add(new SubjectRole
        {
            Id = Guid.NewGuid(), SubjectId = subject.Id, RoleId = role.Id
        });
        await db.SaveChangesAsync();
        return subject;
    }

    // ---- store-backed authority -------------------------------------------

    [Fact]
    public async Task SubjectHoldingGlobalSuperAdmin_IsAdministrator()
    {
        using var db = await TestDbContextFactory.CreateWithSeedDataAsync();
        await GrantGlobalRoleAsync(db, "real-admin", "super-admin");
        var authority = CreateAuthority(db, new AdministratorAuthorityOptions { AllowClaimBootstrap = false });

        (await authority.IsAdministratorAsync(Principal("real-admin"))).Should().BeTrue();
    }

    [Fact]
    public async Task SubjectWithoutAdministratorRole_IsNotAdministrator()
    {
        using var db = await TestDbContextFactory.CreateWithSeedDataAsync();
        await GrantGlobalRoleAsync(db, "ordinary", "viewer");
        var authority = CreateAuthority(db, new AdministratorAuthorityOptions { AllowClaimBootstrap = false });

        (await authority.IsAdministratorAsync(Principal("ordinary"))).Should().BeFalse();
    }

    [Fact]
    public async Task DeactivatedAdministrator_LosesAuthority()
    {
        using var db = await TestDbContextFactory.CreateWithSeedDataAsync();
        var subject = await GrantGlobalRoleAsync(db, "retired-admin", "super-admin");
        subject.IsActive = false;
        await db.SaveChangesAsync();
        var authority = CreateAuthority(db, new AdministratorAuthorityOptions { AllowClaimBootstrap = false });

        (await authority.IsAdministratorAsync(Principal("retired-admin"))).Should().BeFalse();
    }

    // ---- the #114 escalation ----------------------------------------------

    [Fact]
    public async Task AdminClaimAlone_IsRefusedWhenBootstrapIsOff()
    {
        // The escalation this issue is about: an unscoped admin claim, possibly
        // issued for an entirely different application.
        using var db = await TestDbContextFactory.CreateWithSeedDataAsync();
        var authority = CreateAuthority(db, new AdministratorAuthorityOptions { AllowClaimBootstrap = false });

        (await authority.IsAdministratorAsync(Principal("claims-only", "admin")))
            .Should().BeFalse();
        (await authority.IsAdministratorAsync(Principal("claims-only", "super-admin")))
            .Should().BeFalse();
    }

    // ---- bootstrap path ----------------------------------------------------

    [Fact]
    public async Task AdminClaim_IsHonouredWhileBootstrapIsOn()
    {
        // Default configuration, so an upgrade does not lock out deployments
        // that have not yet granted their administrators a real role.
        using var db = await TestDbContextFactory.CreateWithSeedDataAsync();
        var authority = CreateAuthority(db);

        (await authority.IsAdministratorAsync(Principal("claims-only", "admin"))).Should().BeTrue();
    }

    [Fact]
    public async Task NonAdminClaim_IsRefusedEvenWithBootstrapOn()
    {
        using var db = await TestDbContextFactory.CreateWithSeedDataAsync();
        var authority = CreateAuthority(db);

        (await authority.IsAdministratorAsync(Principal("someone", "viewer", "editor")))
            .Should().BeFalse();
    }

    [Fact]
    public async Task AnonymousPrincipal_IsNeverAdministrator()
    {
        using var db = await TestDbContextFactory.CreateWithSeedDataAsync();

        (await CreateAuthority(db).IsAdministratorAsync(Anonymous())).Should().BeFalse();
    }

    [Fact]
    public async Task StoreBackedRole_WorksIndependentlyOfClaims()
    {
        // A real administrator whose token carries no role claims at all — the
        // case a store-backed model has to support.
        using var db = await TestDbContextFactory.CreateWithSeedDataAsync();
        await GrantGlobalRoleAsync(db, "claimless-admin", "rbac-admin");
        var authority = CreateAuthority(db, new AdministratorAuthorityOptions { AllowClaimBootstrap = false });

        (await authority.IsAdministratorAsync(Principal("claimless-admin"))).Should().BeTrue();
    }

    [Fact]
    public async Task ConfiguredAdministratorRoles_AreHonoured()
    {
        using var db = await TestDbContextFactory.CreateWithSeedDataAsync();
        await GrantGlobalRoleAsync(db, "custom-admin", "platform-owner");
        var authority = CreateAuthority(db, new AdministratorAuthorityOptions
        {
            AdministratorRoles = ["platform-owner"],
            AllowClaimBootstrap = false
        });

        (await authority.IsAdministratorAsync(Principal("custom-admin"))).Should().BeTrue();
    }
}
