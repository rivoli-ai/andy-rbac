using Andy.Rbac.Infrastructure.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Andy.Rbac.Api.Tests.Integration;

/// <summary>
/// Factory for creating test web application with in-memory database.
/// </summary>
public class RbacWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName = Guid.NewGuid().ToString();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Program.cs requires these config values (no hardcoded fallbacks).
        // Use loopback placeholders — tests don't actually call andy-auth.
        builder.UseSetting("Mcp:ServerUrl", "https://localhost:0");

        // The schema is created below via EnsureCreated on the in-memory
        // provider, so migration-on-startup stays off; seeding is opted into
        // explicitly. Both used to be implied by the environment — seeding ran
        // unconditionally — which left this suite silently depending on
        // production startup behaviour (#113).
        builder.UseSetting("Database:MigrateOnStartup", "false");
        builder.UseSetting("Database:SeedOnStartup", "true");
        builder.UseSetting("AndyAuth:Authority", "https://localhost:0");
        builder.UseSetting("Auth:Authority", "https://localhost:0");
        builder.UseSetting("Auth:Audience", "urn:andy-rbac-api");

        builder.ConfigureServices(services =>
        {
            // Remove the existing DbContext registration
            var descriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<RbacDbContext>));
            if (descriptor != null)
                services.Remove(descriptor);

            // Add in-memory database
            services.AddDbContext<RbacDbContext>(options =>
            {
                options.UseInMemoryDatabase(_databaseName);
            });

            // Build service provider and ensure database is created with seed data
            var sp = services.BuildServiceProvider();
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<RbacDbContext>();
            db.Database.EnsureCreated();

            // Seed test data
            TestDbContextFactory.SeedTestDataAsync(db).GetAwaiter().GetResult();
        });

        // Replace the auth scheme with a test handler that always succeeds
        // as a fixed in-memory test user. Runs in ConfigureTestServices so
        // it overrides Program.cs's Bearer/MCP scheme registration.
        builder.ConfigureTestServices(services =>
        {
            services.AddAuthentication(TestAuthHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                    TestAuthHandler.SchemeName, _ => { });
        });

        builder.UseEnvironment("Testing");
    }
}
