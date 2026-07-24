using System.Net.Http.Json;
using Andy.Rbac.Infrastructure.Data;
using Andy.Rbac.Models;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Andy.Rbac.Api.Tests.Integration;

/// <summary>
/// End-to-end coverage for group-derived permissions asserted by a service
/// principal — the case the clients were built around and that the issue #45
/// hardening broke.
///
/// <c>RbacHttpClient</c> has always sent a <c>Groups</c> list on
/// <c>POST /api/check</c>; the server stopped binding it, so a service checking
/// a permission on behalf of a user got a false denial with no diagnostic. The
/// group's mapped permissions must now resolve when the caller is an active
/// service principal, and must not when it isn't.
///
/// Uses its own factory instance so the caller's SubjectType change can't leak
/// into other test classes.
/// </summary>
public class DelegatedGroupCheckTests : IClassFixture<RbacWebApplicationFactory>
{
    private readonly RbacWebApplicationFactory _factory;
    private readonly HttpClient _client;

    private const string TargetUser = "delegated-target-user";
    private const string GroupId = "delegated-engineering";
    private const string Permission = "test-app:document:read";

    public DelegatedGroupCheckTests(RbacWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    /// <summary>
    /// Seeds the target subject, a role carrying <see cref="Permission"/>, and
    /// the external group → role mapping. Sets the calling test principal's
    /// SubjectType, which is what the trust gate keys on.
    /// </summary>
    private async Task SeedAsync(SubjectType callerType)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RbacDbContext>();

        // The caller. Insert before any request so EnsureSubjectMiddleware
        // finds it rather than auto-provisioning it as a User.
        var caller = await db.Subjects.FirstOrDefaultAsync(
            s => s.ExternalId == TestAuthHandler.TestSub);
        if (caller is null)
        {
            caller = new Subject
            {
                Id = Guid.NewGuid(),
                ExternalId = TestAuthHandler.TestSub,
                Provider = "andy-auth",
                Email = TestAuthHandler.TestEmail,
                IsActive = true
            };
            db.Subjects.Add(caller);
        }
        caller.Type = callerType;

        if (!await db.Subjects.AnyAsync(s => s.ExternalId == TargetUser))
        {
            db.Subjects.Add(new Subject
            {
                Id = Guid.NewGuid(),
                ExternalId = TargetUser,
                Provider = "andy-auth",
                Type = SubjectType.User,
                IsActive = true
            });
        }

        if (!await db.ExternalGroupMappings.AnyAsync(m => m.ExternalGroupId == GroupId))
        {
            var app = await db.Applications.FirstAsync(a => a.Code == "test-app");
            var resourceType = await db.ResourceTypes.FirstAsync(
                rt => rt.ApplicationId == app.Id && rt.Code == "document");
            var readAction = await db.Actions.FirstAsync(a => a.Code == "read");

            var permission = await db.Permissions.FirstOrDefaultAsync(
                p => p.ResourceTypeId == resourceType.Id && p.ActionId == readAction.Id);
            if (permission is null)
            {
                permission = new Permission
                {
                    Id = Guid.NewGuid(),
                    ResourceTypeId = resourceType.Id,
                    ActionId = readAction.Id
                };
                db.Permissions.Add(permission);
            }

            var role = new Role
            {
                Id = Guid.NewGuid(),
                ApplicationId = app.Id,
                Code = "delegated-group-role",
                Name = "Delegated Group Role"
            };
            db.Roles.Add(role);
            db.RolePermissions.Add(new RolePermission { RoleId = role.Id, PermissionId = permission.Id });
            db.ExternalGroupMappings.Add(new ExternalGroupMapping
            {
                Id = Guid.NewGuid(),
                Provider = "andy-auth",
                ExternalGroupId = GroupId,
                RoleId = role.Id,
                SyncEnabled = true
            });
        }

        await db.SaveChangesAsync();
    }

    private async Task<bool> CheckWithAssertedGroupAsync()
    {
        // Exactly the payload RbacHttpClient.HasPermissionAsync produces.
        var response = await _client.PostAsJsonAsync("/api/check", new
        {
            SubjectId = TargetUser,
            Permission,
            Groups = new[] { GroupId },
            ResourceInstanceId = (string?)null
        });
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<CheckResponse>(TestJsonOptions.Default);
        return result!.Allowed;
    }

    [Fact]
    public async Task ServicePrincipal_AssertingGroup_ResolvesGroupMappedPermission()
    {
        await SeedAsync(SubjectType.Service);

        (await CheckWithAssertedGroupAsync()).Should().BeTrue(
            "an active service principal may assert the subject's group memberships");
    }

    [Fact]
    public async Task NonServiceCaller_AssertingGroup_IsDenied()
    {
        await SeedAsync(SubjectType.User);

        (await CheckWithAssertedGroupAsync()).Should().BeFalse(
            "a non-service caller must not collect another subject's group-mapped permissions");
    }

    private sealed record CheckResponse(bool Allowed, string? Reason);
}
