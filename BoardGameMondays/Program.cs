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
using Microsoft.AspNetCore.WebUtilities;
using System.ComponentModel.DataAnnotations;
using System.Text;
using Azure.Identity;
using Azure.Extensions.AspNetCore.Configuration.Secrets;

var builder = WebApplication.CreateBuilder(args);

// Optional: load secrets from Azure Key Vault (managed identity in production).
var keyVaultUrl = builder.Configuration["KeyVault:Url"];
if (!string.IsNullOrWhiteSpace(keyVaultUrl))
{
    builder.Configuration.AddAzureKeyVault(new Uri(keyVaultUrl), new DefaultAzureCredential());
}

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Configure Blazor circuit options to handle users leaving the browser tab.
// This prevents the "unresponsive page" issue when users return after inactivity.
builder.Services.Configure<CircuitOptions>(options =>
{
    if (builder.Environment.IsDevelopment())
    {
        options.DetailedErrors = true;
    }

    // Allow disconnected circuits to stay alive longer (default is 3 minutes).
    // This gives users more time to return before losing their session.
    options.DisconnectedCircuitRetentionPeriod = TimeSpan.FromMinutes(10);

    // Maximum circuits to retain per user (helps with server memory management).
    options.DisconnectedCircuitMaxRetained = 100;

    // JSInterop call timeout - increase to prevent timeouts on slow connections.
    options.JSInteropDefaultCallTimeout = TimeSpan.FromMinutes(1);
});

// Configure SignalR hub options for better reconnection handling.
builder.Services.AddSignalR(hubOptions =>
{
    // Allow longer keep-alive interval so connections survive brief network blips.
    hubOptions.KeepAliveInterval = TimeSpan.FromSeconds(15);

    // Client timeout - how long the server waits without hearing from client.
    hubOptions.ClientTimeoutInterval = TimeSpan.FromSeconds(60);

    // Enable detailed errors in development for easier debugging.
    hubOptions.EnableDetailedErrors = builder.Environment.IsDevelopment();
});

// Database + Identity.
// Dev default is SQLite; production should use Azure SQL (SQL Server provider).
void ConfigureDbContextOptions(DbContextOptionsBuilder options)
{
    var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
    if (string.IsNullOrWhiteSpace(connectionString))
    {
        throw new InvalidOperationException("Missing connection string 'DefaultConnection'.");
    }

    DatabaseConnectionStringClassifier.EnsureNotSqliteInProduction(builder.Environment, connectionString);

    // Treat the connection string as SQLite only when it clearly points at a local SQLite file.
    // SQL Server connection strings often also contain "Data Source=", so don't use that as a signal.
    var isSqlite = DatabaseConnectionStringClassifier.IsSqlite(connectionString);

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

builder.Services.Configure<EmailOptions>(builder.Configuration.GetSection("Email"));
// Email sender routing: pick API or SMTP at runtime based on Email options.
builder.Services.AddHttpClient("email-api");
builder.Services.AddSingleton<ApiEmailSender>();
builder.Services.AddSingleton<SmtpEmailSender>();
builder.Services.AddSingleton<IEmailSender, RoutingEmailSender>();

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
        options.SignIn.RequireConfirmedEmail = builder.Configuration.GetValue<bool>("Email:RequireConfirmedEmail");
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

// ===== SERVICE LAYER CONFIGURATION =====
// Services are organized by domain (see REFACTORING_CLEANUP_GUIDE.md for folder structure plan)
// 
// CRITICAL: All services inject IDbContextFactory<ApplicationDbContext>, not DbContext directly.
// This is essential for Blazor Server (prevents concurrent-use conflicts on shared DbContext).
// See Core/Infrastructure/DatabaseExtensions.cs for common context lifecycle patterns.
//
// Service domains (target organization after Phase 4):
// - GameManagement: GameNightService (core CRUD, will split into 4 services in Phase 5)
// - Gameplay: BettingService → OddsService → RankingService (core game loop)
// - Community: BgmMemberService, BgmCoinService (member data + reward orchestration)
// - Compliance: ConsentService, GdprService (GDPR + privacy management)
// - Content: BlogService, BoardGameService, WantToPlayService, GameRecommendationService
// - Admin: ShopService, TicketService, AgreementService (shop/event management)
// - Reporting: RecapStatsService (game night analytics and stats generation)

// Game Management Domain
builder.Services.AddScoped<BoardGameMondays.Core.GameNightService>();
builder.Services.AddScoped<BoardGameMondays.Core.GameNightRsvpService>();
builder.Services.AddScoped<BoardGameMondays.Core.GameNightPlayerService>();
builder.Services.AddScoped<BoardGameMondays.Core.GameNightTeamService>();

// Gameplay Domain - Core betting/ranking/odds orchestration
// Dependency chain: BettingService → BgmCoinService → RankingService → OddsService
builder.Services.AddScoped<BoardGameMondays.Core.BettingService>();
builder.Services.AddScoped<BoardGameMondays.Core.RankingService>();
builder.Services.AddScoped<BoardGameMondays.Core.OddsService>();

// Community Domain - Member management and reward systems
builder.Services.AddScoped<BoardGameMondays.Core.BgmMemberService>();
builder.Services.AddScoped<BoardGameMondays.Core.BgmMemberDirectoryService>();
builder.Services.AddScoped<BoardGameMondays.Core.BgmCoinService>();

// Content Management Domain
builder.Services.AddScoped<BoardGameMondays.Core.BoardGameService>();
builder.Services.AddScoped<BoardGameMondays.Core.BlogService>();
builder.Services.AddScoped<BoardGameMondays.Core.WantToPlayService>();
builder.Services.AddScoped<BoardGameMondays.Core.GameRecommendationService>();

// Admin & Shop Operations Domain
builder.Services.AddScoped<BoardGameMondays.Core.ShopService>();
builder.Services.AddScoped<BoardGameMondays.Core.TicketService>();
builder.Services.AddScoped<BoardGameMondays.Core.AgreementService>();

// Compliance & Privacy Domain
builder.Services.AddScoped<BoardGameMondays.Core.ConsentService>();
builder.Services.AddScoped<BoardGameMondays.Core.GdprService>();
builder.Services.AddScoped<BoardGameMondays.Core.UserPreferencesService>();

// Reporting & Analytics Domain
builder.Services.AddScoped<BoardGameMondays.Core.RecapStatsService>();

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
        await EnsureSqlServerGameNightRsvpTableAsync(db, app.Logger);
        await EnsureSqlServerMemberProfileColumnsAsync(db, app.Logger);
        await EnsureSqlServerGameNightAttendeeSnackColumnAsync(db, app.Logger);
        await EnsureSqlServerVictoryRoutesAsync(db, app.Logger);
        await EnsureSqlServerMemberEloColumnsAsync(db, app.Logger);
        await EnsureSqlServerOddsDisplayFormatColumnAsync(db, app.Logger);
        await EnsureSqlServerShopItemColumnsAsync(db, app.Logger);
        await EnsureSqlServerGameScoreColumnsAsync(db, app.Logger);
        await EnsureSqlServerBlogPostColumnsAsync(db, app.Logger);
        await EnsureSqlServerGdprTablesAsync(db, app.Logger);
    }

    // Seed shop items (badge rings and emoji packs) - runs in both dev and production
    await ShopDataSeeder.SeedShopItemsAsync(db);

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

