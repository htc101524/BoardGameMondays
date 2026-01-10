using BoardGameMondays.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;
using BoardGameMondays.Core;
using BoardGameMondays.Data;
using BoardGameMondays.Data.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.DataProtection;
using System.IO;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http.Features;
using System.Security.Claims;
using System.Data;
using System.Threading.RateLimiting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

if (builder.Environment.IsDevelopment())
{
    builder.Services.Configure<CircuitOptions>(options => options.DetailedErrors = true);
}

// Database + Identity.
// Dev default is SQLite; production should use Azure SQL (SQL Server provider).
void ConfigureDbContextOptions(DbContextOptionsBuilder options)
{
    var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
    if (string.IsNullOrWhiteSpace(connectionString))
    {
        throw new InvalidOperationException("Missing connection string 'DefaultConnection'.");
    }

    // Treat the connection string as SQLite only when it clearly points at a local SQLite file.
    // SQL Server connection strings often also contain "Data Source=", so don't use that as a signal.
    var cs = connectionString.Trim();
    var isSqlite = cs.Contains(":memory:", StringComparison.OrdinalIgnoreCase)
        || cs.Contains("Filename=", StringComparison.OrdinalIgnoreCase)
        || cs.EndsWith(".db", StringComparison.OrdinalIgnoreCase)
        || cs.EndsWith(".sqlite", StringComparison.OrdinalIgnoreCase)
        || cs.EndsWith(".sqlite3", StringComparison.OrdinalIgnoreCase)
        || cs.Contains(".db;", StringComparison.OrdinalIgnoreCase)
        || cs.Contains(".sqlite;", StringComparison.OrdinalIgnoreCase)
        || cs.Contains(".sqlite3;", StringComparison.OrdinalIgnoreCase);

    if (isSqlite)
    {
        options.UseSqlite(connectionString);
    }
    else
    {
        // Azure SQL can occasionally return transient errors (e.g., serverless wake-ups, failovers).
        // Enable built-in retry logic so startup migrations don't crash the app on a brief blip.
        options.UseSqlServer(connectionString, sql =>
        {
            sql.EnableRetryOnFailure(
                maxRetryCount: 10,
                maxRetryDelay: TimeSpan.FromSeconds(30),
                errorNumbersToAdd: null);
        });
    }
}

builder.Services.AddDbContext<ApplicationDbContext>(ConfigureDbContextOptions);
// IMPORTANT: We also register a factory so UI services can create short-lived DbContext instances
// (avoids Blazor Server concurrent-use issues). Since AddDbContext registers DbContextOptions as scoped,
// the factory must also be scoped; otherwise DI will reject a singleton factory consuming scoped options.
builder.Services.AddDbContextFactory<ApplicationDbContext>(
    ConfigureDbContextOptions,
    lifetime: Microsoft.Extensions.DependencyInjection.ServiceLifetime.Scoped);

builder.Services.AddMemoryCache(options =>
{
    // Keeps the pwned-password cache bounded.
    options.SizeLimit = 10_000;
});

builder.Services.AddHttpClient("pwned-passwords", client =>
{
    client.BaseAddress = new Uri("https://api.pwnedpasswords.com/");
    client.Timeout = TimeSpan.FromSeconds(3);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("BoardGameMondays/1.0");
});

// Asset storage (uploads). Default is local filesystem; can be switched to Azure Blob Storage via config.
builder.Services.Configure<StorageOptions>(builder.Configuration.GetSection("Storage"));
builder.Services.AddSingleton<IAssetStorage>(sp =>
{
    var options = sp.GetRequiredService<IOptions<StorageOptions>>().Value;
    var provider = (options.Provider ?? "Local").Trim();

    if (provider.Equals("AzureBlob", StringComparison.OrdinalIgnoreCase)
        || provider.Equals("Azure", StringComparison.OrdinalIgnoreCase)
        || provider.Equals("Blob", StringComparison.OrdinalIgnoreCase))
    {
        return new AzureBlobAssetStorage(sp.GetRequiredService<IOptions<StorageOptions>>());
    }

    return new LocalAssetStorage(
        sp.GetRequiredService<IWebHostEnvironment>(),
        sp.GetRequiredService<IOptions<StorageOptions>>());
});

builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
    {
        if (builder.Environment.IsDevelopment())
        {
            // Dev-friendly defaults.
            options.Password.RequiredLength = 6;
            options.Password.RequireDigit = false;
            options.Password.RequireLowercase = false;
            options.Password.RequireUppercase = false;
            options.Password.RequireNonAlphanumeric = false;
            options.Lockout.AllowedForNewUsers = false;
        }
        else
        {
            // Production defaults.
            options.Password.RequiredLength = 12;
            options.Password.RequireDigit = true;
            options.Password.RequireLowercase = true;
            options.Password.RequireUppercase = true;
            options.Password.RequireNonAlphanumeric = true;

            options.Lockout.AllowedForNewUsers = true;
            options.Lockout.MaxFailedAccessAttempts = 5;
            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        }

        options.User.RequireUniqueEmail = false;
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

// Reject breached/common passwords (HIBP k-anonymity).
builder.Services.AddScoped<IPasswordValidator<ApplicationUser>, PwnedPasswordValidator>();

// Explicitly set password hashing strength (PBKDF2). Tune in production if needed.
builder.Services.Configure<PasswordHasherOptions>(options =>
{
    options.CompatibilityMode = PasswordHasherCompatibilityMode.IdentityV3;

    // Keep dev reasonably quick, prod stronger.
    options.IterationCount = builder.Environment.IsDevelopment() ? 50_000 : 210_000;
});

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/login";
    options.AccessDeniedPath = "/login";
    options.Cookie.Name = "bgm.auth";
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.SlidingExpiration = true;
});

