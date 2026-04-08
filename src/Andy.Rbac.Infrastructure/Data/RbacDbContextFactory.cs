using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace Andy.Rbac.Infrastructure.Data;

/// <summary>
/// Factory for creating <see cref="RbacDbContext"/> instances at design time
/// (used by <c>dotnet ef</c> migrations tooling).
///
/// Honours the <c>Database__Provider</c> environment variable so a developer
/// can generate migrations against either provider.
/// </summary>
public class RbacDbContextFactory : IDesignTimeDbContextFactory<RbacDbContext>
{
    public RbacDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var provider = DatabaseProviderExtensions.GetDatabaseProvider(configuration);

        var connectionString = provider switch
        {
            DatabaseProvider.Sqlite =>
                configuration.GetConnectionString("Sqlite")
                ?? "Data Source=andy-rbac-design.sqlite",
            DatabaseProvider.PostgreSql =>
                configuration.GetConnectionString("DefaultConnection")
                ?? "Host=localhost;Database=andy_rbac;Username=postgres;Password=postgres",
            _ => throw new InvalidOperationException($"Unsupported provider: {provider}")
        };

        var optionsBuilder = new DbContextOptionsBuilder<RbacDbContext>();
        DatabaseProviderExtensions.ConfigureDbContext(optionsBuilder, provider, connectionString);

        return new RbacDbContext(optionsBuilder.Options);
    }
}
