using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace BoardGameMondays.Data;

public sealed class ApplicationDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        // Design-time factory for EF migrations.
        // Always targets SQL Server for migration scaffolding so generated code
        // uses the correct column types (nvarchar, bit, int, …).
        // Set EF_PROVIDER=sqlite to scaffold for SQLite instead.

        var basePath = Directory.GetCurrentDirectory();

        var configuration = new ConfigurationBuilder()
            .SetBasePath(basePath)
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration.GetConnectionString("DefaultConnection");

        // Check if an explicit connection string was passed via --connection argument
        var explicitConnection = Environment.GetEnvironmentVariable("EFCORETOOLSDB");
        if (!string.IsNullOrWhiteSpace(explicitConnection))
        {
            connectionString = explicitConnection;
        }

        var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();

        // Only use SQLite when explicitly requested via environment variable
        var provider = Environment.GetEnvironmentVariable("EF_PROVIDER");
        if (string.Equals(provider, "sqlite", StringComparison.OrdinalIgnoreCase)
            && IsSqliteConnectionString(connectionString))
        {
            optionsBuilder.UseSqlite(connectionString);
        }
        else if (!string.IsNullOrWhiteSpace(connectionString) && !IsSqliteConnectionString(connectionString))
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
            // Default: SQL Server stub connection for migration generation
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