static async Task EnsureSqlServerGameNightRsvpTableAsync(ApplicationDbContext db, ILogger logger)
{
    if (!db.Database.IsSqlServer())
    {
        return;
    }

    const string sql = @"
IF OBJECT_ID(N'dbo.GameNightRsvps', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[GameNightRsvps] (
        [Id] int NOT NULL IDENTITY(1,1),
        [GameNightId] uniqueidentifier NOT NULL,
        [MemberId] uniqueidentifier NOT NULL,
        [IsAttending] bit NOT NULL,
        [CreatedOn] bigint NOT NULL,
        CONSTRAINT [PK_GameNightRsvps] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_GameNightRsvps_GameNights_GameNightId] FOREIGN KEY ([GameNightId]) REFERENCES [dbo].[GameNights]([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_GameNightRsvps_Members_MemberId] FOREIGN KEY ([MemberId]) REFERENCES [dbo].[Members]([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_GameNightRsvps_Night_Member'
      AND object_id = OBJECT_ID(N'dbo.GameNightRsvps')
)
BEGIN
    CREATE UNIQUE INDEX [IX_GameNightRsvps_Night_Member] ON [dbo].[GameNightRsvps]([GameNightId], [MemberId]);
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_GameNightRsvps_MemberId'
      AND object_id = OBJECT_ID(N'dbo.GameNightRsvps')
)
BEGIN
    CREATE INDEX [IX_GameNightRsvps_MemberId] ON [dbo].[GameNightRsvps]([MemberId]);
END;
";

    try
    {
        var affected = await db.Database.ExecuteSqlRawAsync(sql);
        if (affected != 0)
        {
            logger.LogWarning("Ensured GameNightRsvps table exists in SQL Server schema.");
        }
    }
    catch (Exception ex)
    {
        logger.LogCritical(ex, "Failed to ensure GameNightRsvps exists in SQL Server schema.");
        throw;
    }
}

static async Task EnsureSqlServerMemberProfileColumnsAsync(ApplicationDbContext db, ILogger logger)
{
    if (!db.Database.IsSqlServer())
    {
        return;
    }

    const string sql = @"
IF COL_LENGTH(N'dbo.Members', N'ProfileTagline') IS NULL
BEGIN
    ALTER TABLE [dbo].[Members] ADD [ProfileTagline] nvarchar(128) NULL;
END;

IF COL_LENGTH(N'dbo.Members', N'FavoriteGame') IS NULL
BEGIN
    ALTER TABLE [dbo].[Members] ADD [FavoriteGame] nvarchar(128) NULL;
END;

IF COL_LENGTH(N'dbo.Members', N'PlayStyle') IS NULL
BEGIN
    ALTER TABLE [dbo].[Members] ADD [PlayStyle] nvarchar(128) NULL;
END;

IF COL_LENGTH(N'dbo.Members', N'FunFact') IS NULL
BEGIN
    ALTER TABLE [dbo].[Members] ADD [FunFact] nvarchar(256) NULL;
END;
";

    try
    {
        var affected = await db.Database.ExecuteSqlRawAsync(sql);
        if (affected != 0)
        {
            logger.LogWarning("Ensured member profile columns exist in SQL Server schema.");
        }
    }
    catch (Exception ex)
    {
        logger.LogCritical(ex, "Failed to ensure member profile columns exist in SQL Server schema.");
        throw;
    }
}

static async Task EnsureSqlServerGameNightAttendeeSnackColumnAsync(ApplicationDbContext db, ILogger logger)
{
    if (!db.Database.IsSqlServer())
    {
        return;
    }

    const string sql = @"
IF COL_LENGTH(N'dbo.GameNightAttendees', N'SnackBrought') IS NULL
BEGIN
    ALTER TABLE [dbo].[GameNightAttendees] ADD [SnackBrought] nvarchar(128) NULL;
END;
";

    try
    {
        var affected = await db.Database.ExecuteSqlRawAsync(sql);
        if (affected != 0)
        {
            logger.LogWarning("Ensured GameNightAttendees.SnackBrought exists in SQL Server schema.");
        }
    }
    catch (Exception ex)
    {
        logger.LogCritical(ex, "Failed to ensure GameNightAttendees.SnackBrought exists in SQL Server schema.");
        throw;
    }
}

static async Task EnsureSqlServerVictoryRoutesAsync(ApplicationDbContext db, ILogger logger)
{
    if (!db.Database.IsSqlServer())
    {
        return;
    }

    const string sql = @"
IF OBJECT_ID(N'dbo.VictoryRoutes', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[VictoryRoutes] (
        [Id] uniqueidentifier NOT NULL,
        [GameId] uniqueidentifier NOT NULL,
        [Name] nvarchar(128) NOT NULL,
        [Type] int NOT NULL,
        [IsRequired] bit NOT NULL,
        [SortOrder] int NOT NULL,
        CONSTRAINT [PK_VictoryRoutes] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_VictoryRoutes_Games_GameId] FOREIGN KEY ([GameId]) REFERENCES [dbo].[Games]([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_VictoryRoutes_GameId_SortOrder' AND object_id = OBJECT_ID(N'dbo.VictoryRoutes'))
BEGIN
    CREATE UNIQUE INDEX [IX_VictoryRoutes_GameId_SortOrder] ON [dbo].[VictoryRoutes]([GameId],[SortOrder]);
END;

IF OBJECT_ID(N'dbo.VictoryRouteOptions', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[VictoryRouteOptions] (
        [Id] uniqueidentifier NOT NULL,
        [VictoryRouteId] uniqueidentifier NOT NULL,
        [Value] nvarchar(128) NOT NULL,
        [SortOrder] int NOT NULL,
        CONSTRAINT [PK_VictoryRouteOptions] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_VictoryRouteOptions_VictoryRoutes_VictoryRouteId] FOREIGN KEY ([VictoryRouteId]) REFERENCES [dbo].[VictoryRoutes]([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_VictoryRouteOptions_RouteId_SortOrder' AND object_id = OBJECT_ID(N'dbo.VictoryRouteOptions'))
BEGIN
    CREATE UNIQUE INDEX [IX_VictoryRouteOptions_RouteId_SortOrder] ON [dbo].[VictoryRouteOptions]([VictoryRouteId],[SortOrder]);
END;

IF OBJECT_ID(N'dbo.GameNightGameVictoryRouteValues', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[GameNightGameVictoryRouteValues] (
        [Id] int IDENTITY(1,1) NOT NULL,
        [GameNightGameId] int NOT NULL,
        [VictoryRouteId] uniqueidentifier NOT NULL,
        [ValueString] nvarchar(256) NULL,
        [ValueBool] bit NULL,
        CONSTRAINT [PK_GameNightGameVictoryRouteValues] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_GNGVRV_GameNightGames_GameNightGameId] FOREIGN KEY ([GameNightGameId]) REFERENCES [dbo].[GameNightGames]([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_GNGVRV_VictoryRoutes_VictoryRouteId] FOREIGN KEY ([VictoryRouteId]) REFERENCES [dbo].[VictoryRoutes]([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_GameNightGameVictoryRouteValues_Game_Route' AND object_id = OBJECT_ID(N'dbo.GameNightGameVictoryRouteValues'))
BEGIN
    CREATE UNIQUE INDEX [IX_GameNightGameVictoryRouteValues_Game_Route] ON [dbo].[GameNightGameVictoryRouteValues]([GameNightGameId],[VictoryRouteId]);
END;
";

    try
    {
        var affected = await db.Database.ExecuteSqlRawAsync(sql);
        if (affected != 0)
        {
            logger.LogInformation("Ensured VictoryRoutes schema (affected {Count}).", affected);
        }
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed ensuring VictoryRoutes schema.");
    }
}

static async Task EnsureSqlServerMemberEloColumnsAsync(ApplicationDbContext db, ILogger logger)
{
    if (!db.Database.IsSqlServer())
    {
        return;
    }

    const string sql = @"
IF COL_LENGTH(N'dbo.Members', N'EloRating') IS NULL
BEGIN
    ALTER TABLE [dbo].[Members] ADD [EloRating] int NOT NULL DEFAULT 1200;
END;

IF COL_LENGTH(N'dbo.Members', N'EloRatingUpdatedOn') IS NULL
BEGIN
    ALTER TABLE [dbo].[Members] ADD [EloRatingUpdatedOn] bigint NULL;
END;
";

    try
    {
        var affected = await db.Database.ExecuteSqlRawAsync(sql);
        if (affected != 0)
        {
            logger.LogWarning("Ensured member ELO columns exist in SQL Server schema.");
        }
    }
    catch (Exception ex)
    {
        logger.LogCritical(ex, "Failed to ensure member ELO columns exist in SQL Server schema.");
        throw;
    }
}

static async Task EnsureSqlServerOddsDisplayFormatColumnAsync(ApplicationDbContext db, ILogger logger)
{
    if (!db.Database.IsSqlServer())
    {
        return;
    }

    const string sql = @"
IF COL_LENGTH(N'dbo.AspNetUsers', N'OddsDisplayFormat') IS NULL
BEGIN
    ALTER TABLE [dbo].[AspNetUsers] ADD [OddsDisplayFormat] int NOT NULL DEFAULT 0;
END;
";

    try
    {
        var affected = await db.Database.ExecuteSqlRawAsync(sql);
        if (affected != 0)
        {
            logger.LogWarning("Ensured OddsDisplayFormat column exists in SQL Server schema.");
        }
    }
    catch (Exception ex)
    {
        logger.LogCritical(ex, "Failed to ensure OddsDisplayFormat column exists in SQL Server schema.");
        throw;
    }
}

static async Task EnsureSqlServerShopItemColumnsAsync(ApplicationDbContext db, ILogger logger)
{
    if (!db.Database.IsSqlServer())
    {
        return;
    }

    const string sql = @"
IF COL_LENGTH(N'dbo.ShopItems', N'MinWinsRequired') IS NULL
BEGIN
    ALTER TABLE [dbo].[ShopItems] ADD [MinWinsRequired] int NOT NULL DEFAULT 0;
END;
";

    try
    {
        var affected = await db.Database.ExecuteSqlRawAsync(sql);
        if (affected != 0)
        {
            logger.LogWarning("Ensured MinWinsRequired column exists in ShopItems table.");
        }
    }
    catch (Exception ex)
    {
        logger.LogCritical(ex, "Failed to ensure MinWinsRequired column exists in ShopItems table.");
        throw;
    }
}

static async Task EnsureSqlServerGameScoreColumnsAsync(ApplicationDbContext db, ILogger logger)
{
    if (!db.Database.IsSqlServer())
    {
        return;
    }

    const string sql = @"
IF COL_LENGTH(N'dbo.Games', N'AreScoresCountable') IS NULL
BEGIN
    ALTER TABLE [dbo].[Games] ADD [AreScoresCountable] bit NOT NULL DEFAULT 0;
END;

IF COL_LENGTH(N'dbo.Games', N'HighScore') IS NULL
BEGIN
    ALTER TABLE [dbo].[Games] ADD [HighScore] int NULL;
END;

IF COL_LENGTH(N'dbo.Games', N'HighScoreMemberId') IS NULL
BEGIN
    ALTER TABLE [dbo].[Games] ADD [HighScoreMemberId] uniqueidentifier NULL;
END;

IF COL_LENGTH(N'dbo.Games', N'HighScoreMemberName') IS NULL
BEGIN
    ALTER TABLE [dbo].[Games] ADD [HighScoreMemberName] nvarchar(128) NULL;
END;

IF COL_LENGTH(N'dbo.Games', N'HighScoreAchievedOn') IS NULL
BEGIN
    ALTER TABLE [dbo].[Games] ADD [HighScoreAchievedOn] bigint NULL;
END;

IF COL_LENGTH(N'dbo.GameNightGames', N'Score') IS NULL
BEGIN
    ALTER TABLE [dbo].[GameNightGames] ADD [Score] int NULL;
END;

IF COL_LENGTH(N'dbo.GameNightGames', N'IsHighScore') IS NULL
BEGIN
    ALTER TABLE [dbo].[GameNightGames] ADD [IsHighScore] bit NOT NULL DEFAULT 0;
END;
";

    try
    {
        var affected = await db.Database.ExecuteSqlRawAsync(sql);
        if (affected != 0)
        {
            logger.LogWarning("Patched missing score tracking columns in SQL Server schema.");
        }
    }
    catch (Exception ex)
    {
        logger.LogCritical(ex, "Failed to ensure score tracking columns exist in SQL Server schema.");
        throw;
    }
}

static async Task EnsureSqlServerBlogPostColumnsAsync(ApplicationDbContext db, ILogger logger)
{
    if (!db.Database.IsSqlServer())
    {
        return;
    }

    const string sql = @"
IF COL_LENGTH(N'dbo.BlogPosts', N'IsAdminOnly') IS NULL
BEGIN
    ALTER TABLE [dbo].[BlogPosts] ADD [IsAdminOnly] bit NOT NULL DEFAULT 0;
END;
";

    try
    {
        var affected = await db.Database.ExecuteSqlRawAsync(sql);
        if (affected != 0)
        {
            logger.LogWarning("Ensured IsAdminOnly column exists in BlogPosts table.");
        }
    }
    catch (Exception ex)
    {
        logger.LogCritical(ex, "Failed to ensure IsAdminOnly column exists in BlogPosts table.");
        throw;
    }
}

static async Task EnsureSqlServerGdprTablesAsync(ApplicationDbContext db, ILogger logger)
{
    if (!db.Database.IsSqlServer())
    {
        return;
    }

    const string sql = @"
IF OBJECT_ID(N'dbo.UserConsents', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[UserConsents] (
        [Id] uniqueidentifier NOT NULL PRIMARY KEY,
        [UserId] nvarchar(450) NULL,
        [AnonymousId] nvarchar(128) NULL,
        [ConsentType] nvarchar(64) NOT NULL,
        [PolicyVersion] nvarchar(32) NOT NULL,
        [IsGranted] bit NOT NULL,
        [ConsentedOn] bigint NOT NULL,
        [IpAddress] nvarchar(45) NULL,
        [UserAgent] nvarchar(512) NULL
    );
    CREATE INDEX [IX_UserConsents_UserId] ON [dbo].[UserConsents] ([UserId]);
    CREATE INDEX [IX_UserConsents_AnonymousId] ON [dbo].[UserConsents] ([AnonymousId]);
    CREATE INDEX [IX_UserConsents_UserId_ConsentType] ON [dbo].[UserConsents] ([UserId], [ConsentType]);
END;

IF OBJECT_ID(N'dbo.DataDeletionRequests', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[DataDeletionRequests] (
        [Id] uniqueidentifier NOT NULL PRIMARY KEY,
        [UserId] nvarchar(450) NOT NULL,
        [Email] nvarchar(256) NOT NULL,
        [RequestedOn] bigint NOT NULL,
        [ScheduledDeletionOn] bigint NOT NULL,
        [CompletedOn] bigint NULL,
        [Status] nvarchar(32) NOT NULL,
        [Reason] nvarchar(1024) NULL,
        [CancelledOn] bigint NULL
    );
    CREATE INDEX [IX_DataDeletionRequests_UserId] ON [dbo].[DataDeletionRequests] ([UserId]);
    CREATE INDEX [IX_DataDeletionRequests_Status] ON [dbo].[DataDeletionRequests] ([Status]);
END;
";

    try
    {
        await db.Database.ExecuteSqlRawAsync(sql);
        logger.LogInformation("Ensured GDPR consent tables exist in SQL Server schema.");
    }
    catch (Exception ex)
    {
        logger.LogCritical(ex, "Failed to ensure GDPR tables exist in SQL Server schema.");
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

        // AspNetUsers.OddsDisplayFormat
        var hasOddsDisplayFormat = false;
        await using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "PRAGMA table_info('AspNetUsers');";
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var name = reader.GetString(1);
                if (string.Equals(name, "OddsDisplayFormat", StringComparison.OrdinalIgnoreCase))
                {
                    hasOddsDisplayFormat = true;
                    break;
                }
            }
        }

        if (!hasOddsDisplayFormat)
        {
            await using var alter = connection.CreateCommand();
            alter.CommandText = "ALTER TABLE AspNetUsers ADD COLUMN OddsDisplayFormat INTEGER NOT NULL DEFAULT 0;";
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

        // BlogPosts.IsAdminOnly
        var hasIsAdminOnly = false;
        await using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "PRAGMA table_info('BlogPosts');";
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var name = reader.GetString(1);
                if (string.Equals(name, "IsAdminOnly", StringComparison.OrdinalIgnoreCase))
                {
                    hasIsAdminOnly = true;
                    break;
                }
            }
        }

        if (!hasIsAdminOnly)
        {
            await using var alter = connection.CreateCommand();
            alter.CommandText = "ALTER TABLE BlogPosts ADD COLUMN IsAdminOnly INTEGER NOT NULL DEFAULT 0;";
            await alter.ExecuteNonQueryAsync();
        }

        // Members profile columns
        var hasProfileTagline = false;
        var hasFavoriteGame = false;
        var hasPlayStyle = false;
        var hasFunFact = false;
        await using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "PRAGMA table_info('Members');";
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var name = reader.GetString(1);
                if (string.Equals(name, "ProfileTagline", StringComparison.OrdinalIgnoreCase)) hasProfileTagline = true;
                if (string.Equals(name, "FavoriteGame", StringComparison.OrdinalIgnoreCase)) hasFavoriteGame = true;
                if (string.Equals(name, "PlayStyle", StringComparison.OrdinalIgnoreCase)) hasPlayStyle = true;
                if (string.Equals(name, "FunFact", StringComparison.OrdinalIgnoreCase)) hasFunFact = true;
            }
        }

        if (!hasProfileTagline)
        {
            await using var alter = connection.CreateCommand();
            alter.CommandText = "ALTER TABLE Members ADD COLUMN ProfileTagline TEXT NULL;";
            await alter.ExecuteNonQueryAsync();
        }

        if (!hasFavoriteGame)
        {
            await using var alter = connection.CreateCommand();
            alter.CommandText = "ALTER TABLE Members ADD COLUMN FavoriteGame TEXT NULL;";
            await alter.ExecuteNonQueryAsync();
        }

        if (!hasPlayStyle)
        {
            await using var alter = connection.CreateCommand();
            alter.CommandText = "ALTER TABLE Members ADD COLUMN PlayStyle TEXT NULL;";
            await alter.ExecuteNonQueryAsync();
        }

        if (!hasFunFact)
        {
            await using var alter = connection.CreateCommand();
            alter.CommandText = "ALTER TABLE Members ADD COLUMN FunFact TEXT NULL;";
            await alter.ExecuteNonQueryAsync();
        }

        // Members ELO rating columns
        var hasEloRating = false;
        var hasEloRatingUpdatedOn = false;
        await using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "PRAGMA table_info('Members');";
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var name = reader.GetString(1);
                if (string.Equals(name, "EloRating", StringComparison.OrdinalIgnoreCase)) hasEloRating = true;
                if (string.Equals(name, "EloRatingUpdatedOn", StringComparison.OrdinalIgnoreCase)) hasEloRatingUpdatedOn = true;
            }
        }

        if (!hasEloRating)
        {
            await using var alter = connection.CreateCommand();
            alter.CommandText = "ALTER TABLE Members ADD COLUMN EloRating INTEGER NOT NULL DEFAULT 1200;";
            await alter.ExecuteNonQueryAsync();
        }

        if (!hasEloRatingUpdatedOn)
        {
            await using var alter = connection.CreateCommand();
            alter.CommandText = "ALTER TABLE Members ADD COLUMN EloRatingUpdatedOn INTEGER NULL;";
            await alter.ExecuteNonQueryAsync();
        }

        // Members.LastMondayCoinsClaimedDateKey
        var hasLastMondayCoinsClaimedDateKey = false;
        await using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "PRAGMA table_info('Members');";
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var name = reader.GetString(1);
                if (string.Equals(name, "LastMondayCoinsClaimedDateKey", StringComparison.OrdinalIgnoreCase))
                {
                    hasLastMondayCoinsClaimedDateKey = true;
                    break;
                }
            }
        }

        if (!hasLastMondayCoinsClaimedDateKey)
        {
            await using var alter = connection.CreateCommand();
            alter.CommandText = "ALTER TABLE Members ADD COLUMN LastMondayCoinsClaimedDateKey INTEGER NULL;";
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
        var hasScoresCountable = false;
        var hasHighScore = false;
        var hasHighScoreMemberId = false;
        var hasHighScoreMemberName = false;
        var hasHighScoreAchievedOn = false;
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
                if (string.Equals(name, "AreScoresCountable", StringComparison.OrdinalIgnoreCase)) hasScoresCountable = true;
                if (string.Equals(name, "HighScore", StringComparison.OrdinalIgnoreCase)) hasHighScore = true;
                if (string.Equals(name, "HighScoreMemberId", StringComparison.OrdinalIgnoreCase)) hasHighScoreMemberId = true;
                if (string.Equals(name, "HighScoreMemberName", StringComparison.OrdinalIgnoreCase)) hasHighScoreMemberName = true;
                if (string.Equals(name, "HighScoreAchievedOn", StringComparison.OrdinalIgnoreCase)) hasHighScoreAchievedOn = true;
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

        if (!hasScoresCountable)
        {
            await using var alter = connection.CreateCommand();
            alter.CommandText = "ALTER TABLE Games ADD COLUMN AreScoresCountable INTEGER NOT NULL DEFAULT 0;";
            await alter.ExecuteNonQueryAsync();
        }

        if (!hasHighScore)
        {
            await using var alter = connection.CreateCommand();
            alter.CommandText = "ALTER TABLE Games ADD COLUMN HighScore INTEGER NULL;";
            await alter.ExecuteNonQueryAsync();
        }

        if (!hasHighScoreMemberId)
        {
            await using var alter = connection.CreateCommand();
            alter.CommandText = "ALTER TABLE Games ADD COLUMN HighScoreMemberId TEXT NULL;";
            await alter.ExecuteNonQueryAsync();
        }

        if (!hasHighScoreMemberName)
        {
            await using var alter = connection.CreateCommand();
            alter.CommandText = "ALTER TABLE Games ADD COLUMN HighScoreMemberName TEXT NULL;";
            await alter.ExecuteNonQueryAsync();
        }

        if (!hasHighScoreAchievedOn)
        {
            await using var alter = connection.CreateCommand();
            alter.CommandText = "ALTER TABLE Games ADD COLUMN HighScoreAchievedOn INTEGER NULL;";
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

        // Want to play votes
        await using (var createWantToPlayVotes = connection.CreateCommand())
        {
            createWantToPlayVotes.CommandText = @"
CREATE TABLE IF NOT EXISTS WantToPlayVotes (
    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    GameId TEXT NOT NULL,
    UserId TEXT NOT NULL,
    WeekKey INTEGER NOT NULL,
    CreatedOn INTEGER NOT NULL,
    FOREIGN KEY (GameId) REFERENCES Games(Id) ON DELETE CASCADE
);
";
            await createWantToPlayVotes.ExecuteNonQueryAsync();
        }

        await using (var createWantToPlayIndexes = connection.CreateCommand())
        {
            createWantToPlayIndexes.CommandText = @"
CREATE UNIQUE INDEX IF NOT EXISTS IX_WantToPlayVotes_User_Game_Week ON WantToPlayVotes(UserId, GameId, WeekKey);
CREATE INDEX IF NOT EXISTS IX_WantToPlayVotes_User_Week ON WantToPlayVotes(UserId, WeekKey);
CREATE INDEX IF NOT EXISTS IX_WantToPlayVotes_Game ON WantToPlayVotes(GameId);
";
            await createWantToPlayIndexes.ExecuteNonQueryAsync();
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
    SnackBrought TEXT NULL,
    FOREIGN KEY (GameNightId) REFERENCES GameNights(Id) ON DELETE CASCADE,
    FOREIGN KEY (MemberId) REFERENCES Members(Id) ON DELETE CASCADE
);
";
            await createGameNightAttendees.ExecuteNonQueryAsync();
        }

        var hasSnackBrought = false;
        await using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "PRAGMA table_info('GameNightAttendees');";
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var name = reader.GetString(1);
                if (string.Equals(name, "SnackBrought", StringComparison.OrdinalIgnoreCase))
                {
                    hasSnackBrought = true;
                    break;
                }
            }
        }

        if (!hasSnackBrought)
        {
            await using var alter = connection.CreateCommand();
            alter.CommandText = "ALTER TABLE GameNightAttendees ADD COLUMN SnackBrought TEXT NULL;";
            await alter.ExecuteNonQueryAsync();
        }

        await using (var createGameNightRsvps = connection.CreateCommand())
        {
            createGameNightRsvps.CommandText = @"
CREATE TABLE IF NOT EXISTS GameNightRsvps (
    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    GameNightId TEXT NOT NULL,
    MemberId TEXT NOT NULL,
    IsAttending INTEGER NOT NULL,
    CreatedOn INTEGER NOT NULL,
    FOREIGN KEY (GameNightId) REFERENCES GameNights(Id) ON DELETE CASCADE,
    FOREIGN KEY (MemberId) REFERENCES Members(Id) ON DELETE CASCADE
);
";
            await createGameNightRsvps.ExecuteNonQueryAsync();
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
    WinnerTeamName TEXT NULL,
    Score INTEGER NULL,
    IsHighScore INTEGER NOT NULL DEFAULT 0,
    CreatedOn INTEGER NOT NULL,
    FOREIGN KEY (GameNightId) REFERENCES GameNights(Id) ON DELETE CASCADE,
    FOREIGN KEY (GameId) REFERENCES Games(Id) ON DELETE CASCADE
);
";
            await createGameNightGames.ExecuteNonQueryAsync();
        }

        // Victory routes (per-game templates)
        await using (var createVictoryRoutes = connection.CreateCommand())
        {
            createVictoryRoutes.CommandText = @"
CREATE TABLE IF NOT EXISTS VictoryRoutes (
    Id TEXT NOT NULL PRIMARY KEY,
    GameId TEXT NOT NULL,
    Name TEXT NOT NULL,
    Type INTEGER NOT NULL,
    IsRequired INTEGER NOT NULL,
    SortOrder INTEGER NOT NULL,
    FOREIGN KEY (GameId) REFERENCES Games(Id) ON DELETE CASCADE
);
";
            await createVictoryRoutes.ExecuteNonQueryAsync();
        }

        await using (var createVictoryRouteOptions = connection.CreateCommand())
        {
            createVictoryRouteOptions.CommandText = @"
CREATE TABLE IF NOT EXISTS VictoryRouteOptions (
    Id TEXT NOT NULL PRIMARY KEY,
    VictoryRouteId TEXT NOT NULL,
    Value TEXT NOT NULL,
    SortOrder INTEGER NOT NULL,
    FOREIGN KEY (VictoryRouteId) REFERENCES VictoryRoutes(Id) ON DELETE CASCADE
);
";
            await createVictoryRouteOptions.ExecuteNonQueryAsync();
        }

        await using (var createVictoryRouteValues = connection.CreateCommand())
        {
            createVictoryRouteValues.CommandText = @"
CREATE TABLE IF NOT EXISTS GameNightGameVictoryRouteValues (
    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    GameNightGameId INTEGER NOT NULL,
    VictoryRouteId TEXT NOT NULL,
    ValueString TEXT NULL,
    ValueBool INTEGER NULL,
    FOREIGN KEY (GameNightGameId) REFERENCES GameNightGames(Id) ON DELETE CASCADE,
    FOREIGN KEY (VictoryRouteId) REFERENCES VictoryRoutes(Id) ON DELETE CASCADE
);
";
            await createVictoryRouteValues.ExecuteNonQueryAsync();
        }

        await using (var createVictoryRouteIndexes = connection.CreateCommand())
        {
            createVictoryRouteIndexes.CommandText = @"
CREATE UNIQUE INDEX IF NOT EXISTS IX_VictoryRoutes_GameId_SortOrder ON VictoryRoutes(GameId, SortOrder);
CREATE UNIQUE INDEX IF NOT EXISTS IX_VictoryRouteOptions_RouteId_SortOrder ON VictoryRouteOptions(VictoryRouteId, SortOrder);
CREATE UNIQUE INDEX IF NOT EXISTS IX_GameNightGameVictoryRouteValues_Game_Route ON GameNightGameVictoryRouteValues(GameNightGameId, VictoryRouteId);
";
            await createVictoryRouteIndexes.ExecuteNonQueryAsync();
        }

        var hasIsConfirmed = false;
        var hasWinnerMemberId = false;
        var hasWinnerTeamName = false;
        var hasScore = false;
        var hasIsHighScore = false;
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

                if (string.Equals(name, "Score", StringComparison.OrdinalIgnoreCase))
                {
                    hasScore = true;
                }

                if (string.Equals(name, "IsHighScore", StringComparison.OrdinalIgnoreCase))
                {
                    hasIsHighScore = true;
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

        if (!hasScore)
        {
            await using var alter = connection.CreateCommand();
            alter.CommandText = "ALTER TABLE GameNightGames ADD COLUMN Score INTEGER NULL;";
            await alter.ExecuteNonQueryAsync();
        }

        if (!hasIsHighScore)
        {
            await using var alter = connection.CreateCommand();
            alter.CommandText = "ALTER TABLE GameNightGames ADD COLUMN IsHighScore INTEGER NOT NULL DEFAULT 0;";
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

        // Drop any old unique index on GameNightGames (GameNightId, GameId) to allow duplicate games per Monday
        await using (var dropOldIndex = connection.CreateCommand())
        {
            dropOldIndex.CommandText = "DROP INDEX IF EXISTS IX_GameNightGames_Night_Game;";
            await dropOldIndex.ExecuteNonQueryAsync();
        }

        await using (var createGameNightIndexes = connection.CreateCommand())
        {
            createGameNightIndexes.CommandText = @"
CREATE UNIQUE INDEX IF NOT EXISTS IX_GameNights_DateKey ON GameNights(DateKey);
CREATE UNIQUE INDEX IF NOT EXISTS IX_GameNightAttendees_Night_Member ON GameNightAttendees(GameNightId, MemberId);
CREATE UNIQUE INDEX IF NOT EXISTS IX_GameNightRsvps_Night_Member ON GameNightRsvps(GameNightId, MemberId);
CREATE INDEX IF NOT EXISTS IX_GameNightGames_Night_Game ON GameNightGames(GameNightId, GameId);
CREATE UNIQUE INDEX IF NOT EXISTS IX_GameNightGamePlayers_NightGame_Member ON GameNightGamePlayers(GameNightGameId, MemberId);
CREATE UNIQUE INDEX IF NOT EXISTS IX_GameNightGameOdds_NightGame_Member ON GameNightGameOdds(GameNightGameId, MemberId);
CREATE UNIQUE INDEX IF NOT EXISTS IX_GameNightGameBets_NightGame_User ON GameNightGameBets(GameNightGameId, UserId);
CREATE INDEX IF NOT EXISTS IX_GameNightAttendees_MemberId ON GameNightAttendees(MemberId);
CREATE INDEX IF NOT EXISTS IX_GameNightRsvps_MemberId ON GameNightRsvps(MemberId);
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
    IsAdminOnly INTEGER NOT NULL DEFAULT 0,
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

        // Shop system tables
        await using (var createShopItems = connection.CreateCommand())
        {
            createShopItems.CommandText = @"
CREATE TABLE IF NOT EXISTS ShopItems (
    Id TEXT NOT NULL PRIMARY KEY,
    Name TEXT NOT NULL,
    Description TEXT NOT NULL,
    Price INTEGER NOT NULL,
    ItemType TEXT NOT NULL,
    Data TEXT NOT NULL,
    IsActive INTEGER NOT NULL,
    MembersOnly INTEGER NOT NULL,
    CreatedOn INTEGER NOT NULL
);
";
            await createShopItems.ExecuteNonQueryAsync();
        }

        await using (var createUserPurchases = connection.CreateCommand())
        {
            createUserPurchases.CommandText = @"
CREATE TABLE IF NOT EXISTS UserPurchases (
    Id TEXT NOT NULL PRIMARY KEY,
    UserId TEXT NOT NULL,
    ShopItemId TEXT NOT NULL,
    PurchasedOn INTEGER NOT NULL,
    FOREIGN KEY (ShopItemId) REFERENCES ShopItems(Id) ON DELETE CASCADE
);
";
            await createUserPurchases.ExecuteNonQueryAsync();
        }

        await using (var createGameResultReactions = connection.CreateCommand())
        {
            createGameResultReactions.CommandText = @"
CREATE TABLE IF NOT EXISTS GameResultReactions (
    Id TEXT NOT NULL PRIMARY KEY,
    GameNightGameId INTEGER NOT NULL,
    UserId TEXT NOT NULL,
    Emoji TEXT NOT NULL,
    CreatedOn INTEGER NOT NULL,
    FOREIGN KEY (GameNightGameId) REFERENCES GameNightGames(Id) ON DELETE CASCADE
);
";
            await createGameResultReactions.ExecuteNonQueryAsync();
        }

        await using (var createShopIndexes = connection.CreateCommand())
        {
            createShopIndexes.CommandText = @"
CREATE INDEX IF NOT EXISTS IX_UserPurchases_ShopItemId ON UserPurchases(ShopItemId);
CREATE INDEX IF NOT EXISTS IX_UserPurchases_UserId ON UserPurchases(UserId);
CREATE INDEX IF NOT EXISTS IX_GameResultReactions_GameNightGameId ON GameResultReactions(GameNightGameId);
CREATE INDEX IF NOT EXISTS IX_GameResultReactions_UserId ON GameResultReactions(UserId);
";
            await createShopIndexes.ExecuteNonQueryAsync();
        }

        // ShopItems.MinWinsRequired (badge ring win requirements)
        var hasMinWinsRequired = false;
        await using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "PRAGMA table_info('ShopItems');";
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var name = reader.GetString(1);
                if (string.Equals(name, "MinWinsRequired", StringComparison.OrdinalIgnoreCase))
                {
                    hasMinWinsRequired = true;
                    break;
                }
            }
        }

        if (!hasMinWinsRequired)
        {
            await using var alter = connection.CreateCommand();
            alter.CommandText = "ALTER TABLE ShopItems ADD COLUMN MinWinsRequired INTEGER NOT NULL DEFAULT 0;";
            await alter.ExecuteNonQueryAsync();
        }

        // GDPR Consent tables
        await using (var createUserConsents = connection.CreateCommand())
        {
            createUserConsents.CommandText = @"
CREATE TABLE IF NOT EXISTS UserConsents (
    Id TEXT NOT NULL PRIMARY KEY,
    UserId TEXT NULL,
    AnonymousId TEXT NULL,
    ConsentType TEXT NOT NULL,
    PolicyVersion TEXT NOT NULL,
    IsGranted INTEGER NOT NULL,
    ConsentedOn INTEGER NOT NULL,
    IpAddress TEXT NULL,
    UserAgent TEXT NULL
);
";
            await createUserConsents.ExecuteNonQueryAsync();
        }

        await using (var createConsentIndexes = connection.CreateCommand())
        {
            createConsentIndexes.CommandText = @"
CREATE INDEX IF NOT EXISTS IX_UserConsents_UserId ON UserConsents(UserId);
CREATE INDEX IF NOT EXISTS IX_UserConsents_AnonymousId ON UserConsents(AnonymousId);
CREATE INDEX IF NOT EXISTS IX_UserConsents_UserId_ConsentType ON UserConsents(UserId, ConsentType);
";
            await createConsentIndexes.ExecuteNonQueryAsync();
        }

        await using (var createDataDeletionRequests = connection.CreateCommand())
        {
            createDataDeletionRequests.CommandText = @"
CREATE TABLE IF NOT EXISTS DataDeletionRequests (
    Id TEXT NOT NULL PRIMARY KEY,
    UserId TEXT NOT NULL,
    Email TEXT NOT NULL,
    RequestedOn INTEGER NOT NULL,
    ScheduledDeletionOn INTEGER NOT NULL,
    CompletedOn INTEGER NULL,
    Status TEXT NOT NULL,
    Reason TEXT NULL,
    CancelledOn INTEGER NULL
);
";
            await createDataDeletionRequests.ExecuteNonQueryAsync();
        }

        await using (var createDeletionIndexes = connection.CreateCommand())
        {
            createDeletionIndexes.CommandText = @"
CREATE INDEX IF NOT EXISTS IX_DataDeletionRequests_UserId ON DataDeletionRequests(UserId);
CREATE INDEX IF NOT EXISTS IX_DataDeletionRequests_Status ON DataDeletionRequests(Status);
";
            await createDeletionIndexes.ExecuteNonQueryAsync();
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
            context.Response.Redirect($"/account?avatarError={Uri.EscapeDataString("Image must be 10MB or smaller.")}#account-profile");
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
}).DisableAntiforgery().RequireRateLimiting("account");

app.MapPost("/account/register", async (
    [FromForm] RegisterRequest request,
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    RoleManager<IdentityRole> roleManager,
    BoardGameMondays.Core.BgmMemberDirectoryService members,
    IWebHostEnvironment env,
    HttpContext http,
    IEmailSender emailSender,
    IConfiguration configuration) =>
{
    var safeReturnUrl = ReturnUrlHelpers.GetSafeReturnUrl(request.ReturnUrl);
    var userName = request.UserName?.Trim();
    var email = request.Email?.Trim();
    var displayName = request.DisplayName?.Trim();
    if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(request.Password))
    {
        return Results.Redirect(ReturnUrlHelpers.AppendReturnUrl($"/register?error={Uri.EscapeDataString("Username and password are required.")}", safeReturnUrl));
    }

    if (string.IsNullOrWhiteSpace(email))
    {
        return Results.Redirect(ReturnUrlHelpers.AppendReturnUrl($"/register?error={Uri.EscapeDataString("Email is required for password recovery.")}", safeReturnUrl));
    }

    var emailValidator = new EmailAddressAttribute();
    if (!emailValidator.IsValid(email))
    {
        return Results.Redirect(ReturnUrlHelpers.AppendReturnUrl($"/register?error={Uri.EscapeDataString("Please enter a valid email address.")}", safeReturnUrl));
    }

    if (userName.Length < 3)
    {
        return Results.Redirect(ReturnUrlHelpers.AppendReturnUrl($"/register?error={Uri.EscapeDataString("Username must be at least 3 characters.")}", safeReturnUrl));
    }

    if (userName.Length > 64)
    {
        return Results.Redirect(ReturnUrlHelpers.AppendReturnUrl($"/register?error={Uri.EscapeDataString("Username is too long.")}", safeReturnUrl));
    }

    if (string.IsNullOrWhiteSpace(displayName))
    {
        return Results.Redirect(ReturnUrlHelpers.AppendReturnUrl($"/register?error={Uri.EscapeDataString("Name is required.")}", safeReturnUrl));
    }

    if (displayName.Length > 80)
    {
        return Results.Redirect(ReturnUrlHelpers.AppendReturnUrl($"/register?error={Uri.EscapeDataString("Name is too long.")}", safeReturnUrl));
    }

    if (!string.Equals(request.Password, request.ConfirmPassword, StringComparison.Ordinal))
    {
        return Results.Redirect(ReturnUrlHelpers.AppendReturnUrl($"/register?error={Uri.EscapeDataString("Passwords do not match.")}", safeReturnUrl));
    }

    var existingEmailUser = await userManager.FindByEmailAsync(email);
    if (existingEmailUser is not null)
    {
        return Results.Redirect(ReturnUrlHelpers.AppendReturnUrl($"/register?error={Uri.EscapeDataString("That email address is already in use.")}", safeReturnUrl));
    }

    if (!request.AcceptTerms)
    {
        return Results.Redirect(ReturnUrlHelpers.AppendReturnUrl($"/register?error={Uri.EscapeDataString("You must accept the Privacy Policy and Terms of Service.")}", safeReturnUrl));
    }

    var user = new ApplicationUser { UserName = userName, Email = email };
    var createResult = await userManager.CreateAsync(user, request.Password);
    if (!createResult.Succeeded)
    {
        var message = string.Join(" ", createResult.Errors.Select(e => e.Description));
        return Results.Redirect(ReturnUrlHelpers.AppendReturnUrl($"/register?error={Uri.EscapeDataString(message)}", safeReturnUrl));
    }

    // Store the friendly display name separately from the login username.
    await userManager.AddClaimAsync(user, new Claim(BgmClaimTypes.DisplayName, displayName));

    var memberId = members.GetOrCreateMemberId(displayName);
    await userManager.AddClaimAsync(user, new Claim(BgmClaimTypes.MemberId, memberId.ToString()));

    await SendEmailConfirmationAsync(userManager, emailSender, user, http);

    // Record GDPR consent for privacy policy and terms of service
    using (var scope = app.Services.CreateScope())
    {
        var consentService = scope.ServiceProvider.GetRequiredService<ConsentService>();
        var ipAddress = http.Connection.RemoteIpAddress?.ToString();
        var userAgent = http.Request.Headers.UserAgent.ToString();
        await consentService.RecordRegistrationConsentAsync(user.Id, ipAddress, userAgent);
    }

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

    var requireConfirmed = configuration.GetValue<bool>("Email:RequireConfirmedEmail");
    if (requireConfirmed)
    {
        return Results.Redirect(ReturnUrlHelpers.AppendReturnUrl("/login?confirm=1", safeReturnUrl));
    }

    await signInManager.SignInAsync(user, isPersistent: true);
    members.GetOrCreate(displayName);
    return Results.Redirect(safeReturnUrl ?? "/");
}).DisableAntiforgery().RequireRateLimiting("account");

app.MapPost("/account/login", async (
    [FromForm] LoginRequest request,
    SignInManager<ApplicationUser> signInManager,
    UserManager<ApplicationUser> userManager,
    BoardGameMondays.Core.BgmMemberDirectoryService members,
    IWebHostEnvironment env,
    HttpContext http,
    IEmailSender emailSender) =>
{
    var safeReturnUrl = ReturnUrlHelpers.GetSafeReturnUrl(request.ReturnUrl);
    var userName = request.UserName?.Trim();
    if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(request.Password))
    {
        return Results.Redirect(ReturnUrlHelpers.AppendReturnUrl($"/login?error={Uri.EscapeDataString("Username and password are required.")}", safeReturnUrl));
    }

    if (userName.Length < 3)
    {
        return Results.Redirect(ReturnUrlHelpers.AppendReturnUrl($"/login?error={Uri.EscapeDataString("Username must be at least 3 characters.")}", safeReturnUrl));
    }

    if (userName.Length > 64)
    {
        return Results.Redirect(ReturnUrlHelpers.AppendReturnUrl($"/login?error={Uri.EscapeDataString("Username is too long.")}", safeReturnUrl));
    }

    var lockoutOnFailure = !env.IsDevelopment();
    var result = await signInManager.PasswordSignInAsync(userName, request.Password, request.RememberMe, lockoutOnFailure);
    if (!result.Succeeded)
    {
        if (result.IsNotAllowed)
        {
            var notAllowedUser = await userManager.FindByNameAsync(userName);
            if (notAllowedUser is not null && !await userManager.IsEmailConfirmedAsync(notAllowedUser))
            {
                if (!string.IsNullOrWhiteSpace(notAllowedUser.Email))
                {
                    await SendEmailConfirmationAsync(userManager, emailSender, notAllowedUser, http);
                }

                return Results.Redirect(ReturnUrlHelpers.AppendReturnUrl($"/login?error={Uri.EscapeDataString("Please confirm your email to sign in.")}&confirm=1", safeReturnUrl));
            }
        }

        return Results.Redirect(ReturnUrlHelpers.AppendReturnUrl($"/login?error={Uri.EscapeDataString("Invalid username or password.")}", safeReturnUrl));
    }

    var displayName = userName;
    var user = await userManager.FindByNameAsync(userName);
    if (user is not null)
    {
        var claims = await userManager.GetClaimsAsync(user);
        displayName = claims.FirstOrDefault(c => c.Type == BgmClaimTypes.DisplayName)?.Value?.Trim() ?? userName;
    }

    members.GetOrCreate(displayName);
    return Results.Redirect(safeReturnUrl ?? "/");
}).DisableAntiforgery().RequireRateLimiting("account");

app.MapPost("/account/forgot", async (
    [FromForm] ForgotPasswordRequest request,
    UserManager<ApplicationUser> userManager,
    IEmailSender emailSender,
    HttpContext http) =>
{
    var email = request.Email?.Trim();
    if (string.IsNullOrWhiteSpace(email))
    {
        return Results.Redirect($"/forgot-password?error={Uri.EscapeDataString("Email is required.")}");
    }

    var emailValidator = new EmailAddressAttribute();
    if (!emailValidator.IsValid(email))
    {
        return Results.Redirect($"/forgot-password?error={Uri.EscapeDataString("Please enter a valid email address.")}");
    }

    var user = await userManager.FindByEmailAsync(email);
    if (user is not null)
    {
        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        var encodedToken = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
        var callbackUrl = $"{http.Request.Scheme}://{http.Request.Host}/reset-password?email={Uri.EscapeDataString(email)}&token={Uri.EscapeDataString(encodedToken)}";

        var body = $"<p>We received a password reset request for your Board Game Mondays account.</p>" +
                   $"<p><a href=\"{callbackUrl}\">Reset your password</a></p>" +
                   $"<p>If you didn't request this, you can ignore this email.</p>";

        await emailSender.SendEmailAsync(email, "Reset your Board Game Mondays password", body);
    }

    return Results.Redirect("/forgot-password?sent=1");
}).DisableAntiforgery().RequireRateLimiting("account");

app.MapPost("/account/reset", async (
    [FromForm] ResetPasswordRequest request,
    UserManager<ApplicationUser> userManager) =>
{
    var email = request.Email?.Trim();
    var token = request.Token?.Trim();

    if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(token))
    {
        return Results.Redirect($"/reset-password?error={Uri.EscapeDataString("The reset link is missing or invalid.")}");
    }

    if (string.IsNullOrWhiteSpace(request.Password))
    {
        return Results.Redirect($"/reset-password?error={Uri.EscapeDataString("Password is required.")}&email={Uri.EscapeDataString(email)}&token={Uri.EscapeDataString(token)}");
    }

    if (!string.Equals(request.Password, request.ConfirmPassword, StringComparison.Ordinal))
    {
        return Results.Redirect($"/reset-password?error={Uri.EscapeDataString("Passwords do not match.")}&email={Uri.EscapeDataString(email)}&token={Uri.EscapeDataString(token)}");
    }

    var user = await userManager.FindByEmailAsync(email);
    if (user is null)
    {
        return Results.Redirect("/login?reset=1");
    }

    string decodedToken;
    try
    {
        decodedToken = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(token));
    }
    catch
    {
        return Results.Redirect($"/reset-password?error={Uri.EscapeDataString("The reset token is invalid.")}&email={Uri.EscapeDataString(email)}");
    }

    var result = await userManager.ResetPasswordAsync(user, decodedToken, request.Password);
    if (!result.Succeeded)
    {
        var message = string.Join(" ", result.Errors.Select(e => e.Description));
        return Results.Redirect($"/reset-password?error={Uri.EscapeDataString(message)}&email={Uri.EscapeDataString(email)}&token={Uri.EscapeDataString(token)}");
    }

    // Auto-confirm email when user resets password - clicking the reset link proves they own the email.
    if (!await userManager.IsEmailConfirmedAsync(user))
    {
        var confirmToken = await userManager.GenerateEmailConfirmationTokenAsync(user);
        await userManager.ConfirmEmailAsync(user, confirmToken);
    }

    return Results.Redirect("/login?reset=1");
}).DisableAntiforgery().RequireRateLimiting("account");

app.MapGet("/account/confirm-email", async (
    string userId,
    string token,
    UserManager<ApplicationUser> userManager) =>
{
    if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(token))
    {
        return Results.Redirect($"/confirm-email?error={Uri.EscapeDataString("The confirmation link is invalid.")}");
    }

    var user = await userManager.FindByIdAsync(userId);
    if (user is null)
    {
        return Results.Redirect($"/confirm-email?error={Uri.EscapeDataString("The confirmation link is invalid.")}");
    }

    string decodedToken;
    try
    {
        decodedToken = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(token));
    }
    catch
    {
        return Results.Redirect($"/confirm-email?error={Uri.EscapeDataString("The confirmation token is invalid.")}");
    }

    var result = await userManager.ConfirmEmailAsync(user, decodedToken);
    if (!result.Succeeded)
    {
        var message = string.Join(" ", result.Errors.Select(e => e.Description));
        return Results.Redirect($"/confirm-email?error={Uri.EscapeDataString(message)}");
    }

    return Results.Redirect("/confirm-email?success=1");
});