// Enforce Admin access from configuration (Azure env vars) rather than persisted DB roles.
builder.Services.AddTransient<Microsoft.AspNetCore.Authentication.IClaimsTransformation, AdminRoleClaimsTransformation>();

builder.Services.AddAuthorization();

// Guardrails for multipart/form-data (avatars, uploads).
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 10 * 1024 * 1024; // 10MB
});

// Basic rate limiting. This is not a substitute for a CDN/WAF, but it helps protect form endpoints.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy("account", httpContext =>
    {
        var ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: ip,
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 20,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst
            });
    });
});
builder.Services.AddHttpContextAccessor();
// Use the built-in Blazor Server auth state plumbing. This ensures the circuit principal is
// populated from the ASP.NET Core authentication middleware (cookies/Identity).
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<BoardGameMondays.Core.BgmMemberService>();
builder.Services.AddScoped<BoardGameMondays.Core.BgmMemberDirectoryService>();
builder.Services.AddScoped<BoardGameMondays.Core.BoardGameService>();
builder.Services.AddScoped<BoardGameMondays.Core.TicketService>();
builder.Services.AddScoped<BoardGameMondays.Core.AgreementService>();
builder.Services.AddScoped<BoardGameMondays.Core.GameNightService>();
builder.Services.AddScoped<BoardGameMondays.Core.BgmCoinService>();
builder.Services.AddScoped<BoardGameMondays.Core.BettingService>();
builder.Services.AddScoped<BoardGameMondays.Core.BlogService>();

// Persist Data Protection keys so auth cookies remain valid across instances/restarts on Azure App Service.
// Preferred path resolution order:
// 1. Configuration: DataProtection:Path
// 2. Environment variable: DATA_PROTECTION_PATH
// 3. Azure App Service home directory: %HOME%/DataProtection-Keys (D:\home on Windows-hosted App Service)
{
    var dpPath = builder.Configuration["DataProtection:Path"]
        ?? Environment.GetEnvironmentVariable("DATA_PROTECTION_PATH");

    if (string.IsNullOrEmpty(dpPath))
    {
        var home = Environment.GetEnvironmentVariable("HOME") ?? Environment.GetEnvironmentVariable("HOMEDRIVE") + Environment.GetEnvironmentVariable("HOMEPATH");
        if (!string.IsNullOrWhiteSpace(home))
        {
            dpPath = Path.Combine(home, "DataProtection-Keys");
        }
    }

    if (!string.IsNullOrWhiteSpace(dpPath))
    {
        try
        {
            Directory.CreateDirectory(dpPath);
            builder.Services.AddDataProtection()
                .PersistKeysToFileSystem(new DirectoryInfo(dpPath))
                .SetApplicationName("BoardGameMondays");
        }
        catch
        {
            // If path creation or persistence fails, fall back to default in-memory keys.
            builder.Services.AddDataProtection()
                .SetApplicationName("BoardGameMondays");
        }
    }
    else
    {
        builder.Services.AddDataProtection()
            .SetApplicationName("BoardGameMondays");
    }
}

var app = builder.Build();

// Initialize the database.
// Wrap this block so Azure App Service startup failures always emit an actionable error to logs.
try
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

    if (db.Database.IsSqlite())
    {
        // SQLite dev workflow (no EF migrations yet): keep the existing schema upgrader.
        await db.Database.EnsureCreatedAsync();
        await EnsureSqliteSchemaUpToDateAsync(db);

        if (app.Environment.IsDevelopment())
        {
            await DataSeeder.SeedAsync(db);

            // Dev convenience: create a non-admin user for quickly checking the non-admin UX.
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            await SeedDevNonAdminUserAsync(userManager, db);
        }
    }
    else
    {
        // Production workflow (Azure SQL): use EF migrations.
        await db.Database.MigrateAsync();

        // Defensive guard: if a previous deploy ended up with migrations history out of sync
        // (migration marked applied but columns missing), patch the schema so runtime queries don't crash.
        await EnsureSqlServerTeamColumnsAsync(db, app.Logger);
        await EnsureSqlServerTicketColumnsAsync(db, app.Logger);
    }

    // Ensure identity tables exist before assigning roles.
    await EnsureAdminRoleAssignmentsAsync(scope.ServiceProvider);
}
catch (Exception ex)
{
    app.Logger.LogCritical(ex, "Fatal error during application startup.");
    throw;
}

static async Task EnsureSqlServerTeamColumnsAsync(ApplicationDbContext db, ILogger logger)
{
    if (!db.Database.IsSqlServer())
    {
        return;
    }

    const string sql = @"
IF COL_LENGTH(N'dbo.GameNightGames', N'WinnerTeamName') IS NULL
BEGIN
    ALTER TABLE [dbo].[GameNightGames] ADD [WinnerTeamName] nvarchar(64) NULL;
END;

IF COL_LENGTH(N'dbo.GameNightGamePlayers', N'TeamName') IS NULL
BEGIN
    ALTER TABLE [dbo].[GameNightGamePlayers] ADD [TeamName] nvarchar(64) NULL;
END;
";

    try
    {
        var affected = await db.Database.ExecuteSqlRawAsync(sql);
        if (affected != 0)
        {
            logger.LogWarning("Patched missing team columns in SQL Server schema.");
        }
    }
    catch (Exception ex)
    {
        // If this fails, keep the exception visible; missing columns will crash later anyway.
        logger.LogCritical(ex, "Failed to ensure team columns exist in SQL Server schema.");
        throw;
    }
}

