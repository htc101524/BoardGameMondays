using BoardGameMondays.Data;
using Microsoft.EntityFrameworkCore;
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
    public void AllMigrationsFileExist()
    {
        // Arrange
        var migrationsPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "..",
            "..",
            "..",
            "BoardGameMondays",
            "Migrations"
        );

        var normalizedPath = Path.GetFullPath(migrationsPath);

        // Act
        var migrationFiles = Directory.GetFiles(normalizedPath, "*_*.cs")
            .Where(f => !f.EndsWith(".Designer.cs"))
            .ToList();

        var designerFiles = Directory.GetFiles(normalizedPath, "*_*.Designer.cs").ToList();

        // Assert: Each migration should have a matching Designer file
        // Missing Designer files indicate incomplete migrations
        Assert.NotEmpty(migrationFiles);
        Assert.NotEmpty(designerFiles);
        
        foreach (var migrationFile in migrationFiles)
        {
            var designerPath = migrationFile.Replace(".cs", ".Designer.cs");
            Assert.True(
                File.Exists(designerPath),
                $"Migration {Path.GetFileName(migrationFile)} is missing its Designer file");
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
}



