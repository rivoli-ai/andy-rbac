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
/// Regression tests for global-role resolution.
///
/// Roles with <c>ApplicationId == null</c> (the seeded <c>super-admin</c> and
/// <c>user</c>) are global. <see cref="RoleResolver"/> used to require an exact
/// application match whenever an applicationCode was supplied, which excluded
/// every global role — and <c>RbacHttpClient</c> always supplies its configured
/// application code, so global roles were unassignable through the client
/// library entirely. An ambiguous-but-also-global code had no selectable value
/// at all: omitting applicationCode errored as ambiguous, supplying one
/// excluded global.
///
/// The contract is scoped-first-then-global: an application-scoped role of the
/// same code always wins, and the global role is the fallback.
/// </summary>
public class RoleResolverGlobalScopeTests
{
    private readonly Mock<ILogger<RoleService>> _loggerMock = new();

    private RoleService CreateService(RbacDbContext context) =>
        new(context, _loggerMock.Object, new Andy.Rbac.Infrastructure.Messaging.RbacEventPublisher(context));

    /// <summary>Adds a global (application-less) role to the standard seed.</summary>
    private static async Task<Role> AddGlobalRoleAsync(RbacDbContext context, string code)
    {
        var role = new Role
        {
            Id = Guid.NewGuid(),
            ApplicationId = null,
            Code = code,
            Name = $"Global {code}",
            IsSystem = true
        };
        context.Roles.Add(role);
        await context.SaveChangesAsync();
        return role;
    }

    [Fact]
    public async Task ResolveAsync_GlobalOnlyCode_WithApplicationCode_FallsBackToGlobalRole()
    {
        using var context = await TestDbContextFactory.CreateWithSeedDataAsync();
        var global = await AddGlobalRoleAsync(context, "super-admin");

        var (role, _, error) = await RoleResolver.ResolveAsync(
            context, "super-admin", "test-app", CancellationToken.None);

        error.Should().BeNull();
        role.Should().NotBeNull();
        role!.Id.Should().Be(global.Id);
        role.ApplicationId.Should().BeNull();
    }

    [Fact]
    public async Task ResolveAsync_CodeInBothScopes_PrefersApplicationScopedRole()
    {
        using var context = await TestDbContextFactory.CreateWithSeedDataAsync();
        // "admin" already exists scoped to test-app in the standard seed.
        await AddGlobalRoleAsync(context, "admin");

        var (role, _, error) = await RoleResolver.ResolveAsync(
            context, "admin", "test-app", CancellationToken.None);

        error.Should().BeNull();
        role.Should().NotBeNull();
        role!.ApplicationId.Should().NotBeNull(
            "an application-scoped role must win over the global role of the same code");

        var app = await context.Applications.FirstAsync(a => a.Id == role.ApplicationId);
        app.Code.Should().Be("test-app");
    }

    [Fact]
    public async Task ResolveAsync_AmbiguousBareCode_StillReportsAmbiguous()
    {
        using var context = await TestDbContextFactory.CreateWithSeedDataAsync();
        await AddGlobalRoleAsync(context, "admin");

        var (role, kind, error) = await RoleResolver.ResolveAsync(
            context, "admin", applicationCode: null, CancellationToken.None);

        role.Should().BeNull("a bare code matching several scopes must never silently bind one");
        kind.Should().Be(RoleResolutionErrorKind.Ambiguous);
        error.Should().Contain("ambiguous");
    }

    [Fact]
    public async Task ResolveAsync_UnknownCode_WithApplicationCode_ReportsNotFound()
    {
        using var context = await TestDbContextFactory.CreateWithSeedDataAsync();

        var (role, kind, error) = await RoleResolver.ResolveAsync(
            context, "no-such-role", "test-app", CancellationToken.None);

        role.Should().BeNull();
        kind.Should().Be(RoleResolutionErrorKind.NotFound);
        error.Should().NotBeNull();
    }

    [Fact]
    public async Task AssignToSubject_GlobalRole_WithApplicationCode_Succeeds()
    {
        // The end-to-end shape RbacHttpClient.AssignRoleAsync produces: it
        // always sends RbacOptions.ApplicationCode, even for a global role.
        using var context = await TestDbContextFactory.CreateWithSeedDataAsync();
        var global = await AddGlobalRoleAsync(context, "super-admin");
        var subject = await context.Subjects.FirstAsync();
        var service = CreateService(context);

        var result = await service.AssignToSubjectWithExpiryAsync(
            subject.ExternalId, "super-admin", resourceInstanceId: null,
            applicationCode: "test-app", expiresAt: null);

        result.Message.Should().NotStartWith("Error:");
        (await context.SubjectRoles
            .AnyAsync(sr => sr.SubjectId == subject.Id && sr.RoleId == global.Id))
            .Should().BeTrue();
    }

    [Fact]
    public async Task GetByCodeAsync_AmbiguousBareCode_ThrowsRatherThanBindingArbitraryRole()
    {
        using var context = await TestDbContextFactory.CreateWithSeedDataAsync();
        await AddGlobalRoleAsync(context, "admin");
        var service = CreateService(context);

        // "you must scope this" is a different answer from "no such role",
        // so ambiguity surfaces as 400 rather than 404.
        var act = () => service.GetByCodeAsync("admin");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*ambiguous*");
    }

    [Fact]
    public async Task GetByCodeAsync_UnknownCode_ReturnsNullForNotFound()
    {
        using var context = await TestDbContextFactory.CreateWithSeedDataAsync();
        var service = CreateService(context);

        var result = await service.GetByCodeAsync("no-such-role");

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetByCodeAsync_GlobalOnlyCode_WithApplicationCode_ReturnsGlobalRole()
    {
        using var context = await TestDbContextFactory.CreateWithSeedDataAsync();
        await AddGlobalRoleAsync(context, "super-admin");
        var service = CreateService(context);

        var result = await service.GetByCodeAsync("super-admin", "test-app");

        result.Should().NotBeNull();
        result!.Role.Code.Should().Be("super-admin");
        result.Role.ApplicationCode.Should().BeNull();
    }
}
