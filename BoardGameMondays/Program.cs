using BoardGameMondays.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;
using BoardGameMondays.Core;
using BoardGameMondays.Data;
using BoardGameMondays.Data.Entities;
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

var app = builder.Build();

// Initialize the dev database.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    await db.Database.EnsureCreatedAsync();

    await EnsureSqliteSchemaUpToDateAsync(db);
    
    await DataSeeder.SeedAsync(db);
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
        var hasTimesPlayed = false;

        await using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "PRAGMA table_info('Reviews');";
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                // PRAGMA table_info returns: cid, name, type, notnull, dflt_value, pk
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

        // Admin tickets + priorities.
        // Using CREATE TABLE IF NOT EXISTS keeps this safe for existing dev DBs created via EnsureCreated.
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
    IFormFile avatar,
    UserManager<ApplicationUser> userManager,
    ApplicationDbContext db,
    IWebHostEnvironment env) =>
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
        return Results.Redirect($"/?avatarError={Uri.EscapeDataString("Supported formats: JPG, PNG, WEBP, GIF.")}#people");
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

    return Results.Redirect("/?avatarUpdated=1#people");
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
