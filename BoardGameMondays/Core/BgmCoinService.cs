using BoardGameMondays.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace BoardGameMondays.Core;

public sealed class BgmCoinService
{
    private const int MondayAttendanceCoinsPerWeek = 10;

    private readonly IDbContextFactory<ApplicationDbContext> _dbFactory;
    private readonly IConfiguration _configuration;

    public BgmCoinService(IDbContextFactory<ApplicationDbContext> dbFactory, IConfiguration configuration)
    {
        _dbFactory = dbFactory;
        _configuration = configuration;
    }

    public event Action? Changed;

    public async Task<int> GetPendingMondayAttendanceCoinsAsync(string userId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return 0;
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var memberId = await GetMemberIdForUserAsync(db, userId, ct);
        if (memberId is null)
        {
            return 0;
        }

        var member = await db.Members
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == memberId.Value, ct);

        if (member is null)
        {
            return 0;
        }

        var dateKeys = await db.GameNightAttendees
            .AsNoTracking()
            .Where(a => a.MemberId == memberId.Value)
            .Select(a => a.GameNight.DateKey)
            .ToListAsync(ct);

        var summary = SummarizeAttendanceWeeks(dateKeys, member.LastMondayCoinsClaimedDateKey);
        if (summary.Weeks == 0)
        {
            return 0;
        }

