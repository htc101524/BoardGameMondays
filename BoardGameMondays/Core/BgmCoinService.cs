using BoardGameMondays.Data;
using Microsoft.EntityFrameworkCore;

namespace BoardGameMondays.Core;

public sealed class BgmCoinService
{
    private readonly IDbContextFactory<ApplicationDbContext> _dbFactory;

    public BgmCoinService(IDbContextFactory<ApplicationDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public event Action? Changed;

    public async Task<int?> GetCoinsAsync(string userId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => (int?)u.BgmCoins)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<bool> TrySpendAsync(string userId, int amount, CancellationToken ct = default)
    {
        if (amount <= 0)
        {
            return false;
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await TrySpendAsync(db, userId, amount, ct);
    }

    public async Task<bool> TrySpendAsync(ApplicationDbContext db, string userId, int amount, CancellationToken ct = default)
    {
        if (amount <= 0)
        {
            return false;
        }

        var affected = await db.Users
            .Where(u => u.Id == userId && u.BgmCoins >= amount)
            .ExecuteUpdateAsync(setters => setters.SetProperty(u => u.BgmCoins, u => u.BgmCoins - amount), ct);

        if (affected > 0)
        {
            Changed?.Invoke();
            return true;
        }

        return false;
    }

    public async Task<bool> TryAddAsync(string userId, int amount, CancellationToken ct = default)
    {
        if (amount <= 0)
        {
            return false;
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await TryAddAsync(db, userId, amount, ct);
    }

    public async Task<bool> TryAddAsync(ApplicationDbContext db, string userId, int amount, CancellationToken ct = default)
    {
        if (amount <= 0)
        {
            return false;
        }

        var affected = await db.Users
            .Where(u => u.Id == userId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(u => u.BgmCoins, u => u.BgmCoins + amount), ct);

        if (affected > 0)
        {
            Changed?.Invoke();
            return true;
        }

        return false;
    }

    public async Task<IReadOnlyList<CoinLeaderboardItem>> GetLeaderboardAsync(int take, CancellationToken ct = default)
    {
        if (take <= 0)
        {
            return Array.Empty<CoinLeaderboardItem>();
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var topUsers = await db.Users
            .AsNoTracking()
            .OrderByDescending(u => u.BgmCoins)
            .ThenBy(u => u.UserName)
            .Select(u => new { u.Id, u.UserName, u.BgmCoins })
            .Take(take)
            .ToListAsync(ct);

        if (topUsers.Count == 0)
        {
            return Array.Empty<CoinLeaderboardItem>();
        }

        var userIds = topUsers.Select(u => u.Id).ToHashSet(StringComparer.Ordinal);

        var displayNameClaims = await db.UserClaims
            .AsNoTracking()
            .Where(c => userIds.Contains(c.UserId) && c.ClaimType == BgmClaimTypes.DisplayName)
            .Select(c => new { c.UserId, c.ClaimValue })
            .ToListAsync(ct);

        var displayNameByUserId = displayNameClaims
            .Where(c => !string.IsNullOrWhiteSpace(c.ClaimValue))
            .GroupBy(c => c.UserId)
            .ToDictionary(g => g.Key, g => g.First().ClaimValue!.Trim(), StringComparer.Ordinal);

        return topUsers
            .Select(u => new CoinLeaderboardItem(
                u.Id,
                displayNameByUserId.TryGetValue(u.Id, out var dn) ? dn : (u.UserName ?? u.Id),
                u.BgmCoins))
            .ToArray();
    }

    public sealed record CoinLeaderboardItem(string UserId, string DisplayName, int Coins);
}