static async Task EnsureSqlServerTicketColumnsAsync(ApplicationDbContext db, ILogger logger)
{
    if (!db.Database.IsSqlServer())
    {
        return;
    }

    const string sql = @"
IF COL_LENGTH(N'dbo.Tickets', N'DoneOn') IS NULL
BEGIN
    ALTER TABLE [dbo].[Tickets] ADD [DoneOn] bigint NULL;
END;
";

    try
    {
        var affected = await db.Database.ExecuteSqlRawAsync(sql);
        if (affected != 0)
        {
            logger.LogWarning("Patched missing ticket columns in SQL Server schema.");
        }
    }
    catch (Exception ex)
    {
        logger.LogCritical(ex, "Failed to ensure ticket columns exist in SQL Server schema.");
        throw;
    }
}

static async Task EnsureAdminRoleAssignmentsAsync(IServiceProvider services)
{
    var config = services.GetRequiredService<IConfiguration>();
    var rawUserNames = config.GetSection("Security:Admins:UserNames").Get<string[]>() ?? [];
    var userNames = rawUserNames
        .Select(x => x?.Trim())
        .Where(x => !string.IsNullOrWhiteSpace(x))
        .Select(x => x!)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    if (userNames.Length == 0)
    {
        return;
    }

    var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
    var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();

    if (!await roleManager.RoleExistsAsync("Admin"))
    {
        var created = await roleManager.CreateAsync(new IdentityRole("Admin"));
        if (!created.Succeeded)
        {
            return;
        }
    }

    foreach (var userName in userNames)
    {
        var user = await userManager.FindByNameAsync(userName);
        if (user is null)
        {
            continue;
        }

        if (!await userManager.IsInRoleAsync(user, "Admin"))
        {
            await userManager.AddToRoleAsync(user, "Admin");
        }
    }
}

static async Task SeedDevNonAdminUserAsync(UserManager<ApplicationUser> userManager, ApplicationDbContext db)
{
    const string userName = "nonadmin";
    const string password = "test123";
    const string displayName = "Non-admin";

    var existing = await userManager.FindByNameAsync(userName);
    if (existing is null)
    {
        existing = new ApplicationUser { UserName = userName };
        var result = await userManager.CreateAsync(existing, password);
        if (!result.Succeeded)
        {
            // If seeding fails (e.g., password policy changes), don't block app startup.
            return;
        }
    }

    var claims = await userManager.GetClaimsAsync(existing);

    if (!claims.Any(c => c.Type == BgmClaimTypes.DisplayName))
    {
        await userManager.AddClaimAsync(existing, new Claim(BgmClaimTypes.DisplayName, displayName));
    }

    // If this observer was accidentally created as a BGM member previously, mark them as not a member
    // so they never show up in People.
    var existingMember = await db.Members.FirstOrDefaultAsync(
        m => m.Name.ToLower() == displayName.ToLower());
    if (existingMember is not null && existingMember.IsBgmMember)
    {
        existingMember.IsBgmMember = false;
        await db.SaveChangesAsync();
    }
}

