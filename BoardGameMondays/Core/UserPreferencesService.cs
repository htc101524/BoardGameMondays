using BoardGameMondays.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace BoardGameMondays.Core;

/// <summary>
/// Service for managing user preferences.
/// </summary>
public sealed class UserPreferencesService
{
    private readonly IDbContextFactory<ApplicationDbContext> _dbFactory;
    private readonly UserManager<ApplicationUser> _userManager;

    public UserPreferencesService(
        IDbContextFactory<ApplicationDbContext> dbFactory,
        UserManager<ApplicationUser> userManager)
    {
        _dbFactory = dbFactory;
        _userManager = userManager;
    }

    /// <summary>
    /// Gets the odds display format preference for a user.
    /// </summary>
    public async Task<OddsDisplayFormat> GetOddsDisplayFormatAsync(string? userId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return OddsDisplayFormat.Fraction;
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var user = await db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId, ct);

        return user?.OddsDisplayFormat ?? OddsDisplayFormat.Fraction;
    }

    /// <summary>
    /// Sets the odds display format preference for a user.
    /// </summary>
    public async Task<bool> SetOddsDisplayFormatAsync(string userId, OddsDisplayFormat format, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return false;
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);

        if (user is null)
        {
            return false;
        }

        user.OddsDisplayFormat = format;
        await db.SaveChangesAsync(ct);
        return true;
    }
}