app.MapPost("/account/resend-confirmation", async (
    [FromForm] ResendConfirmationRequest request,
    UserManager<ApplicationUser> userManager,
    IEmailSender emailSender,
    HttpContext http) =>
{
    var email = request.Email?.Trim();
    if (string.IsNullOrWhiteSpace(email))
    {
        return Results.Redirect($"/resend-confirmation?error={Uri.EscapeDataString("Email is required.")}");
    }

    var emailValidator = new EmailAddressAttribute();
    if (!emailValidator.IsValid(email))
    {
        return Results.Redirect($"/resend-confirmation?error={Uri.EscapeDataString("Please enter a valid email address.")}");
    }

    var user = await userManager.FindByEmailAsync(email);
    if (user is not null && !await userManager.IsEmailConfirmedAsync(user))
    {
        await SendEmailConfirmationAsync(userManager, emailSender, user, http);
    }

    // Always show success to prevent email enumeration.
    return Results.Redirect("/resend-confirmation?sent=1");
}).DisableAntiforgery().RequireRateLimiting("account");

app.MapPost("/account/email", async (
    [FromForm] UpdateEmailRequest request,
    UserManager<ApplicationUser> userManager,
    HttpContext http,
    IEmailSender emailSender) =>
{
    var email = request.Email?.Trim();
    if (string.IsNullOrWhiteSpace(email))
    {
        return Results.Redirect($"/account/email?error={Uri.EscapeDataString("Email is required.")}");
    }

    var emailValidator = new EmailAddressAttribute();
    if (!emailValidator.IsValid(email))
    {
        return Results.Redirect($"/account/email?error={Uri.EscapeDataString("Please enter a valid email address.")}");
    }

    var currentUser = await userManager.GetUserAsync(http.User);
    if (currentUser is null)
    {
        return Results.Redirect("/login");
    }

    var existing = await userManager.FindByEmailAsync(email);
    if (existing is not null && existing.Id != currentUser.Id)
    {
        return Results.Redirect($"/account/email?error={Uri.EscapeDataString("That email address is already in use.")}");
    }

    if (string.Equals(currentUser.Email, email, StringComparison.OrdinalIgnoreCase)
        && await userManager.IsEmailConfirmedAsync(currentUser))
    {
        return Results.Redirect("/account/email?updated=1");
    }

    var result = await userManager.SetEmailAsync(currentUser, email);
    if (!result.Succeeded)
    {
        var message = string.Join(" ", result.Errors.Select(e => e.Description));
        return Results.Redirect($"/account/email?error={Uri.EscapeDataString(message)}");
    }

    currentUser.EmailConfirmed = false;
    await userManager.UpdateAsync(currentUser);
    await SendEmailConfirmationAsync(userManager, emailSender, currentUser, http);

    return Results.Redirect("/account/email?updated=1&confirm=1");
}).RequireAuthorization();