static async Task EnsureSqliteSchemaUpToDateAsync(ApplicationDbContext db)
{
    if (!db.Database.IsSqlite())
    {
        return;
    }

    var connection = db.Database.GetDbConnection();
    if (connection.State != ConnectionState.Open)
    {
        await connection.OpenAsync();
    }

    try
    {
        // AspNetUsers.BgmCoins
        var hasBgmCoins = false;
        await using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "PRAGMA table_info('AspNetUsers');";
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var name = reader.GetString(1);
                if (string.Equals(name, "BgmCoins", StringComparison.OrdinalIgnoreCase))
                {
                    hasBgmCoins = true;
                    break;
                }
            }
        }

        if (!hasBgmCoins)
        {
            await using var alter = connection.CreateCommand();
            alter.CommandText = "ALTER TABLE AspNetUsers ADD COLUMN BgmCoins INTEGER NOT NULL DEFAULT 100;";
            await alter.ExecuteNonQueryAsync();
        }

        // Members.IsBgmMember
        var hasIsBgmMember = false;
        await using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "PRAGMA table_info('Members');";
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var name = reader.GetString(1);
                if (string.Equals(name, "IsBgmMember", StringComparison.OrdinalIgnoreCase))
                {
                    hasIsBgmMember = true;
                    break;
                }
            }
        }

        if (!hasIsBgmMember)
        {
            await using var alter = connection.CreateCommand();
            alter.CommandText = "ALTER TABLE Members ADD COLUMN IsBgmMember INTEGER NOT NULL DEFAULT 1;";
            await alter.ExecuteNonQueryAsync();
        }

        // Reviews.TimesPlayed
        var hasTimesPlayed = false;
        await using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "PRAGMA table_info('Reviews');";
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var name = reader.GetString(1);
                if (string.Equals(name, "TimesPlayed", StringComparison.OrdinalIgnoreCase))
                {
                    hasTimesPlayed = true;
                    break;
                }
            }
        }

        if (!hasTimesPlayed)
        {
            await using var alter = connection.CreateCommand();
            alter.CommandText = "ALTER TABLE Reviews ADD COLUMN TimesPlayed INTEGER NOT NULL DEFAULT 0;";
            await alter.ExecuteNonQueryAsync();
        }

        // Games stats
        var hasMinPlayers = false;
        var hasMaxPlayers = false;
        var hasRuntimeMinutes = false;
        var hasFirstPlayRuntimeMinutes = false;
        var hasComplexity = false;
        var hasBggScore = false;
        var hasBggUrl = false;
        await using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "PRAGMA table_info('Games');";
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var name = reader.GetString(1);
                if (string.Equals(name, "MinPlayers", StringComparison.OrdinalIgnoreCase)) hasMinPlayers = true;
                if (string.Equals(name, "MaxPlayers", StringComparison.OrdinalIgnoreCase)) hasMaxPlayers = true;
                if (string.Equals(name, "RuntimeMinutes", StringComparison.OrdinalIgnoreCase)) hasRuntimeMinutes = true;
                if (string.Equals(name, "FirstPlayRuntimeMinutes", StringComparison.OrdinalIgnoreCase)) hasFirstPlayRuntimeMinutes = true;
                if (string.Equals(name, "Complexity", StringComparison.OrdinalIgnoreCase)) hasComplexity = true;
                if (string.Equals(name, "BoardGameGeekScore", StringComparison.OrdinalIgnoreCase)) hasBggScore = true;
                if (string.Equals(name, "BoardGameGeekUrl", StringComparison.OrdinalIgnoreCase)) hasBggUrl = true;
            }
        }

        if (!hasMinPlayers)
        {
            await using var alter = connection.CreateCommand();
            alter.CommandText = "ALTER TABLE Games ADD COLUMN MinPlayers INTEGER NULL;";
            await alter.ExecuteNonQueryAsync();
        }

        if (!hasMaxPlayers)
        {
            await using var alter = connection.CreateCommand();
            alter.CommandText = "ALTER TABLE Games ADD COLUMN MaxPlayers INTEGER NULL;";
            await alter.ExecuteNonQueryAsync();
        }

        if (!hasRuntimeMinutes)
        {
            await using var alter = connection.CreateCommand();
            alter.CommandText = "ALTER TABLE Games ADD COLUMN RuntimeMinutes INTEGER NULL;";
            await alter.ExecuteNonQueryAsync();
        }

        if (!hasFirstPlayRuntimeMinutes)
        {
            await using var alter = connection.CreateCommand();
            alter.CommandText = "ALTER TABLE Games ADD COLUMN FirstPlayRuntimeMinutes INTEGER NULL;";
            await alter.ExecuteNonQueryAsync();
        }

        if (!hasComplexity)
        {
            await using var alter = connection.CreateCommand();
            alter.CommandText = "ALTER TABLE Games ADD COLUMN Complexity REAL NULL;";
            await alter.ExecuteNonQueryAsync();
        }

        if (!hasBggScore)
        {
            await using var alter = connection.CreateCommand();
            alter.CommandText = "ALTER TABLE Games ADD COLUMN BoardGameGeekScore REAL NULL;";
            await alter.ExecuteNonQueryAsync();
        }

        if (!hasBggUrl)
        {
            await using var alter = connection.CreateCommand();
            alter.CommandText = "ALTER TABLE Games ADD COLUMN BoardGameGeekUrl TEXT NULL;";
            await alter.ExecuteNonQueryAsync();
        }

        // Admin tickets + priorities.
        await using (var createTickets = connection.CreateCommand())
        {
            createTickets.CommandText = @"
CREATE TABLE IF NOT EXISTS Tickets (
    Id TEXT NOT NULL PRIMARY KEY,
    Type INTEGER NOT NULL,
    Title TEXT NOT NULL,
    Description TEXT NULL,
    CreatedOn INTEGER NOT NULL,
    DoneOn INTEGER NULL,
    CreatedByUserId TEXT NULL
);
";
            await createTickets.ExecuteNonQueryAsync();
        }

        // Tickets.DoneOn
        var hasTicketDoneOn = false;
        await using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "PRAGMA table_info('Tickets');";
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var name = reader.GetString(1);
                if (string.Equals(name, "DoneOn", StringComparison.OrdinalIgnoreCase))
                {
                    hasTicketDoneOn = true;
                    break;
                }
            }
        }

        if (!hasTicketDoneOn)
        {
            await using var alter = connection.CreateCommand();
            alter.CommandText = "ALTER TABLE Tickets ADD COLUMN DoneOn INTEGER NULL;";
            await alter.ExecuteNonQueryAsync();
        }

        await using (var createTicketPriorities = connection.CreateCommand())
        {
            createTicketPriorities.CommandText = @"
CREATE TABLE IF NOT EXISTS TicketPriorities (
    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    TicketId TEXT NOT NULL,
    AdminUserId TEXT NOT NULL,
    Type INTEGER NOT NULL,
    Rank INTEGER NOT NULL,
    FOREIGN KEY (TicketId) REFERENCES Tickets(Id) ON DELETE CASCADE
);
";
            await createTicketPriorities.ExecuteNonQueryAsync();
        }

        await using (var createIndexes = connection.CreateCommand())
        {
            createIndexes.CommandText = @"
CREATE INDEX IF NOT EXISTS IX_Tickets_Type ON Tickets(Type);
CREATE UNIQUE INDEX IF NOT EXISTS IX_TicketPriorities_Admin_Type_Rank ON TicketPriorities(AdminUserId, Type, Rank);
CREATE UNIQUE INDEX IF NOT EXISTS IX_TicketPriorities_Admin_Ticket ON TicketPriorities(AdminUserId, TicketId);
";
            await createIndexes.ExecuteNonQueryAsync();
        }

        // Review agreements
        await using (var createReviewAgreements = connection.CreateCommand())
        {
            createReviewAgreements.CommandText = @"
CREATE TABLE IF NOT EXISTS ReviewAgreements (
    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    ReviewId TEXT NOT NULL,
    UserId TEXT NOT NULL,
    Score INTEGER NOT NULL,
    CreatedOn INTEGER NOT NULL,
    FOREIGN KEY (ReviewId) REFERENCES Reviews(Id) ON DELETE CASCADE
);
";
            await createReviewAgreements.ExecuteNonQueryAsync();
        }

        await using (var createReviewAgreementIndexes = connection.CreateCommand())
        {
            createReviewAgreementIndexes.CommandText = @"
CREATE UNIQUE INDEX IF NOT EXISTS IX_ReviewAgreements_User_Review ON ReviewAgreements(UserId, ReviewId);
CREATE INDEX IF NOT EXISTS IX_ReviewAgreements_UserId ON ReviewAgreements(UserId);
";
            await createReviewAgreementIndexes.ExecuteNonQueryAsync();
        }

        // Game nights
        await using (var createGameNights = connection.CreateCommand())
        {
            createGameNights.CommandText = @"
CREATE TABLE IF NOT EXISTS GameNights (
    Id TEXT NOT NULL PRIMARY KEY,
    DateKey INTEGER NOT NULL,
    Recap TEXT NULL
);
";
            await createGameNights.ExecuteNonQueryAsync();
        }

        var hasGameNightRecap = false;
        await using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "PRAGMA table_info('GameNights');";
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var name = reader.GetString(1);
                if (string.Equals(name, "Recap", StringComparison.OrdinalIgnoreCase))
                {
                    hasGameNightRecap = true;
                    break;
                }
            }
        }

        if (!hasGameNightRecap)
        {
            await using var alter = connection.CreateCommand();
            alter.CommandText = "ALTER TABLE GameNights ADD COLUMN Recap TEXT NULL;";
            await alter.ExecuteNonQueryAsync();
        }

        await using (var createGameNightAttendees = connection.CreateCommand())
        {
            createGameNightAttendees.CommandText = @"
CREATE TABLE IF NOT EXISTS GameNightAttendees (
    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    GameNightId TEXT NOT NULL,
    MemberId TEXT NOT NULL,
    CreatedOn INTEGER NOT NULL,
    FOREIGN KEY (GameNightId) REFERENCES GameNights(Id) ON DELETE CASCADE,
    FOREIGN KEY (MemberId) REFERENCES Members(Id) ON DELETE CASCADE
);
";
            await createGameNightAttendees.ExecuteNonQueryAsync();
        }

        await using (var createGameNightGames = connection.CreateCommand())
        {
            createGameNightGames.CommandText = @"
CREATE TABLE IF NOT EXISTS GameNightGames (
    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    GameNightId TEXT NOT NULL,
    GameId TEXT NOT NULL,
    IsPlayed INTEGER NOT NULL,
    IsConfirmed INTEGER NOT NULL DEFAULT 0,
    WinnerMemberId TEXT NULL,
    CreatedOn INTEGER NOT NULL,
    FOREIGN KEY (GameNightId) REFERENCES GameNights(Id) ON DELETE CASCADE,
    FOREIGN KEY (GameId) REFERENCES Games(Id) ON DELETE CASCADE
);
";
            await createGameNightGames.ExecuteNonQueryAsync();
        }

        var hasIsConfirmed = false;
        var hasWinnerMemberId = false;
        var hasWinnerTeamName = false;
        await using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "PRAGMA table_info('GameNightGames');";
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var name = reader.GetString(1);
                if (string.Equals(name, "IsConfirmed", StringComparison.OrdinalIgnoreCase))
                {
                    hasIsConfirmed = true;
                }

                if (string.Equals(name, "WinnerMemberId", StringComparison.OrdinalIgnoreCase))
                {
                    hasWinnerMemberId = true;
                }

                if (string.Equals(name, "WinnerTeamName", StringComparison.OrdinalIgnoreCase))
                {
                    hasWinnerTeamName = true;
                }
            }
        }

        if (!hasIsConfirmed)
        {
            await using var alter = connection.CreateCommand();
            alter.CommandText = "ALTER TABLE GameNightGames ADD COLUMN IsConfirmed INTEGER NOT NULL DEFAULT 0;";
            await alter.ExecuteNonQueryAsync();
        }

        if (!hasWinnerMemberId)
        {
            await using var alter = connection.CreateCommand();
            alter.CommandText = "ALTER TABLE GameNightGames ADD COLUMN WinnerMemberId TEXT NULL;";
            await alter.ExecuteNonQueryAsync();
        }

        if (!hasWinnerTeamName)
        {
            await using var alter = connection.CreateCommand();
            alter.CommandText = "ALTER TABLE GameNightGames ADD COLUMN WinnerTeamName TEXT NULL;";
            await alter.ExecuteNonQueryAsync();
        }

        await using (var createGameNightGamePlayers = connection.CreateCommand())
        {
            createGameNightGamePlayers.CommandText = @"
CREATE TABLE IF NOT EXISTS GameNightGamePlayers (
    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    GameNightGameId INTEGER NOT NULL,
    MemberId TEXT NOT NULL,
    CreatedOn INTEGER NOT NULL,
    TeamName TEXT NULL,
    FOREIGN KEY (GameNightGameId) REFERENCES GameNightGames(Id) ON DELETE CASCADE,
    FOREIGN KEY (MemberId) REFERENCES Members(Id) ON DELETE CASCADE
);
";
            await createGameNightGamePlayers.ExecuteNonQueryAsync();
        }

        var hasPlayerTeamName = false;
        await using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "PRAGMA table_info('GameNightGamePlayers');";
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var name = reader.GetString(1);
                if (string.Equals(name, "TeamName", StringComparison.OrdinalIgnoreCase))
                {
                    hasPlayerTeamName = true;
                    break;
                }
            }
        }

        if (!hasPlayerTeamName)
        {
            await using var alter = connection.CreateCommand();
            alter.CommandText = "ALTER TABLE GameNightGamePlayers ADD COLUMN TeamName TEXT NULL;";
            await alter.ExecuteNonQueryAsync();
        }

        // Per-game team colours table
        await using (var createGameNightGameTeams = connection.CreateCommand())
        {
            createGameNightGameTeams.CommandText = @"
CREATE TABLE IF NOT EXISTS GameNightGameTeams (
    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    GameNightGameId INTEGER NOT NULL,
    TeamName TEXT NOT NULL,
    ColorHex TEXT NULL,
    FOREIGN KEY (GameNightGameId) REFERENCES GameNightGames(Id) ON DELETE CASCADE
);
";
            await createGameNightGameTeams.ExecuteNonQueryAsync();
        }

        await using (var createGameNightGameTeamsIndexes = connection.CreateCommand())
        {
            createGameNightGameTeamsIndexes.CommandText = @"
CREATE UNIQUE INDEX IF NOT EXISTS IX_GameNightGameTeams_NightGame_Team ON GameNightGameTeams(GameNightGameId, TeamName);
";
            await createGameNightGameTeamsIndexes.ExecuteNonQueryAsync();
        }

        // Betting odds + bets
        await using (var createOdds = connection.CreateCommand())
        {
            createOdds.CommandText = @"
CREATE TABLE IF NOT EXISTS GameNightGameOdds (
    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    GameNightGameId INTEGER NOT NULL,
    MemberId TEXT NOT NULL,
    OddsTimes100 INTEGER NOT NULL,
    CreatedOn INTEGER NOT NULL,
    FOREIGN KEY (GameNightGameId) REFERENCES GameNightGames(Id) ON DELETE CASCADE,
    FOREIGN KEY (MemberId) REFERENCES Members(Id) ON DELETE CASCADE
);
";
            await createOdds.ExecuteNonQueryAsync();
        }

        await using (var createBets = connection.CreateCommand())
        {
            createBets.CommandText = @"
CREATE TABLE IF NOT EXISTS GameNightGameBets (
    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    GameNightGameId INTEGER NOT NULL,
    UserId TEXT NOT NULL,
    PredictedWinnerMemberId TEXT NOT NULL,
    Amount INTEGER NOT NULL,
    OddsTimes100 INTEGER NOT NULL,
    IsResolved INTEGER NOT NULL DEFAULT 0,
    Payout INTEGER NOT NULL DEFAULT 0,
    ResolvedOn INTEGER NULL,
    CreatedOn INTEGER NOT NULL,
    FOREIGN KEY (GameNightGameId) REFERENCES GameNightGames(Id) ON DELETE CASCADE,
    FOREIGN KEY (PredictedWinnerMemberId) REFERENCES Members(Id) ON DELETE RESTRICT
);
";
            await createBets.ExecuteNonQueryAsync();
        }

        // If the table existed before settlement columns were added, patch it in.
        var hasBetIsResolved = false;
        var hasBetPayout = false;
        var hasBetResolvedOn = false;
        await using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "PRAGMA table_info('GameNightGameBets');";
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var name = reader.GetString(1);
                if (string.Equals(name, "IsResolved", StringComparison.OrdinalIgnoreCase)) hasBetIsResolved = true;
                if (string.Equals(name, "Payout", StringComparison.OrdinalIgnoreCase)) hasBetPayout = true;
                if (string.Equals(name, "ResolvedOn", StringComparison.OrdinalIgnoreCase)) hasBetResolvedOn = true;
            }
        }

        if (!hasBetIsResolved)
        {
            await using var alter = connection.CreateCommand();
            alter.CommandText = "ALTER TABLE GameNightGameBets ADD COLUMN IsResolved INTEGER NOT NULL DEFAULT 0;";
            await alter.ExecuteNonQueryAsync();
        }

        if (!hasBetPayout)
        {
            await using var alter = connection.CreateCommand();
            alter.CommandText = "ALTER TABLE GameNightGameBets ADD COLUMN Payout INTEGER NOT NULL DEFAULT 0;";
            await alter.ExecuteNonQueryAsync();
        }

        if (!hasBetResolvedOn)
        {
            await using var alter = connection.CreateCommand();
            alter.CommandText = "ALTER TABLE GameNightGameBets ADD COLUMN ResolvedOn INTEGER NULL;";
            await alter.ExecuteNonQueryAsync();
        }

        await using (var createGameNightIndexes = connection.CreateCommand())
        {
            createGameNightIndexes.CommandText = @"
CREATE UNIQUE INDEX IF NOT EXISTS IX_GameNights_DateKey ON GameNights(DateKey);
CREATE UNIQUE INDEX IF NOT EXISTS IX_GameNightAttendees_Night_Member ON GameNightAttendees(GameNightId, MemberId);
CREATE UNIQUE INDEX IF NOT EXISTS IX_GameNightGames_Night_Game ON GameNightGames(GameNightId, GameId);
CREATE UNIQUE INDEX IF NOT EXISTS IX_GameNightGamePlayers_NightGame_Member ON GameNightGamePlayers(GameNightGameId, MemberId);
CREATE UNIQUE INDEX IF NOT EXISTS IX_GameNightGameOdds_NightGame_Member ON GameNightGameOdds(GameNightGameId, MemberId);
CREATE UNIQUE INDEX IF NOT EXISTS IX_GameNightGameBets_NightGame_User ON GameNightGameBets(GameNightGameId, UserId);
CREATE INDEX IF NOT EXISTS IX_GameNightAttendees_MemberId ON GameNightAttendees(MemberId);
CREATE INDEX IF NOT EXISTS IX_GameNightGames_GameId ON GameNightGames(GameId);
CREATE INDEX IF NOT EXISTS IX_GameNightGamePlayers_MemberId ON GameNightGamePlayers(MemberId);
CREATE INDEX IF NOT EXISTS IX_GameNightGameOdds_MemberId ON GameNightGameOdds(MemberId);
CREATE INDEX IF NOT EXISTS IX_GameNightGameBets_UserId ON GameNightGameBets(UserId);
";
            await createGameNightIndexes.ExecuteNonQueryAsync();
        }

        // Blog posts
        await using (var createBlogPosts = connection.CreateCommand())
        {
            createBlogPosts.CommandText = @"
CREATE TABLE IF NOT EXISTS BlogPosts (
    Id TEXT NOT NULL PRIMARY KEY,
    Title TEXT NOT NULL,
    Slug TEXT NOT NULL,
    Body TEXT NOT NULL,
    CreatedOn INTEGER NOT NULL,
    CreatedByUserId TEXT NULL
);
";
            await createBlogPosts.ExecuteNonQueryAsync();
        }

        await using (var createBlogIndexes = connection.CreateCommand())
        {
            createBlogIndexes.CommandText = @"
CREATE UNIQUE INDEX IF NOT EXISTS IX_BlogPosts_Slug ON BlogPosts(Slug);
CREATE INDEX IF NOT EXISTS IX_BlogPosts_CreatedOn ON BlogPosts(CreatedOn);
";
            await createBlogIndexes.ExecuteNonQueryAsync();
        }
    }
    finally
    {
        await connection.CloseAsync();
    }
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

