using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Andy.Rbac.Infrastructure.Data;

/// <summary>
/// Factory for creating DbContext instances at design time (for EF migrations).
/// </summary>
public class RbacDbContextFactory : IDesignTimeDbContextFactory<RbacDbContext>
{
    public RbacDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<RbacDbContext>();
        optionsBuilder.UseNpgsql("Host=localhost;Database=andy_rbac;Username=postgres;Password=postgres");

        return new RbacDbContext(optionsBuilder.Options);
    }
}
