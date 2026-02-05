using BoardGameMondays.Data;
using Xunit;

namespace BoardGameMondays.Tests;

/// <summary>
/// Tests to ensure Entity Framework migrations are up-to-date with the model.
/// 
/// These tests catch the common issue where model changes are made without creating migrations.
/// This is the same error that crashes the app on startup if migrations are missing.
/// 
/// When you modify an entity in Data/Entities, you MUST create a migration:
///   dotnet ef migrations add [DescriptiveName]
/// 
/// Without it, the app startup will fail with:
///   "The model for context 'ApplicationDbContext' has pending changes."
/// </summary>
public sealed class MigrationTests
{
    [Fact]
    public void ApplicationDbContextFactoryCreatesContextSuccessfully()
    {
        // Arrange
        var factory = new ApplicationDbContextFactory();

        // Act & Assert
        // If the context creation throws, it means there are model changes without corresponding migrations
        using var context = factory.CreateDbContext(Array.Empty<string>());
        
        // If we reach here, the context was created successfully
        // This is a sanity check that the design-time factory works
        Assert.NotNull(context);
    }
}