// Friendly handling for multipart upload limits (e.g., very large avatar uploads).
// These exceptions can occur before the endpoint delegate is invoked.
app.Use(async (context, next) =>
{
    try
    {
        await next();
    }
    catch (Exception ex) when (IsMultipartBodyLengthLimitExceeded(ex))
    {
        if (context.Response.HasStarted)
        {
            throw;
        }

        if (HttpMethods.IsPost(context.Request.Method)
            && string.Equals(context.Request.Path, "/account/avatar", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.Clear();
            context.Response.Redirect($"/?avatarError={Uri.EscapeDataString("Image must be 10MB or smaller.")}#people");
            return;
        }

        throw;
    }
});

// If running on Azure App Service with Local storage, persist uploads under %HOME% (not wwwroot)
// and serve them explicitly. This prevents uploaded images from being wiped during deployments.
{
    var storage = app.Services.GetRequiredService<IOptions<StorageOptions>>().Value;
    var provider = (storage.Provider ?? "Local").Trim();

    if (provider.Equals("Local", StringComparison.OrdinalIgnoreCase))
    {
        var localRoot = storage.Local.RootPath?.Trim();

        if (string.IsNullOrWhiteSpace(localRoot) && !app.Environment.IsDevelopment())
        {
            var home = Environment.GetEnvironmentVariable("HOME");
            if (!string.IsNullOrWhiteSpace(home))
            {
                localRoot = Path.Combine(home, "bgm-assets");
            }
        }

        if (!string.IsNullOrWhiteSpace(localRoot))
        {
            var fullLocalRoot = Path.GetFullPath(localRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var fullWebRoot = Path.GetFullPath(app.Environment.WebRootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            if (!string.Equals(fullLocalRoot, fullWebRoot, StringComparison.OrdinalIgnoreCase))
            {
                var uploadsRoot = Path.Combine(fullLocalRoot, "uploads");
                var imagesRoot = Path.Combine(fullLocalRoot, "images");

                Directory.CreateDirectory(Path.Combine(uploadsRoot, "avatars"));
                Directory.CreateDirectory(Path.Combine(imagesRoot, "games"));

                app.UseStaticFiles(new StaticFileOptions
                {
                    FileProvider = new PhysicalFileProvider(uploadsRoot),
                    RequestPath = "/uploads"
                });

                app.UseStaticFiles(new StaticFileOptions
                {
                    FileProvider = new PhysicalFileProvider(imagesRoot),
                    RequestPath = "/images"
                });
            }
        }
    }
}

// Required for serving files written at runtime (e.g., uploaded avatars/covers).
app.UseStaticFiles();

app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

app.UseAntiforgery();

app.MapStaticAssets();

app.MapPost("/account/logout", async (SignInManager<ApplicationUser> signInManager, HttpContext http) =>
{
    await signInManager.SignOutAsync();
    http.Response.Redirect("/");
}).RequireRateLimiting("account");

app.MapPost("/account/register", async (
    [FromForm] RegisterRequest request,
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    RoleManager<IdentityRole> roleManager,
    BoardGameMondays.Core.BgmMemberDirectoryService members,
    IWebHostEnvironment env) =>
{
    var userName = request.UserName?.Trim();
    var displayName = request.DisplayName?.Trim();
    if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(request.Password))
    {
        return Results.Redirect($"/register?error={Uri.EscapeDataString("Username and password are required.")}");
    }

    if (userName.Length < 3)
    {
        return Results.Redirect($"/register?error={Uri.EscapeDataString("Username must be at least 3 characters.")}");
    }

    if (userName.Length > 64)
    {
        return Results.Redirect($"/register?error={Uri.EscapeDataString("Username is too long.")}");
    }

    if (string.IsNullOrWhiteSpace(displayName))
    {
        return Results.Redirect($"/register?error={Uri.EscapeDataString("Name is required.")}");
    }

    if (displayName.Length > 80)
    {
        return Results.Redirect($"/register?error={Uri.EscapeDataString("Name is too long.")}");
    }

    if (!string.Equals(request.Password, request.ConfirmPassword, StringComparison.Ordinal))
    {
        return Results.Redirect($"/register?error={Uri.EscapeDataString("Passwords do not match.")}");
    }

    var user = new ApplicationUser { UserName = userName };
    var createResult = await userManager.CreateAsync(user, request.Password);
    if (!createResult.Succeeded)
    {
        var message = string.Join(" ", createResult.Errors.Select(e => e.Description));
        return Results.Redirect($"/register?error={Uri.EscapeDataString(message)}");
    }

    // Store the friendly display name separately from the login username.
    await userManager.AddClaimAsync(user, new Claim(BgmClaimTypes.DisplayName, displayName));

    var memberId = members.GetOrCreateMemberId(displayName);
    await userManager.AddClaimAsync(user, new Claim(BgmClaimTypes.MemberId, memberId.ToString()));

    // Dev convenience: automatically grant Admin role so existing admin tooling works.
    if (env.IsDevelopment())
    {
        const string adminRole = "Admin";
        if (!await roleManager.RoleExistsAsync(adminRole))
        {
            await roleManager.CreateAsync(new IdentityRole(adminRole));
        }

        await userManager.AddToRoleAsync(user, adminRole);
    }

    await signInManager.SignInAsync(user, isPersistent: true);
    members.GetOrCreate(displayName);
    return Results.Redirect("/");
}).RequireRateLimiting("account");

app.MapPost("/account/login", async (
    [FromForm] LoginRequest request,
    SignInManager<ApplicationUser> signInManager,
    UserManager<ApplicationUser> userManager,
    BoardGameMondays.Core.BgmMemberDirectoryService members,
    IWebHostEnvironment env) =>
{
    var userName = request.UserName?.Trim();
    if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(request.Password))
    {
        return Results.Redirect($"/login?error={Uri.EscapeDataString("Username and password are required.")}");
    }

    if (userName.Length < 3)
    {
        return Results.Redirect($"/login?error={Uri.EscapeDataString("Username must be at least 3 characters.")}");
    }

    if (userName.Length > 64)
    {
        return Results.Redirect($"/login?error={Uri.EscapeDataString("Username is too long.")}");
    }

    var lockoutOnFailure = !env.IsDevelopment();
    var result = await signInManager.PasswordSignInAsync(userName, request.Password, request.RememberMe, lockoutOnFailure);
    if (!result.Succeeded)
    {
        return Results.Redirect($"/login?error={Uri.EscapeDataString("Invalid username or password.")}");
    }

    var displayName = userName;
    var user = await userManager.FindByNameAsync(userName);
    if (user is not null)
    {
        var claims = await userManager.GetClaimsAsync(user);
        displayName = claims.FirstOrDefault(c => c.Type == BgmClaimTypes.DisplayName)?.Value?.Trim() ?? userName;
    }

    members.GetOrCreate(displayName);
    return Results.Redirect("/");
}).RequireRateLimiting("account");

app.MapPost("/account/avatar", async (
    HttpContext http,
    IFormFile? avatar,
    UserManager<ApplicationUser> userManager,
    ApplicationDbContext db,
    IAssetStorage storage) =>
{
    if (avatar is null || avatar.Length == 0)
    {
        return Results.Redirect($"/?avatarError={Uri.EscapeDataString("Please choose an image.")}#people");
    }

    const long maxBytes = 2 * 1024 * 1024; // 2MB
    if (avatar.Length > maxBytes)
    {
        return Results.Redirect($"/?avatarError={Uri.EscapeDataString("Image must be 2MB or smaller.")}#people");
    }

    string? extension;
    await using (var sniffStream = avatar.OpenReadStream())
    {
        extension = await ImageFileSniffer.DetectExtensionAsync(sniffStream);
    }

    if (string.IsNullOrWhiteSpace(extension))
    {
        return Results.Redirect($"/?avatarError={Uri.EscapeDataString("Supported formats: JPG, PNG, WEBP, GIF.")}#people");
    }

    if (string.Equals(extension, ".jpeg", StringComparison.OrdinalIgnoreCase))
    {
        extension = ".jpg";
    }

    var userName = http.User?.Identity?.Name;
    if (string.IsNullOrWhiteSpace(userName))
    {
        return Results.Redirect($"/?avatarError={Uri.EscapeDataString("You must be logged in.")}#people");
    }

    var user = await userManager.FindByNameAsync(userName);
    if (user is null)
    {
        return Results.Redirect($"/?avatarError={Uri.EscapeDataString("You must be logged in.")}#people");
    }

    var claims = await userManager.GetClaimsAsync(user);
    var displayName = claims.FirstOrDefault(c => c.Type == BgmClaimTypes.DisplayName)?.Value?.Trim();
    if (string.IsNullOrWhiteSpace(displayName))
    {
        displayName = userName;
    }

    MemberEntity? member = null;
    var memberIdClaim = claims.FirstOrDefault(c => c.Type == BgmClaimTypes.MemberId)?.Value;
    if (Guid.TryParse(memberIdClaim, out var memberId))
    {
        member = await db.Members.FirstOrDefaultAsync(m => m.Id == memberId);
    }

    if (member is null)
    {
        member = await db.Members.FirstOrDefaultAsync(m => m.Name.ToLower() == displayName.ToLower());
    }

    if (member is null)
    {
        member = new MemberEntity
        {
            Id = Guid.NewGuid(),
            Name = displayName,
            Email = $"{displayName.ToLowerInvariant()}@placeholder.com"
        };
        db.Members.Add(member);
        await db.SaveChangesAsync();
    }

    await using var avatarStream = avatar.OpenReadStream();
    member.AvatarUrl = await storage.SaveAvatarAsync(member.Id, avatarStream, extension);
    await db.SaveChangesAsync();

    return Results.Redirect("/?avatarUpdated=1#people");
})
.RequireAuthorization(new Microsoft.AspNetCore.Authorization.AuthorizeAttribute { Roles = "Admin" })
.RequireRateLimiting("account");

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

static bool IsMultipartBodyLengthLimitExceeded(Exception ex)
{
    // Depending on runtime/provider, this can be InvalidDataException directly or wrapped.
    for (Exception? current = ex; current is not null; current = current.InnerException)
    {
        if (current is InvalidDataException && current.Message.Contains("Multipart body length limit", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (current is BadHttpRequestException && current.Message.Contains("Multipart body length limit", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
    }

    return false;
}

internal sealed record RegisterRequest(string UserName, string DisplayName, string Password, string ConfirmPassword);
internal sealed class LoginRequest
{
    public string UserName { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public bool RememberMe { get; set; } = false;
}

internal static class BgmClaimTypes
{
    public const string DisplayName = "bgm:displayName";
    public const string MemberId = "bgm:memberId";
}
