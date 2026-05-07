using Andy.Rbac.Infrastructure.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
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

        builder.UseEnvironment("Testing");
    }
}
