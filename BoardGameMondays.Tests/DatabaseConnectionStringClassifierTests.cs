using BoardGameMondays.Core;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace BoardGameMondays.Tests;

public class DatabaseConnectionStringClassifierTests
{
    [Theory]
    [InlineData("Data Source=bgm.dev.db")]
    [InlineData("Filename=bgm.dev.db")]
    [InlineData("Data Source=:memory:")]
    [InlineData("Data Source=../data/bgm.sqlite")]
    [InlineData("Data Source=C:\\data\\bgm.sqlite3")]
    [InlineData("Data Source=bgm.db;Cache=Shared")]
    public void IsSqlite_ReturnsTrue_ForSqliteConnectionStrings(string connectionString)
    {
        var result = DatabaseConnectionStringClassifier.IsSqlite(connectionString);

        Assert.True(result);
    }

    [Theory]
    [InlineData("Server=tcp:myserver.database.windows.net,1433;Database=bgm;User ID=bgm;Password=secret;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;")]
    [InlineData("Server=localhost;Database=bgm;Trusted_Connection=True;TrustServerCertificate=True;")]
    [InlineData("Data Source=sqlserver;Initial Catalog=bgm;Integrated Security=True;")]
    public void IsSqlite_ReturnsFalse_ForSqlServerConnectionStrings(string connectionString)
    {
        var result = DatabaseConnectionStringClassifier.IsSqlite(connectionString);

        Assert.False(result);
    }

    [Fact]
    public void EnsureNotSqliteInProduction_Throws_WhenSqliteInProduction()
    {
        var env = new TestHostEnvironment(Environments.Production);
        var connectionString = "Data Source=bgm.dev.db";

        var exception = Assert.Throws<InvalidOperationException>(() =>
            DatabaseConnectionStringClassifier.EnsureNotSqliteInProduction(env, connectionString));

        Assert.Contains("SQLite", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EnsureNotSqliteInProduction_DoesNotThrow_WhenSqlServerInProduction()
    {
        var env = new TestHostEnvironment(Environments.Production);
        var connectionString = "Server=tcp:myserver.database.windows.net,1433;Database=bgm;User ID=bgm;Password=secret;";

        var exception = Record.Exception(() =>
            DatabaseConnectionStringClassifier.EnsureNotSqliteInProduction(env, connectionString));

        Assert.Null(exception);
    }

    [Fact]
    public void EnsureNotSqliteInProduction_DoesNotThrow_WhenDevelopment()
    {
        var env = new TestHostEnvironment(Environments.Development);
        var connectionString = "Data Source=bgm.dev.db";

        var exception = Record.Exception(() =>
            DatabaseConnectionStringClassifier.EnsureNotSqliteInProduction(env, connectionString));

        Assert.Null(exception);
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public TestHostEnvironment(string environmentName)
        {
            EnvironmentName = environmentName;
            ApplicationName = "BoardGameMondays.Tests";
            ContentRootPath = AppContext.BaseDirectory;
        }

        public string EnvironmentName { get; set; }
        public string ApplicationName { get; set; }
        public string ContentRootPath { get; set; }
        public IFileProvider ContentRootFileProvider { get; set; } = default!;
    }
}
