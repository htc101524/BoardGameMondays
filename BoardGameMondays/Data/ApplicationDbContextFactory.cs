using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace BoardGameMondays.Data;

public sealed class ApplicationDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        // IMPORTANT: Migrations are intended for production (Azure SQL / SQL Server).
        // Developers may run SQLite locally, but EF migrations must be generated against SQL Server
        // to avoid provider-specific model diffs that cause PendingModelChangesWarning at runtime.

        var basePath = Directory.GetCurrentDirectory();

        var configuration = new ConfigurationBuilder()
            .SetBasePath(basePath)
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration.GetConnectionString("DefaultConnection");

        // If the configured connection string is SQLite, fall back to a local SQL Server-formatted
        // string purely for migrations generation (no connection is required to scaffold migrations).
        if (IsSqliteConnectionString(connectionString))
        {
            connectionString = null;
        }

        connectionString ??= "Server=localhost;Database=BoardGameMondays;User Id=sa;Password=ChangeMe!123;Encrypt=False;";

        var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();
        optionsBuilder.UseSqlServer(connectionString);

        return new ApplicationDbContext(optionsBuilder.Options);
    }

    private static bool IsSqliteConnectionString(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return false;
        }

        var cs = connectionString.Trim();
        return cs.Contains(":memory:", StringComparison.OrdinalIgnoreCase)
            || cs.Contains("Filename=", StringComparison.OrdinalIgnoreCase)
            || cs.EndsWith(".db", StringComparison.OrdinalIgnoreCase)
            || cs.EndsWith(".sqlite", StringComparison.OrdinalIgnoreCase)
            || cs.EndsWith(".sqlite3", StringComparison.OrdinalIgnoreCase)
            || cs.Contains(".db;", StringComparison.OrdinalIgnoreCase)
            || cs.Contains(".sqlite;", StringComparison.OrdinalIgnoreCase)
            || cs.Contains(".sqlite3;", StringComparison.OrdinalIgnoreCase);
    }
}
