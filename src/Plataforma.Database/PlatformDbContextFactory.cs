using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Plataforma.Database;

/// <summary>
/// Creates the platform context for Entity Framework design-time commands.
/// </summary>
public sealed class PlatformDbContextFactory : IDesignTimeDbContextFactory<PlatformDbContext>
{
    /// <inheritdoc />
    public PlatformDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Platform");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Set the ConnectionStrings__Platform environment variable before running EF migrations.");
        }

        var options = new DbContextOptionsBuilder<PlatformDbContext>()
            .UseMySQL(connectionString)
            .Options;

        return new PlatformDbContext(options);
    }
}
