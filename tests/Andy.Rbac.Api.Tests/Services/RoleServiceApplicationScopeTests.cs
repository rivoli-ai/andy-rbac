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
/// Regression tests for role assignment ignoring the application scope.
/// RBAC is multi-application and role codes repeat across applications
/// (every andy-* service has its own "admin" role), so assign/revoke must
/// resolve roles by (code, applicationCode) — never silently bind whichever
/// application's role happens to match the code first.
/// </summary>
public class RoleServiceApplicationScopeTests
{
    /// <summary>The seeded test-app "admin" role from <see cref="TestDbContextFactory"/>.</summary>
    private static readonly Guid AppARoleId = Guid.Parse("55555555-5555-5555-5555-555555555555");

    /// <summary>Second application's "admin" role, seeded by this fixture.</summary>
    private static readonly Guid AppBRoleId = Guid.Parse("88888888-8888-8888-8888-888888888888");

    /// <summary>The seeded no-role-user subject.</summary>
    private static readonly Guid NoRoleUserId = Guid.Parse("66666666-6666-6666-6666-666666666669");

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
    public async Task AssignToSubjectAsync_WithApplicationCode_BindsRoleFromThatApplication()
    {
        // Arrange — "admin" exists in both test-app and app-b.
        using var context = await CreateContextWithTwoAdminApplicationsAsync();
        var service = CreateService(context);

        // Act — explicitly ask for app-b's admin.
        var result = await service.AssignToSubjectAsync("no-role-user", "admin", applicationCode: "app-b");

        // Assert — the assignment must reference app-b's role, not test-app's.
        result.Message.Should().StartWith("Successfully assigned");
        var assignment = await context.SubjectRoles.SingleAsync(sr => sr.SubjectId == NoRoleUserId);
        assignment.RoleId.Should().Be(AppBRoleId, "the caller asked for app-b's admin role");
        assignment.RoleId.Should().NotBe(AppARoleId);
    }

    [Fact]
    public async Task AssignToSubjectAsync_WithAmbiguousCodeAndNoApplicationCode_ReturnsErrorListingApplications()
    {
        // Arrange
        using var context = await CreateContextWithTwoAdminApplicationsAsync();
        var service = CreateService(context);

        // Act — "admin" is ambiguous and no applicationCode was given.
        var result = await service.AssignToSubjectAsync("no-role-user", "admin");

        // Assert — must refuse, listing the candidate applications, and assign nothing.
        result.Succeeded.Should().BeFalse();
        result.Message.Should().Contain("ambiguous");
        result.Message.Should().Contain("test-app");
        result.Message.Should().Contain("app-b");
        (await context.SubjectRoles.AnyAsync(sr => sr.SubjectId == NoRoleUserId)).Should().BeFalse(
            "an ambiguous request must never silently pick an application's role");
    }

    [Fact]
    public async Task AssignToSubjectAsync_WithUnambiguousCodeAndNoApplicationCode_StillAssigns()
    {
        // Backward compatibility — "editor" exists only in test-app.
        using var context = await CreateContextWithTwoAdminApplicationsAsync();
        var service = CreateService(context);

        var result = await service.AssignToSubjectAsync("no-role-user", "editor");

        result.Message.Should().StartWith("Successfully assigned");
    }

    [Fact]
    public async Task AssignToSubjectAsync_WithApplicationCodeNotContainingRole_ReturnsError()
    {
        // "editor" exists only in test-app; asking for it in app-b must fail.
        using var context = await CreateContextWithTwoAdminApplicationsAsync();
        var service = CreateService(context);

        var result = await service.AssignToSubjectAsync("no-role-user", "editor", applicationCode: "app-b");

        result.Succeeded.Should().BeFalse();
        result.Message.Should().Contain("app-b");
        (await context.SubjectRoles.AnyAsync(sr => sr.SubjectId == NoRoleUserId)).Should().BeFalse();
    }

    [Fact]
    public async Task RevokeFromSubjectAsync_WithApplicationCode_RevokesOnlyThatApplicationsAssignment()
    {
        // Arrange — no-role-user holds BOTH applications' admin roles.
        using var context = await CreateContextWithTwoAdminApplicationsAsync();
        context.SubjectRoles.AddRange(
            new SubjectRole { SubjectId = NoRoleUserId, RoleId = AppARoleId },
            new SubjectRole { SubjectId = NoRoleUserId, RoleId = AppBRoleId });
        await context.SaveChangesAsync();
        var service = CreateService(context);

        // Act — revoke only app-b's admin.
        var result = await service.RevokeFromSubjectAsync("no-role-user", "admin", applicationCode: "app-b");

        // Assert — app-b assignment gone, test-app assignment untouched.
        result.Message.Should().StartWith("Successfully revoked");
        var remaining = await context.SubjectRoles
            .Where(sr => sr.SubjectId == NoRoleUserId)
            .Select(sr => sr.RoleId)
            .ToListAsync();
        remaining.Should().ContainSingle().Which.Should().Be(AppARoleId);
    }

    [Fact]
    public async Task RevokeFromSubjectAsync_WithAmbiguousCodeAndNoApplicationCode_ReturnsError()
    {
        // Arrange
        using var context = await CreateContextWithTwoAdminApplicationsAsync();
        context.SubjectRoles.AddRange(
            new SubjectRole { SubjectId = NoRoleUserId, RoleId = AppARoleId },
            new SubjectRole { SubjectId = NoRoleUserId, RoleId = AppBRoleId });
        await context.SaveChangesAsync();
        var service = CreateService(context);

        // Act
        var result = await service.RevokeFromSubjectAsync("no-role-user", "admin");

        // Assert — must refuse and revoke nothing.
        result.Succeeded.Should().BeFalse();
        result.Message.Should().Contain("ambiguous");
        (await context.SubjectRoles.CountAsync(sr => sr.SubjectId == NoRoleUserId)).Should().Be(2);
    }
}
