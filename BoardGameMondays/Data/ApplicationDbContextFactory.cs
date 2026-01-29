using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace BoardGameMondays.Data;

public sealed class ApplicationDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        // Design-time factory for EF migrations.
        // Uses the connection string from configuration (Development or Production).
        // Falls back to a default SQL Server connection for migration generation if SQLite is configured
        // AND no explicit --connection argument was passed via EF tools.

        var basePath = Directory.GetCurrentDirectory();

        var configuration = new ConfigurationBuilder()
            .SetBasePath(basePath)
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration.GetConnectionString("DefaultConnection");

        // Check if an explicit connection string was passed via --connection argument
        // EF tools pass this as an environment variable
        var explicitConnection = Environment.GetEnvironmentVariable("EFCORETOOLSDB");
        if (!string.IsNullOrWhiteSpace(explicitConnection))
        {
            connectionString = explicitConnection;
        }

        var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();

        // Use the appropriate provider based on the connection string format
        if (IsSqliteConnectionString(connectionString))
        {
            optionsBuilder.UseSqlite(connectionString);
        }
        else if (!string.IsNullOrWhiteSpace(connectionString))
        {
            optionsBuilder.UseSqlServer(connectionString, sql =>
            {
                sql.EnableRetryOnFailure(
                    maxRetryCount: 10,
                    maxRetryDelay: TimeSpan.FromSeconds(30),
                    errorNumbersToAdd: null);
            });
        }
        else
        {
            // Fall back to SQL Server for migration generation when no valid connection is available
            connectionString = "Server=localhost;Database=BoardGameMondays;User Id=sa;Password=ChangeMe!123;Encrypt=False;";
            optionsBuilder.UseSqlServer(connectionString);
        }

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
