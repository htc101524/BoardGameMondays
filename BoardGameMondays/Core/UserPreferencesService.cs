using BoardGameMondays.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace BoardGameMondays.Core;

/// <summary>
/// Service for managing user preferences.
/// </summary>
public sealed class UserPreferencesService
{
    private readonly IDbContextFactory<ApplicationDbContext> _dbFactory;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IMemoryCache _cache;

    // Cache key prefix
    private const string OddsFormatCacheKeyPrefix = "UserPrefs:OddsFormat:";

    // User preferences can be cached longer since they rarely change
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(30);

    public UserPreferencesService(
        IDbContextFactory<ApplicationDbContext> dbFactory,
        UserManager<ApplicationUser> userManager,
        IMemoryCache cache)
    {
        _dbFactory = dbFactory;
        _userManager = userManager;
        _cache = cache;
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

        var cacheKey = $"{OddsFormatCacheKeyPrefix}{userId}";
        
        // Use TryGetValue pattern for value types
        if (_cache.TryGetValue(cacheKey, out OddsDisplayFormat cached))
        {
            return cached;
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var user = await db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId, ct);

        var result = user?.OddsDisplayFormat ?? OddsDisplayFormat.Fraction;
        _cache.Set(cacheKey, result, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = CacheDuration,
            Size = 1 // Single enum value
        });
        return result;
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
        
        // Invalidate the cache for this user
        _cache.Remove($"{OddsFormatCacheKeyPrefix}{userId}");
        
        return true;
    }
}