app.MapPost("/account/display-name", async (
    [FromForm] UpdateDisplayNameRequest request,
    HttpContext http,
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    ApplicationDbContext db,
    BoardGameMondays.Core.BgmMemberDirectoryService members) =>
{
    var displayName = request.DisplayName?.Trim();
    if (string.IsNullOrWhiteSpace(displayName))
    {
        return Results.Redirect($"/account?displayNameError={Uri.EscapeDataString("Name is required.")}#account-profile");
    }

    if (displayName.Length > 80)
    {
        return Results.Redirect($"/account?displayNameError={Uri.EscapeDataString("Name is too long.")}#account-profile");
    }

    var currentUser = await userManager.GetUserAsync(http.User);
    if (currentUser is null)
    {
        return Results.Redirect("/login");
    }

    var claims = await userManager.GetClaimsAsync(currentUser);
    var existingDisplayName = claims.FirstOrDefault(c => c.Type == BgmClaimTypes.DisplayName)?.Value?.Trim();

    MemberEntity? member = null;
    var memberIdClaim = claims.FirstOrDefault(c => c.Type == BgmClaimTypes.MemberId)?.Value;
    if (Guid.TryParse(memberIdClaim, out var memberId))
    {
        member = await db.Members.FirstOrDefaultAsync(m => m.Id == memberId);
    }

    if (member is null && !string.IsNullOrWhiteSpace(existingDisplayName))
    {
        member = await db.Members.FirstOrDefaultAsync(m => m.Name.ToLower() == existingDisplayName.ToLower());
    }

    var nameMatch = await db.Members
        .AsNoTracking()
        .FirstOrDefaultAsync(m => m.Name.ToLower() == displayName.ToLower());

    if (nameMatch is not null && (member is null || nameMatch.Id != member.Id))
    {
        return Results.Redirect($"/account?displayNameError={Uri.EscapeDataString("That name is already in use.")}#account-profile");
    }

    if (member is null)
    {
        member = new MemberEntity
        {
            Id = Guid.NewGuid(),
            IsBgmMember = true,
            Name = displayName,
            Email = currentUser.Email ?? string.Empty
        };
        db.Members.Add(member);
    }
    else
    {
        member.Name = displayName;
    }

    await db.SaveChangesAsync();

    var existingClaim = claims.FirstOrDefault(c => c.Type == BgmClaimTypes.DisplayName);
    if (existingClaim is null)
    {
        await userManager.AddClaimAsync(currentUser, new Claim(BgmClaimTypes.DisplayName, displayName));
    }
    else if (!string.Equals(existingClaim.Value, displayName, StringComparison.Ordinal))
    {
        await userManager.ReplaceClaimAsync(currentUser, existingClaim, new Claim(BgmClaimTypes.DisplayName, displayName));
    }

    if (!Guid.TryParse(memberIdClaim, out _))
    {
        await userManager.AddClaimAsync(currentUser, new Claim(BgmClaimTypes.MemberId, member.Id.ToString()));
    }

    await signInManager.RefreshSignInAsync(currentUser);
    members.InvalidateCache();

    return Results.Redirect("/account?displayNameUpdated=1#account-profile");
}).RequireAuthorization().RequireRateLimiting("account");

