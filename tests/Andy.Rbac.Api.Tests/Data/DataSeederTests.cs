using Andy.Rbac.Api.Data;
using Andy.Rbac.Infrastructure.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Andy.Rbac.Api.Tests.Data;

public class DataSeederTests
{
    private static RbacDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<RbacDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var context = new RbacDbContext(options);
        context.Database.EnsureCreated();
        return context;
    }

    [Fact]
    public async Task SeedAsync_SeedsActions()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        await DataSeeder.SeedAsync(context);

        // Assert
        var actions = await context.Actions.ToListAsync();
        actions.Should().Contain(a => a.Code == "read");
        actions.Should().Contain(a => a.Code == "write");
        actions.Should().Contain(a => a.Code == "delete");
        actions.Should().Contain(a => a.Code == "share");
        actions.Should().Contain(a => a.Code == "admin");
        actions.Should().Contain(a => a.Code == "execute");
        actions.Should().Contain(a => a.Code == "export");
        actions.Should().Contain(a => a.Code == "import");
    }

    [Fact]
    public async Task SeedAsync_SeedsApplications()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        await DataSeeder.SeedAsync(context);

        // Assert
        var apps = await context.Applications.ToListAsync();
        apps.Should().Contain(a => a.Code == "andy-auth");
        apps.Should().Contain(a => a.Code == "andy-docs");
        apps.Should().Contain(a => a.Code == "andy-cli");
        apps.Should().Contain(a => a.Code == "andy-agentic-web");
        apps.Should().Contain(a => a.Code == "andy-rbac");
    }

    [Fact]
    public async Task SeedAsync_SeedsGlobalRoles()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        await DataSeeder.SeedAsync(context);

        // Assert
        var roles = await context.Roles.Where(r => r.ApplicationId == null).ToListAsync();
        roles.Should().Contain(r => r.Code == "super-admin");
        roles.Should().Contain(r => r.Code == "user");
        roles.All(r => r.IsSystem).Should().BeTrue();
    }

    [Fact]
    public async Task SeedAsync_IsIdempotent()
    {
        // Arrange
        using var context = CreateContext();

        // Act - run seed twice
        await DataSeeder.SeedAsync(context);
        await DataSeeder.SeedAsync(context);

        // Assert - should not create duplicates
        var actions = await context.Actions.Where(a => a.Code == "read").ToListAsync();
        actions.Should().ContainSingle();

        var apps = await context.Applications.Where(a => a.Code == "andy-docs").ToListAsync();
        apps.Should().ContainSingle();

        var roles = await context.Roles.Where(r => r.Code == "super-admin" && r.ApplicationId == null).ToListAsync();
        roles.Should().ContainSingle();
    }

    [Fact]
    public async Task SeedApplicationDataAsync_WithAndyDocs_SeedsResourceTypesAndRoles()
    {
        // Arrange
        using var context = CreateContext();
        await DataSeeder.SeedAsync(context);

        // Act
        await DataSeeder.SeedApplicationDataAsync(context, "andy-docs");

        // Assert
        var app = await context.Applications
            .Include(a => a.ResourceTypes)
            .Include(a => a.Roles)
            .FirstOrDefaultAsync(a => a.Code == "andy-docs");

        app.Should().NotBeNull();
        app!.ResourceTypes.Should().Contain(rt => rt.Code == "document");
        app.ResourceTypes.Should().Contain(rt => rt.Code == "collection");
        app.ResourceTypes.Should().Contain(rt => rt.Code == "template");

        app.Roles.Should().Contain(r => r.Code == "admin");
        app.Roles.Should().Contain(r => r.Code == "editor");
        app.Roles.Should().Contain(r => r.Code == "viewer");
    }

    [Fact]
    public async Task SeedApplicationDataAsync_WithAndyCli_SeedsResourceTypesAndRoles()
    {
        // Arrange
        using var context = CreateContext();
        await DataSeeder.SeedAsync(context);

        // Act
        await DataSeeder.SeedApplicationDataAsync(context, "andy-cli");

        // Assert
        var app = await context.Applications
            .Include(a => a.ResourceTypes)
            .Include(a => a.Roles)
            .FirstOrDefaultAsync(a => a.Code == "andy-cli");

        app.Should().NotBeNull();
        app!.ResourceTypes.Should().Contain(rt => rt.Code == "config");
        app.ResourceTypes.Should().Contain(rt => rt.Code == "session");
        app.ResourceTypes.Should().Contain(rt => rt.Code == "tool");

        app.Roles.Should().Contain(r => r.Code == "admin");
        app.Roles.Should().Contain(r => r.Code == "user");
        app.Roles.Should().Contain(r => r.Code == "restricted");
    }

    [Fact]
    public async Task SeedApplicationDataAsync_WithAndyAuth_SeedsResourceTypesAndRoles()
    {
        // Arrange
        using var context = CreateContext();
        await DataSeeder.SeedAsync(context);

        // Act
        await DataSeeder.SeedApplicationDataAsync(context, "andy-auth");

        // Assert
        var app = await context.Applications
            .Include(a => a.ResourceTypes)
            .Include(a => a.Roles)
            .FirstOrDefaultAsync(a => a.Code == "andy-auth");

        app.Should().NotBeNull();
        app!.ResourceTypes.Should().Contain(rt => rt.Code == "user");
        app.ResourceTypes.Should().Contain(rt => rt.Code == "client");
        app.ResourceTypes.Should().Contain(rt => rt.Code == "scope");

        app.Roles.Should().Contain(r => r.Code == "admin");
        app.Roles.Should().Contain(r => r.Code == "user-manager");
    }

    [Fact]
    public async Task SeedApplicationDataAsync_WithAndyAgenticWeb_SeedsResourceTypesAndRoles()
    {
        // Arrange
        using var context = CreateContext();
        await DataSeeder.SeedAsync(context);

        // Act
        await DataSeeder.SeedApplicationDataAsync(context, "andy-agentic-web");

        // Assert
        var app = await context.Applications
            .Include(a => a.ResourceTypes)
            .Include(a => a.Roles)
            .FirstOrDefaultAsync(a => a.Code == "andy-agentic-web");

        app.Should().NotBeNull();
        app!.ResourceTypes.Should().Contain(rt => rt.Code == "setup");
        app.ResourceTypes.Should().Contain(rt => rt.Code == "conversation");
        app.ResourceTypes.Should().Contain(rt => rt.Code == "workspace");

        app.Roles.Should().Contain(r => r.Code == "admin");
        app.Roles.Should().Contain(r => r.Code == "user");
    }

    [Fact]
    public async Task SeedApplicationDataAsync_WithNonExistentApp_DoesNotThrow()
    {
        // Arrange
        using var context = CreateContext();
        await DataSeeder.SeedAsync(context);

        // Act & Assert - should not throw
        await DataSeeder.SeedApplicationDataAsync(context, "non-existent-app");
    }

    [Fact]
    public async Task SeedApplicationDataAsync_IsIdempotent()
    {
        // Arrange
        using var context = CreateContext();
        await DataSeeder.SeedAsync(context);

        // Act - run twice
        await DataSeeder.SeedApplicationDataAsync(context, "andy-docs");
        await DataSeeder.SeedApplicationDataAsync(context, "andy-docs");

        // Assert - should not create duplicates
        var app = await context.Applications
            .Include(a => a.ResourceTypes)
            .Include(a => a.Roles)
            .FirstOrDefaultAsync(a => a.Code == "andy-docs");

        var docTypes = app!.ResourceTypes.Where(rt => rt.Code == "document").ToList();
        docTypes.Should().ContainSingle();

        var adminRoles = app.Roles.Where(r => r.Code == "admin").ToList();
        adminRoles.Should().ContainSingle();
    }

    [Fact]
    public async Task SeedApplicationDataAsync_ResourceTypes_HaveCorrectSupportsInstancesValue()
    {
        // Arrange
        using var context = CreateContext();
        await DataSeeder.SeedAsync(context);
        await DataSeeder.SeedApplicationDataAsync(context, "andy-docs");
        await DataSeeder.SeedApplicationDataAsync(context, "andy-cli");

        // Assert
        var docType = await context.ResourceTypes.FirstOrDefaultAsync(rt => rt.Code == "document");
        docType!.SupportsInstances.Should().BeTrue();

        var configType = await context.ResourceTypes.FirstOrDefaultAsync(rt => rt.Code == "config");
        configType!.SupportsInstances.Should().BeFalse();
    }

    [Fact]
    public async Task SeedAsync_Actions_HaveSortOrder()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        await DataSeeder.SeedAsync(context);

        // Assert
        var readAction = await context.Actions.FirstOrDefaultAsync(a => a.Code == "read");
        var writeAction = await context.Actions.FirstOrDefaultAsync(a => a.Code == "write");
        var deleteAction = await context.Actions.FirstOrDefaultAsync(a => a.Code == "delete");

        readAction!.SortOrder.Should().BeLessThan(writeAction!.SortOrder);
        writeAction.SortOrder.Should().BeLessThan(deleteAction!.SortOrder);
    }

    [Fact]
    public async Task SeedApplicationDataAsync_Roles_AreSystemRoles()
    {
        // Arrange
        using var context = CreateContext();
        await DataSeeder.SeedAsync(context);
        await DataSeeder.SeedApplicationDataAsync(context, "andy-docs");

        // Assert
        var app = await context.Applications
            .Include(a => a.Roles)
            .FirstOrDefaultAsync(a => a.Code == "andy-docs");

        app!.Roles.Should().OnlyContain(r => r.IsSystem);
    }

    // -- testUserRole binding (#52) ------------------------------------------

    private const string SampleManifestWithTestUserRole = """
        {
          "service": {
            "name": "andy-policies-test",
            "displayName": "Andy Policies (Test Manifest)",
            "description": "Fixture",
            "embeddedProxyPrefix": "/policies"
          },
          "rbac": {
            "applicationCode": "andy-policies-test",
            "applicationName": "Andy Policies (Test)",
            "description": "Fixture for DataSeederTests",
            "resourceTypes": [
              { "code": "policy", "name": "Policy", "supportsInstances": true }
            ],
            "roles": [
              { "code": "admin", "name": "Administrator", "isSystem": true },
              { "code": "viewer", "name": "Viewer", "isSystem": true }
            ],
            "testUserRole": "admin"
          }
        }
        """;

    private static IConfiguration ConfigurationWithManifest(string manifestPath, string env = "Development")
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ASPNETCORE_ENVIRONMENT"] = env,
                ["Registrations:ManifestPaths:0"] = manifestPath,
            })
            .Build();
    }

    private static string WriteTempManifest(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"manifest-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public async Task SeedFromManifestsAsync_WithTestUserRole_BindsRoleToWellKnownTestUser()
    {
        using var context = CreateContext();
        var manifestPath = WriteTempManifest(SampleManifestWithTestUserRole);
        try
        {
            var config = ConfigurationWithManifest(manifestPath);
            var logger = NullLogger.Instance;

            await DataSeeder.SeedFromManifestsAsync(context, config, logger);

            var subject = await context.Subjects
                .FirstOrDefaultAsync(s => s.ExternalId == DataSeeder.TestUserWellKnownExternalId);
            subject.Should().NotBeNull();
            subject!.Email.Should().Be(DataSeeder.TestUserWellKnownEmail);

            var binding = await context.SubjectRoles
                .Include(sr => sr.Role)
                .ThenInclude(r => r.Application)
                .FirstOrDefaultAsync(sr => sr.SubjectId == subject.Id);
            binding.Should().NotBeNull();
            binding!.Role.Code.Should().Be("admin");
            binding.Role.Application!.Code.Should().Be("andy-policies-test");
        }
        finally
        {
            File.Delete(manifestPath);
        }
    }

    [Fact]
    public async Task SeedFromManifestsAsync_TestUserRole_IsIdempotent()
    {
        using var context = CreateContext();
        var manifestPath = WriteTempManifest(SampleManifestWithTestUserRole);
        try
        {
            var config = ConfigurationWithManifest(manifestPath);
            var logger = NullLogger.Instance;

            await DataSeeder.SeedFromManifestsAsync(context, config, logger);
            await DataSeeder.SeedFromManifestsAsync(context, config, logger);

            (await context.Subjects.CountAsync(s => s.ExternalId == DataSeeder.TestUserWellKnownExternalId))
                .Should().Be(1);
            (await context.SubjectRoles.CountAsync()).Should().Be(1);
        }
        finally
        {
            File.Delete(manifestPath);
        }
    }

    [Fact]
    public async Task SeedFromManifestsAsync_InProduction_SkipsTestUserRoleBinding()
    {
        using var context = CreateContext();
        var manifestPath = WriteTempManifest(SampleManifestWithTestUserRole);
        try
        {
            var config = ConfigurationWithManifest(manifestPath, env: "Production");
            var logger = NullLogger.Instance;

            await DataSeeder.SeedFromManifestsAsync(context, config, logger);

            // Application + roles still seed normally; only the testUserRole
            // binding is skipped.
            (await context.Applications.AnyAsync(a => a.Code == "andy-policies-test")).Should().BeTrue();
            (await context.Subjects.AnyAsync(s => s.ExternalId == DataSeeder.TestUserWellKnownExternalId))
                .Should().BeFalse();
            (await context.SubjectRoles.AnyAsync()).Should().BeFalse();
        }
        finally
        {
            File.Delete(manifestPath);
        }
    }

    [Fact]
    public void TestUserWellKnownExternalId_MatchesAndyAuthConstant()
    {
        // Locks the constant — andy-auth's DbSeeder.TestUserWellKnownId must
        // match. If you change one, change both. See rivoli-ai/andy-auth#56.
        DataSeeder.TestUserWellKnownExternalId.Should().Be("00000000-0000-0000-0000-000000000001");
        DataSeeder.TestUserWellKnownEmail.Should().Be("test@andy.local");
    }
}
