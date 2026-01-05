using BoardGameMondays.Data;
using Microsoft.EntityFrameworkCore;

namespace BoardGameMondays.Core;

public sealed class BgmCoinService
{
    private readonly ApplicationDbContext _db;

    public BgmCoinService(ApplicationDbContext db)
    {
        _db = db;
    }

    public event Action? Changed;

    public async Task<int?> GetCoinsAsync(string userId, CancellationToken ct = default)
        => await _db.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => (int?)u.BgmCoins)
            .FirstOrDefaultAsync(ct);

    public async Task<bool> TrySpendAsync(string userId, int amount, CancellationToken ct = default)
    {
        if (amount <= 0)
        {
            return false;
        }

        var affected = await _db.Users
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

        var affected = await _db.Users
            .Where(u => u.Id == userId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(u => u.BgmCoins, u => u.BgmCoins + amount), ct);

        if (affected > 0)
        {
            Changed?.Invoke();
            return true;
        }

        return false;
    }
}