app.MapPost("/account/resend-confirmation", async (
    UserManager<ApplicationUser> userManager,
    HttpContext http,
    IEmailSender emailSender) =>
{
    var currentUser = await userManager.GetUserAsync(http.User);
    if (currentUser is null)
    {
        return Results.Redirect("/login");
    }

    if (!string.IsNullOrWhiteSpace(currentUser.Email) && !await userManager.IsEmailConfirmedAsync(currentUser))
    {
        await SendEmailConfirmationAsync(userManager, emailSender, currentUser, http);
    }

    return Results.Redirect("/account/email?sent=1");
}).RequireAuthorization();

app.MapPost("/account/avatar", async (
    HttpContext http,
    IFormFile? avatar,
    UserManager<ApplicationUser> userManager,
    ApplicationDbContext db,
    IAssetStorage storage,
    ILoggerFactory loggerFactory) =>
{
    var logger = loggerFactory.CreateLogger("AvatarUpload");

    if (avatar is null || avatar.Length == 0)
    {
        return Results.Redirect($"/account?avatarError={Uri.EscapeDataString("Please choose an image.")}#account-profile");
    }

    const long maxBytes = 10 * 1024 * 1024; // 10MB
    if (avatar.Length > maxBytes)
    {
        return Results.Redirect($"/account?avatarError={Uri.EscapeDataString("Image must be 10MB or smaller.")}#account-profile");
    }

    if (!string.IsNullOrWhiteSpace(avatar.ContentType)
        && !avatar.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
    {
        return Results.Redirect($"/account?avatarError={Uri.EscapeDataString("That file doesn't look like an image.")}#account-profile");
    }

    string? extension;
    try
    {
        await using (var sniffStream = avatar.OpenReadStream())
        {
            extension = await ImageFileSniffer.DetectExtensionAsync(sniffStream);
        }
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Failed to read avatar upload stream.");
        return Results.Redirect($"/account?avatarError={Uri.EscapeDataString("Couldn't read that image. Try a different file.")}#account-profile");
    }

    if (string.IsNullOrWhiteSpace(extension))
    {
        return Results.Redirect($"/account?avatarError={Uri.EscapeDataString("Supported formats: JPG, PNG, WEBP, GIF.")}#account-profile");
    }

    if (string.Equals(extension, ".jpeg", StringComparison.OrdinalIgnoreCase))
    {
        extension = ".jpg";
    }

    var userName = http.User?.Identity?.Name;
    if (string.IsNullOrWhiteSpace(userName))
    {
        return Results.Redirect($"/account?avatarError={Uri.EscapeDataString("You must be logged in.")}#account-profile");
    }

    var user = await userManager.FindByNameAsync(userName);
    if (user is null)
    {
        return Results.Redirect($"/account?avatarError={Uri.EscapeDataString("You must be logged in.")}#account-profile");
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
            IsBgmMember = true,
            Name = displayName,
            Email = string.Empty
        };
        db.Members.Add(member);
        await db.SaveChangesAsync();
    }

    try
    {
        await using var avatarStream = avatar.OpenReadStream();
        member.AvatarUrl = await storage.SaveAvatarAsync(member.Id, avatarStream, extension);
        await db.SaveChangesAsync();
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Avatar upload failed for user '{UserName}'.", userName);
        return Results.Redirect($"/account?avatarError={Uri.EscapeDataString("Upload failed. Try a smaller image or a different format.")}#account-profile");
    }

    return Results.Redirect("/account?avatarUpdated=1#account-profile");
})
.RequireAuthorization()
.RequireRateLimiting("account");

// ============================================================================
// IMAGE MIGRATION ENDPOINT
// One-time admin endpoint to migrate images from local storage to Azure Blob.
// Usage: POST /api/migrate-images with JSON body: { "localAssetsFolder": "C:/path/to/downloaded/assets" }
// ============================================================================
app.MapPost("/api/migrate-images", async (
    [FromBody] MigrateImagesRequest request,
    ApplicationDbContext db,
    IOptions<StorageOptions> storageOptions,
    ILogger<Program> logger,
    ClaimsPrincipal user) =>
{
    if (!user.IsInRole("Admin"))
    {
        return Results.Forbid();
    }

    var blobOptions = storageOptions.Value.AzureBlob;
    if (string.IsNullOrWhiteSpace(blobOptions.ConnectionString))
    {
        return Results.BadRequest(new { error = "Azure Blob Storage connection string is not configured. Set Storage:AzureBlob:ConnectionString." });
    }

    if (string.IsNullOrWhiteSpace(request.LocalAssetsFolder) || !Directory.Exists(request.LocalAssetsFolder))
    {
        return Results.BadRequest(new { error = $"Local assets folder not found: {request.LocalAssetsFolder}" });
    }

    try
    {
        var tool = new BoardGameMondays.Tools.ImageMigrationTool(
            db,
            blobOptions.ConnectionString,
            blobOptions.ContainerName,
            blobOptions.BaseUrl);

        logger.LogInformation("Starting image migration from {Folder}", request.LocalAssetsFolder);
        var result = await tool.MigrateFromLocalFolderAsync(request.LocalAssetsFolder);
        logger.LogInformation("Image migration completed: {Success}/{Total} files migrated successfully",
            result.SuccessCount, result.TotalFiles);

        return Results.Ok(new
        {
            totalFiles = result.TotalFiles,
            successCount = result.SuccessCount,
            avatars = result.AvatarResults,
            gameImages = result.GameImageResults,
            blogImages = result.BlogImageResults
        });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Image migration failed");
        return Results.Problem($"Migration failed: {ex.Message}");
    }
})
.RequireAuthorization();

// For social crawlers (WhatsApp/Facebook/Slack/etc) request to `/rsvp` return a small
// HTML document with Open Graph meta tags so link previews show a proper image/title.
app.Use(async (context, next) =>
{
    if (context.Request.Path.Equals("/rsvp", StringComparison.OrdinalIgnoreCase))
    {
        var ua = context.Request.Headers["User-Agent"].ToString();
        if (ReturnUrlHelpers.IsSocialCrawler(ua))
        {
            // Build meta info based on query (date) if provided.
            var query = context.Request.Query;
            var dateRaw = query.TryGetValue("date", out var d) ? d.ToString() : string.Empty;
            var title = "Are you going to the upcoming Board Game Monday?";
            var baseUrl = $"{context.Request.Scheme}://{context.Request.Host}";
            var pageUrl = baseUrl + context.Request.Path + context.Request.QueryString;

            // Parse and format date if available
            string description;
            if (!string.IsNullOrWhiteSpace(dateRaw)
                && System.DateOnly.TryParseExact(dateRaw, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var parsedDate))
            {
                var friendly = parsedDate.ToString("dddd d MMMM yyyy", System.Globalization.CultureInfo.InvariantCulture);
                description = $"RSVP for {friendly}.";
            }
            else
            {
                description = "RSVP for the next Board Game Monday.";
            }

            // Use the Board Game Mondays logo as the share image. Add this file under wwwroot/images/logo-share.png
            var imageUrl = baseUrl + "/images/logo-share.png";
            var imageType = "image/png";
            var imageWidth = 1200;
            var imageHeight = 630;

            var html = $"<!doctype html><html><head>" +
                       $"<meta charset=\"utf-8\" />" +
                       $"<meta property=\"og:title\" content=\"{System.Text.Encodings.Web.HtmlEncoder.Default.Encode(title)}\" />" +
                       $"<meta property=\"og:description\" content=\"{System.Text.Encodings.Web.HtmlEncoder.Default.Encode(description)}\" />" +
                       $"<meta property=\"og:image\" content=\"{imageUrl}\" />" +
                       $"<meta property=\"og:image:secure_url\" content=\"{imageUrl}\" />" +
                       $"<meta property=\"og:image:type\" content=\"{imageType}\" />" +
                       $"<meta property=\"og:image:width\" content=\"{imageWidth}\" />" +
                       $"<meta property=\"og:image:height\" content=\"{imageHeight}\" />" +
                       $"<meta property=\"og:image:alt\" content=\"Board Game Mondays RSVP\" />" +
                       $"<meta property=\"og:url\" content=\"{pageUrl}\" />" +
                       $"<meta name=\"twitter:card\" content=\"summary_large_image\" />" +
                       $"<title>{System.Text.Encodings.Web.HtmlEncoder.Default.Encode(title)}</title>" +
                       $"</head><body>" +
                       $"<p><a href=\"{pageUrl}\">Open RSVP</a></p>" +
                       $"</body></html>";

            context.Response.ContentType = "text/html; charset=utf-8";
            await context.Response.WriteAsync(html);
            return;
        }
    }

    await next();
});

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapHub<GameNightHub>("/gameNightHub");

app.Run();

static async Task SendEmailConfirmationAsync(
    UserManager<ApplicationUser> userManager,
    IEmailSender emailSender,
    ApplicationUser user,
    HttpContext http)
{
    if (string.IsNullOrWhiteSpace(user.Email))
    {
        return;
    }

    var token = await userManager.GenerateEmailConfirmationTokenAsync(user);
    var encodedToken = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
    var callbackUrl = $"{http.Request.Scheme}://{http.Request.Host}/account/confirm-email?userId={Uri.EscapeDataString(user.Id)}&token={Uri.EscapeDataString(encodedToken)}";

    var body = $"<p>Please confirm your email address for Board Game Mondays.</p>" +
               $"<p><a href=\"{callbackUrl}\">Confirm email</a></p>" +
               $"<p>If you didn't request this, you can ignore this email.</p>";

    await emailSender.SendEmailAsync(user.Email, "Confirm your Board Game Mondays email", body);
}

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

internal sealed record RegisterRequest(string UserName, string DisplayName, string Email, string Password, string ConfirmPassword, string? ReturnUrl, bool AcceptTerms);
internal sealed class LoginRequest
{
    public string UserName { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public bool RememberMe { get; set; } = false;
    public string? ReturnUrl { get; set; }
}

internal sealed record ForgotPasswordRequest(string Email);
internal sealed record ResendConfirmationRequest(string Email);
internal sealed record ResetPasswordRequest(string Email, string Token, string Password, string ConfirmPassword);
internal sealed record UpdateEmailRequest(string Email);
internal sealed record UpdateDisplayNameRequest(string DisplayName);

internal static class BgmClaimTypes
{
    public const string DisplayName = "bgm:displayName";
    public const string MemberId = "bgm:memberId";
}

internal record MigrateImagesRequest(string LocalAssetsFolder);

internal static class ReturnUrlHelpers
{
    public static string? GetSafeReturnUrl(string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(returnUrl))
        {
            return null;
        }

        var trimmed = returnUrl.Trim();
        if (!trimmed.StartsWith("/", StringComparison.Ordinal)
            || trimmed.StartsWith("//", StringComparison.Ordinal)
            || trimmed.StartsWith("/\\", StringComparison.Ordinal))
        {
            return null;
        }

        if (trimmed.Contains("\r", StringComparison.Ordinal) || trimmed.Contains("\n", StringComparison.Ordinal))
        {
            return null;
        }

        return trimmed;
    }

    public static string AppendReturnUrl(string url, string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(returnUrl))
        {
            return url;
        }

        var separator = url.Contains('?') ? "&" : "?";
        return $"{url}{separator}returnUrl={Uri.EscapeDataString(returnUrl)}";
    }

    public static bool IsSocialCrawler(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent)) return false;
        var ua = userAgent.ToLowerInvariant();

        // Common social crawler identifiers
        return ua.Contains("facebookexternalhit")
            || ua.Contains("whatsapp")
            || ua.Contains("twitterbot")
            || ua.Contains("slackbot")
            || ua.Contains("linkedinbot")
            || ua.Contains("discord")
            || ua.Contains("telegrambot")
            || ua.Contains("applebot")
            || ua.Contains("pinterest")
            || ua.Contains("twitterpreview");
    }
}

