using BoardGameMondays.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;
using BoardGameMondays.Core;
using BoardGameMondays.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;

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

var app = builder.Build();

// Initialize the dev database.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    await db.Database.EnsureCreatedAsync();
    
    await DataSeeder.SeedAsync(db);
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
    if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(request.Password))
    {
        return Results.Redirect($"/register?error={Uri.EscapeDataString("Username and password are required.")}");
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
    members.GetOrCreate(userName);
    return Results.Redirect("/");
}).DisableAntiforgery();

app.MapPost("/account/login", async (
    [FromForm] LoginRequest request,
    SignInManager<ApplicationUser> signInManager,
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

    members.GetOrCreate(userName);
    return Results.Redirect("/");
}).DisableAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

internal sealed record RegisterRequest(string UserName, string Password, string ConfirmPassword);
internal sealed record LoginRequest(string UserName, string Password, bool RememberMe);
