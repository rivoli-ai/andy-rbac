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
/// Regression tests for TEAM role assignment ignoring the application scope —
/// the Teams-path twin of <see cref="RoleServiceApplicationScopeTests"/>
/// (issue #86). RBAC is multi-application and role codes repeat across
/// applications (every andy-* service has its own "admin" role), so
/// <see cref="RoleService.AssignToTeamAsync"/> must resolve roles by
/// (code, applicationCode) — never silently bind whichever application's
/// role happens to match the code first.
/// </summary>
public class TeamRoleServiceApplicationScopeTests
{
    /// <summary>The seeded test-app "admin" role from <see cref="TestDbContextFactory"/>.</summary>
    private static readonly Guid AppARoleId = Guid.Parse("55555555-5555-5555-5555-555555555555");

    /// <summary>Second application's "admin" role, seeded by this fixture.</summary>
    private static readonly Guid AppBRoleId = Guid.Parse("88888888-8888-8888-8888-888888888888");

    /// <summary>The seeded test team.</summary>
    private static readonly Guid TestTeamId = Guid.Parse("77777777-7777-7777-7777-777777777777");

    private readonly Mock<ILogger<RoleService>> _loggerMock = new();

    private RoleService CreateService(RbacDbContext context) =>
        new(context, _loggerMock.Object, new Andy.Rbac.Infrastructure.Messaging.RbacEventPublisher(context));

    /// <summary>
    /// Standard seed data plus a second application ("app-b") that also has a
    /// role coded "admin" — mirroring production, where twelve applications
    /// each carry an "admin" role.
    /// </summary>
    private static async Task<RbacDbContext> CreateContextWithTwoAdminApplicationsAsync()
    {
        var context = await TestDbContextFactory.CreateWithSeedDataAsync();

        var appB = new Application
        {
            Id = Guid.Parse("11111111-1111-1111-1111-222222222222"),
            Code = "app-b",
            Name = "Application B"
        };
        context.Applications.Add(appB);
        context.Roles.Add(new Role
        {
            Id = AppBRoleId,
            ApplicationId = appB.Id,
            Code = "admin",
            Name = "App B Administrator",
            IsSystem = true
        });
        await context.SaveChangesAsync();

        return context;
    }

    [Fact]
    public async Task AssignToTeamAsync_WithApplicationCode_BindsRoleFromThatApplication()
    {
        // Arrange — "admin" exists in both test-app and app-b.
        using var context = await CreateContextWithTwoAdminApplicationsAsync();
        var service = CreateService(context);

        // Act — explicitly ask for app-b's admin.
        var result = await service.AssignToTeamAsync("test-team", "admin", applicationCode: "app-b");

        // Assert — the assignment must reference app-b's role, not test-app's.
        result.Should().StartWith("Successfully assigned");
        var assignment = await context.TeamRoles.SingleAsync(tr => tr.TeamId == TestTeamId);
        assignment.RoleId.Should().Be(AppBRoleId, "the caller asked for app-b's admin role");
        assignment.RoleId.Should().NotBe(AppARoleId);
    }

    [Fact]
    public async Task AssignToTeamAsync_WithAmbiguousCodeAndNoApplicationCode_ReturnsErrorListingApplications()
    {
        // Arrange
        using var context = await CreateContextWithTwoAdminApplicationsAsync();
        var service = CreateService(context);

        // Act — "admin" is ambiguous and no applicationCode was given.
        var result = await service.AssignToTeamAsync("test-team", "admin");

        // Assert — must refuse, listing the candidate applications, and assign nothing.
        result.Should().StartWith("Error:");
        result.Should().Contain("ambiguous");
        result.Should().Contain("test-app");
        result.Should().Contain("app-b");
        (await context.TeamRoles.AnyAsync(tr => tr.TeamId == TestTeamId)).Should().BeFalse(
            "an ambiguous request must never silently pick an application's role");
    }

    [Fact]
    public async Task AssignToTeamAsync_WithUnambiguousCodeAndNoApplicationCode_StillAssigns()
    {
        // Backward compatibility — "editor" exists only in test-app.
        using var context = await CreateContextWithTwoAdminApplicationsAsync();
        var service = CreateService(context);

        var result = await service.AssignToTeamAsync("test-team", "editor");

        result.Should().StartWith("Successfully assigned");
    }

    [Fact]
    public async Task AssignToTeamAsync_WithApplicationCodeNotContainingRole_ReturnsError()
    {
        // "editor" exists only in test-app; asking for it in app-b must fail.
        using var context = await CreateContextWithTwoAdminApplicationsAsync();
        var service = CreateService(context);

        var result = await service.AssignToTeamAsync("test-team", "editor", applicationCode: "app-b");

        result.Should().StartWith("Error:");
        result.Should().Contain("app-b");
        (await context.TeamRoles.AnyAsync(tr => tr.TeamId == TestTeamId)).Should().BeFalse();
    }
}
