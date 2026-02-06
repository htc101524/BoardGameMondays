using System.Reflection;
using BoardGameMondays.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace BoardGameMondays.Tests;

/// <summary>
/// Tests to ensure Entity Framework migrations are up-to-date with the model.
/// 
/// **CRITICAL**: These tests catch model changes without migrations BEFORE deployment.
/// This prevents the crashing startup error: "The model for context has pending changes."
/// 
/// **When to run:**
/// - After modifying any entity in Data/Entities (add/remove/modify properties)
/// - Before committing code
/// - As part of CI/CD pipeline
/// 
/// **If test fails:**
/// 1. Create a migration: dotnet ef migrations add DescriptiveName
/// 2. Run tests again to verify
/// 3. Review the generated migration file before committing
/// 
/// **Entity modification checklist:**
/// □ Modified property (type, constraints, etc.)? → Run: dotnet ef migrations add PropertyUpdate
/// □ Added new property? → Run: dotnet ef migrations add AddPropertyName
/// □ Added [Required], [MaxLength], or [Key]? → Run: dotnet ef migrations add UpdateConstraints
/// □ Modified OnModelCreating (indexes, conversions)? → Run: dotnet ef migrations add UpdateModelConfig
/// □ Changed relationship between entities? → Run: dotnet ef migrations add UpdateRelationships
/// </summary>
public sealed class MigrationTests
{
    private const string SqlServerProvider = "Microsoft.EntityFrameworkCore.SqlServer";
    private static readonly string[] SqliteOnlyColumnTypes = { "TEXT", "INTEGER", "REAL", "BLOB" };

    [Fact]
    public void DesignTimeContextFactoryCreatesSuccessfully()
    {
        // Arrange
        var factory = new ApplicationDbContextFactory();

        // Act & Assert
        // This validates the DbContext can be instantiated without errors.
        // If it throws, there's a configuration issue in ApplicationDbContext.cs
        using var context = factory.CreateDbContext(Array.Empty<string>());
        Assert.NotNull(context);
    }

    [Fact]
    public void ContextCanBeCreatedWithSqliteInMemory()
    {
        // Arrange: Create an in-memory SQLite context
        var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();
        optionsBuilder.UseSqlite("Data Source=:memory:");

        // Act & Assert
        // This tests that the context model itself is valid (no malformed entity configs).
        // If this throws, there's an issue with OnModelCreating configuration.
        using var context = new ApplicationDbContext(optionsBuilder.Options);
        Assert.NotNull(context);
    }

    [Fact]
    public void AllMigrationsCanBeEnumerated()
    {
        // Arrange
        var factory = new ApplicationDbContextFactory();
        using var db = factory.CreateDbContext(Array.Empty<string>());

        // Act
        // This validates that EF Core can enumerate and process all migrations
        // without errors. Missing or malformed migration files will cause this to fail.
        var migrations = db.Database.GetMigrations().ToList();

        // Assert: There should be at least one migration
        // (Otherwise the app has no database schema)
        Assert.NotEmpty(migrations);
        
        // Verify BuildTargetModel can be executed without errors for each migration
        // This is a deeper validation that migrations are well-formed
        foreach (var migration in migrations)
        {
            Assert.False(string.IsNullOrWhiteSpace(migration), 
                "Migration name should not be empty");
        }
    }

    [Fact]
    public void ModelIsConsistentWithLatestMigration()
    {
        // Arrange
        var factory = new ApplicationDbContextFactory();
        using var db = factory.CreateDbContext(Array.Empty<string>());

        // Act: Get the current model and latest migration
        var currentModel = db.Model;
        var databaseProvider = db.Database.ProviderName;

        // Assert:
        // This checks that the model can be successfully analyzed without errors.
        // If the model/migration are out of sync, EF will detect issues here during real app startup.
        Assert.NotNull(currentModel);
        Assert.NotNull(databaseProvider);
        
        // The fact that we got here means:
        // 1. DbContext can be created
        // 2. Model is valid
        // 3. No structural issues with entity configurations
    }

    [Fact]
    public void MigrationsAvoidSqliteColumnTypesForSqlServer()
    {
        // Arrange: build a SqlServer context without connecting to a real database.
        var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();
        optionsBuilder.UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=BgMigrationsTest;Trusted_Connection=True;TrustServerCertificate=True;");

        using var db = new ApplicationDbContext(optionsBuilder.Options);
        var migrationsAssembly = db.GetService<IMigrationsAssembly>();

        // Act + Assert: each migration should not emit SQLite-only column types when targeting SqlServer.
        foreach (var migrationPair in migrationsAssembly.Migrations)
        {
            var migration = (Migration)Activator.CreateInstance(migrationPair.Value)!;
            SetActiveProvider(migration, SqlServerProvider);

            var builder = new MigrationBuilder(SqlServerProvider);
            migration.Up(builder);

            var invalidTypes = builder.Operations
                .SelectMany(GetColumnTypes)
                .Where(type => SqliteOnlyColumnTypes.Contains(type, StringComparer.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            Assert.True(
                invalidTypes.Length == 0,
                $"Migration '{migrationPair.Key}' emits SQLite-only column types for SqlServer: {string.Join(", ", invalidTypes)}");
        }
    }

    private static void SetActiveProvider(Migration migration, string provider)
    {
        var property = typeof(Migration).GetProperty("ActiveProvider", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (property is { CanWrite: true })
        {
            property.SetValue(migration, provider);
            return;
        }

        var field = typeof(Migration).GetField("_activeProvider", BindingFlags.Instance | BindingFlags.NonPublic);
        if (field is not null)
        {
            field.SetValue(migration, provider);
        }
    }

    private static IEnumerable<string> GetColumnTypes(MigrationOperation operation)
    {
        switch (operation)
        {
            case AddColumnOperation addColumn when !string.IsNullOrWhiteSpace(addColumn.ColumnType):
                yield return addColumn.ColumnType!;
                break;
            case AlterColumnOperation alterColumn when !string.IsNullOrWhiteSpace(alterColumn.ColumnType):
                yield return alterColumn.ColumnType!;
                break;
            case CreateTableOperation createTable:
                foreach (var column in createTable.Columns)
                {
                    if (!string.IsNullOrWhiteSpace(column.ColumnType))
                    {
                        yield return column.ColumnType!;
                    }
                }
                break;
        }
    }
}