        return summary.Weeks * MondayAttendanceCoinsPerWeek;
    }

    public async Task<int> ClaimMondayAttendanceCoinsAsync(string userId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return 0;
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var memberId = await GetMemberIdForUserAsync(db, userId, ct);
        if (memberId is null)
        {
            return 0;
        }

        var member = await db.Members
            .FirstOrDefaultAsync(m => m.Id == memberId.Value, ct);

        if (member is null)
        {
            return 0;
        }

        var dateKeys = await db.GameNightAttendees
            .AsNoTracking()
            .Where(a => a.MemberId == memberId.Value)
            .Select(a => a.GameNight.DateKey)
            .ToListAsync(ct);

        var summary = SummarizeAttendanceWeeks(dateKeys, member.LastMondayCoinsClaimedDateKey);
        if (summary.Weeks == 0 || summary.LatestWeekKey is null)
        {
            return 0;
        }

        var coinsToAdd = summary.Weeks * MondayAttendanceCoinsPerWeek;
        var added = await TryAddAsync(db, userId, coinsToAdd, ct);
        if (!added)
        {
            return 0;
        }

        member.LastMondayCoinsClaimedDateKey = summary.LatestWeekKey;
        await db.SaveChangesAsync(ct);
        return coinsToAdd;
    }

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
        var success = await TrySpendAsync(db, userId, amount, ct);
        if (success)
        {
            await db.SaveChangesAsync(ct);
        }

        return success;
    }

    public async Task<bool> TrySpendAsync(ApplicationDbContext db, string userId, int amount, CancellationToken ct = default)
    {
        if (amount <= 0)
        {
            return false;
        }

        // When called with an external DbContext (likely inside a transaction),
        // use traditional tracking to avoid ExecuteUpdateAsync which conflicts
        // with SQL Server's retry execution strategy inside user transactions.
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId && u.BgmCoins >= amount, ct);
        if (user is null)
        {
            return false;
        }

        user.BgmCoins -= amount;
        Changed?.Invoke();
        return true;
    }

    public async Task<bool> TryAddAsync(string userId, int amount, CancellationToken ct = default)
    {
        if (amount <= 0)
        {
            return false;
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var success = await TryAddAsync(db, userId, amount, ct);
        if (success)
        {
            await db.SaveChangesAsync(ct);
        }

        return success;
    }

    public async Task<bool> TryAddAsync(ApplicationDbContext db, string userId, int amount, CancellationToken ct = default)
    {
        if (amount <= 0)
        {
            return false;
        }

        // When called with an external DbContext (likely inside a transaction),
        // use traditional tracking to avoid ExecuteUpdateAsync which conflicts
        // with SQL Server's retry execution strategy inside user transactions.
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null)
        {
            return false;
        }

        user.BgmCoins += amount;
        Changed?.Invoke();
        return true;
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

    public async Task<IReadOnlyList<FullLeaderboardItem>> GetFullLeaderboardAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var users = await db.Users
            .AsNoTracking()
            .OrderByDescending(u => u.BgmCoins)
            .ThenBy(u => u.UserName)
            .Select(u => new { u.Id, u.UserName, u.BgmCoins })
            .ToListAsync(ct);

        if (users.Count == 0)
        {
            return Array.Empty<FullLeaderboardItem>();
        }

        var userIds = users.Select(u => u.Id).ToHashSet(StringComparer.Ordinal);

        // Get display names
        var displayNameClaims = await db.UserClaims
            .AsNoTracking()
            .Where(c => userIds.Contains(c.UserId) && c.ClaimType == BgmClaimTypes.DisplayName)
            .Select(c => new { c.UserId, c.ClaimValue })
            .ToListAsync(ct);

        var displayNameByUserId = displayNameClaims
            .Where(c => !string.IsNullOrWhiteSpace(c.ClaimValue))
            .GroupBy(c => c.UserId)
            .ToDictionary(g => g.Key, g => g.First().ClaimValue!.Trim(), StringComparer.Ordinal);

        // Determine which users are BGM members (admins)
        var adminUserIds = await GetAdminUserIdsAsync(db, ct);

        return users
            .Select(u => new FullLeaderboardItem(
                u.Id,
                displayNameByUserId.TryGetValue(u.Id, out var dn) ? dn : (u.UserName ?? u.Id),
                u.BgmCoins,
                adminUserIds.Contains(u.Id)))
            .ToArray();
    }

    private async Task<HashSet<string>> GetAdminUserIdsAsync(ApplicationDbContext db, CancellationToken ct)
    {
        // Admins are defined via configuration (see AdminRoleClaimsTransformation). In development,
        // the role may also be persisted via Identity; we fall back to DB roles if no config is set.
        var configuredUserNames = _configuration.GetSection("Security:Admins:UserNames").Get<string[]>() ?? [];
        var normalizedConfigured = configuredUserNames
            .Select(x => x?.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!.ToUpperInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToList();

        HashSet<string> adminUserIds;
        if (normalizedConfigured.Count > 0)
        {
            var ids = await db.Users
                .AsNoTracking()
                .Where(u => u.NormalizedUserName != null && normalizedConfigured.Contains(u.NormalizedUserName))
                .Select(u => u.Id)
                .ToListAsync(ct);
            adminUserIds = ids.ToHashSet(StringComparer.Ordinal);
        }
        else
        {
            var adminRoleId = await db.Roles
                .AsNoTracking()
                .Where(r => r.Name == "Admin")
                .Select(r => r.Id)
                .FirstOrDefaultAsync(ct);

            if (string.IsNullOrWhiteSpace(adminRoleId))
            {
                adminUserIds = new HashSet<string>(StringComparer.Ordinal);
            }
            else
            {
                var ids = await db.UserRoles
                    .AsNoTracking()
                    .Where(ur => ur.RoleId == adminRoleId)
                    .Select(ur => ur.UserId)
                    .Distinct()
                    .ToListAsync(ct);
                adminUserIds = ids.ToHashSet(StringComparer.Ordinal);
            }
        }

        return adminUserIds;
    }

    public async Task<IReadOnlyDictionary<string, int>> GetCoinsGainedSinceAsync(DateTimeOffset since, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        // Sum payouts from bets resolved since the given date
        var payoutsByUser = await db.GameNightGameBets
            .AsNoTracking()
            .Where(b => b.IsResolved && b.ResolvedOn >= since)
            .GroupBy(b => b.UserId)
            .Select(g => new { UserId = g.Key, TotalPayout = g.Sum(b => b.Payout), TotalStake = g.Sum(b => b.Amount) })
            .ToListAsync(ct);

        return payoutsByUser.ToDictionary(
            x => x.UserId,
            x => x.TotalPayout - x.TotalStake,
            StringComparer.Ordinal);
    }

    public async Task<int> GetHouseNetSinceAsync(DateTimeOffset? since, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var query = db.GameNightGameBets
            .AsNoTracking()
            .Where(b => b.IsResolved);

        if (since.HasValue)
        {
            query = query.Where(b => b.ResolvedOn >= since.Value);
        }

        return await query
            .Select(b => b.Amount - b.Payout)
            .SumAsync(ct);
    }

    public sealed record CoinLeaderboardItem(string UserId, string DisplayName, int Coins);
    public sealed record FullLeaderboardItem(string UserId, string DisplayName, int Coins, bool IsBgmMember);

    private static AttendanceWeekSummary SummarizeAttendanceWeeks(IEnumerable<int> dateKeys, int? lastClaimedWeekKey)
    {
        var weekKeys = new HashSet<int>();

        foreach (var dateKey in dateKeys)
        {
            var weekKey = WantToPlayService.GetWeekKey(GameNightService.FromDateKey(dateKey));
            if (lastClaimedWeekKey.HasValue && weekKey <= lastClaimedWeekKey.Value)
            {
                continue;
            }

            weekKeys.Add(weekKey);
        }

        if (weekKeys.Count == 0)
        {
            return new AttendanceWeekSummary(0, null);
        }

        return new AttendanceWeekSummary(weekKeys.Count, weekKeys.Max());
    }

    private static async Task<Guid?> GetMemberIdForUserAsync(ApplicationDbContext db, string userId, CancellationToken ct)
    {
        var memberIdValue = await db.UserClaims
            .AsNoTracking()
            .Where(c => c.UserId == userId && c.ClaimType == BgmClaimTypes.MemberId)
            .Select(c => c.ClaimValue)
            .FirstOrDefaultAsync(ct);

        return Guid.TryParse(memberIdValue, out var memberId) ? memberId : null;
    }

    private sealed record AttendanceWeekSummary(int Weeks, int? LatestWeekKey);
}