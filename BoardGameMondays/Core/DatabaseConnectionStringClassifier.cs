using Microsoft.Extensions.Hosting;

namespace BoardGameMondays.Core;

public static class DatabaseConnectionStringClassifier
{
    public static bool IsSqlite(string connectionString)
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

    public static void EnsureNotSqliteInProduction(IHostEnvironment environment, string connectionString)
    {
        if (environment.IsDevelopment())
        {
            return;
        }

        if (IsSqlite(connectionString))
        {
            throw new InvalidOperationException(
                "SQLite connection string detected for a non-development environment. " +
                "Configure ConnectionStrings:DefaultConnection to use Azure SQL / SQL Server.");
        }
    }
}
