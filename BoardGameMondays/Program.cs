using BoardGameMondays.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;
using BoardGameMondays.Core;
using BoardGameMondays.Data;
using BoardGameMondays.Data.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using System.Data;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

if (builder.Environment.IsDevelopment())
{
    builder.Services.Configure<CircuitOptions>(options => options.DetailedErrors = true);
}

// Database + Identity (dev: SQLite; prod later: swap provider/connection string).
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
    {
        // Dev-friendly defaults; tighten in production.
        options.Password.RequiredLength = 6;
        options.Password.RequireDigit = false;
        options.Password.RequireLowercase = false;
        options.Password.RequireUppercase = false;
        options.Password.RequireNonAlphanumeric = false;

        options.User.RequireUniqueEmail = false;
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/login";
    options.AccessDeniedPath = "/login";
    options.Cookie.Name = "bgm.auth";
    options.SlidingExpiration = true;
});

builder.Services.AddAuthorization();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<AuthenticationStateProvider, HttpContextAuthStateProvider>();
builder.Services.AddScoped<BoardGameMondays.Core.BgmMemberService>();
builder.Services.AddScoped<BoardGameMondays.Core.BgmMemberDirectoryService>();
builder.Services.AddScoped<BoardGameMondays.Core.BoardGameService>();
builder.Services.AddScoped<BoardGameMondays.Core.TicketService>();
builder.Services.AddScoped<BoardGameMondays.Core.AgreementService>();
builder.Services.AddScoped<BoardGameMondays.Core.GameNightService>();
builder.Services.AddScoped<BoardGameMondays.Core.BgmCoinService>();
builder.Services.AddScoped<BoardGameMondays.Core.BettingService>();
builder.Services.AddScoped<BoardGameMondays.Core.BlogService>();

var app = builder.Build();

// Initialize the dev database.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    await db.Database.EnsureCreatedAsync();

    await EnsureSqliteSchemaUpToDateAsync(db);
    
    await DataSeeder.SeedAsync(db);

    // Dev convenience: create a non-admin user for quickly checking the non-admin UX.
    if (app.Environment.IsDevelopment())
    {
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        await SeedDevNonAdminUserAsync(userManager, db);
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
    CreatedByUserId TEXT NULL
);
";
            await createTickets.ExecuteNonQueryAsync();
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
                    break;
                }
            }
        }

        if (!hasIsConfirmed)
        {
            await using var alter = connection.CreateCommand();
            alter.CommandText = "ALTER TABLE GameNightGames ADD COLUMN IsConfirmed INTEGER NOT NULL DEFAULT 0;";
            await alter.ExecuteNonQueryAsync();
        }

        var hasWinnerMemberId = false;
        await using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "PRAGMA table_info('GameNightGames');";
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var name = reader.GetString(1);
                if (string.Equals(name, "WinnerMemberId", StringComparison.OrdinalIgnoreCase))
                {
                    hasWinnerMemberId = true;
                    break;
                }
            }
        }

        if (!hasWinnerMemberId)
        {
            await using var alter = connection.CreateCommand();
            alter.CommandText = "ALTER TABLE GameNightGames ADD COLUMN WinnerMemberId TEXT NULL;";
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
    FOREIGN KEY (GameNightGameId) REFERENCES GameNightGames(Id) ON DELETE CASCADE,
    FOREIGN KEY (MemberId) REFERENCES Members(Id) ON DELETE CASCADE
);
";
            await createGameNightGamePlayers.ExecuteNonQueryAsync();
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

app.UseAuthentication();
app.UseAuthorization();

app.UseAntiforgery();

app.MapStaticAssets();

app.MapPost("/account/logout", async (SignInManager<ApplicationUser> signInManager, HttpContext http) =>
{
    await signInManager.SignOutAsync();
    http.Response.Redirect("/");
}).DisableAntiforgery();

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

    if (string.IsNullOrWhiteSpace(displayName))
    {
        return Results.Redirect($"/register?error={Uri.EscapeDataString("Name is required.")}");
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
}).DisableAntiforgery();

app.MapPost("/account/login", async (
    [FromForm] LoginRequest request,
    SignInManager<ApplicationUser> signInManager,
    UserManager<ApplicationUser> userManager,
    BoardGameMondays.Core.BgmMemberDirectoryService members) =>
{
    var userName = request.UserName?.Trim();
    if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(request.Password))
    {
        return Results.Redirect($"/login?error={Uri.EscapeDataString("Username and password are required.")}");
    }

    var result = await signInManager.PasswordSignInAsync(userName, request.Password, request.RememberMe, lockoutOnFailure: false);
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
}).DisableAntiforgery();

app.MapPost("/account/avatar", async (
    HttpContext http,
    IFormFile? avatar,
    UserManager<ApplicationUser> userManager,
    ApplicationDbContext db,
    IWebHostEnvironment env) =>
{
    if (avatar is null || avatar.Length == 0)
    {
        return Results.Redirect($"/people?avatarError={Uri.EscapeDataString("Please choose an image.")}");
    }

    const long maxBytes = 2 * 1024 * 1024; // 2MB
    if (avatar.Length > maxBytes)
    {
        return Results.Redirect($"/people?avatarError={Uri.EscapeDataString("Image must be 2MB or smaller.")}");
    }

    var contentType = avatar.ContentType?.ToLowerInvariant();
    var extension = contentType switch
    {
        "image/jpeg" => ".jpg",
        "image/jpg" => ".jpg",
        "image/png" => ".png",
        "image/webp" => ".webp",
        "image/gif" => ".gif",
        _ => null
    };

    if (extension is null)
    {
        return Results.Redirect($"/people?avatarError={Uri.EscapeDataString("Supported formats: JPG, PNG, WEBP, GIF.")}");
    }

    var userName = http.User?.Identity?.Name;
    if (string.IsNullOrWhiteSpace(userName))
    {
        return Results.Redirect($"/people?avatarError={Uri.EscapeDataString("You must be logged in.")}");
    }

    var user = await userManager.FindByNameAsync(userName);
    if (user is null)
    {
        return Results.Redirect($"/people?avatarError={Uri.EscapeDataString("You must be logged in.")}");
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

    var uploadsRoot = Path.Combine(env.WebRootPath, "uploads", "avatars");
    Directory.CreateDirectory(uploadsRoot);

    var fileName = $"{member.Id}{extension}";
    var filePath = Path.Combine(uploadsRoot, fileName);
    await using (var stream = File.Create(filePath))
    {
        await avatar.CopyToAsync(stream);
    }

    // Add a cache-buster so browsers refresh after re-upload.
    member.AvatarUrl = $"/uploads/avatars/{fileName}?v={DateTimeOffset.UtcNow.UtcTicks}";
    await db.SaveChangesAsync();

    return Results.Redirect("/people?avatarUpdated=1");
}).RequireAuthorization().DisableAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

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
