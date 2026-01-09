using BoardGameMondays.Data;
using BoardGameMondays.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace BoardGameMondays.Core;

public sealed class BettingService
{
    private readonly IDbContextFactory<ApplicationDbContext> _dbFactory;
    private readonly BgmCoinService _coins;

    public BettingService(IDbContextFactory<ApplicationDbContext> dbFactory, BgmCoinService coins)
    {
        _dbFactory = dbFactory;
        _coins = coins;
    }

    public async Task<IReadOnlyDictionary<int, UserBet>> GetUserBetsForNightAsync(Guid gameNightId, string userId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var bets = await db.GameNightGameBets
            .AsNoTracking()
            .Where(b => b.GameNightGame.GameNightId == gameNightId && b.UserId == userId)
            .Select(b => new
            {
                b.GameNightGameId,
                b.Amount,
                b.PredictedWinnerMemberId,
                WinnerName = b.PredictedWinnerMember.Name,
                b.OddsTimes100
            })
            .ToListAsync(ct);

        return bets.ToDictionary(
            x => x.GameNightGameId,
            x => new UserBet(x.Amount, x.PredictedWinnerMemberId, x.WinnerName, x.OddsTimes100));
    }

    public async Task<PlaceBetResult> PlaceBetAsync(Guid gameNightId, int gameNightGameId, Guid predictedWinnerMemberId, int amount, string userId, CancellationToken ct = default)
    {
        if (amount <= 0)
        {
            return PlaceBetResult.InvalidAmount;
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var game = await db.GameNightGames
            .AsNoTracking()
            .Include(g => g.GameNight)
            .FirstOrDefaultAsync(g => g.Id == gameNightGameId && g.GameNightId == gameNightId, ct);

        if (game is null)
        {
            return PlaceBetResult.NotFound;
        }

        if (!game.IsConfirmed)
        {
            return PlaceBetResult.NotConfirmed;
        }

        var gameDate = GameNightService.FromDateKey(game.GameNight.DateKey);
        if (gameDate < DateOnly.FromDateTime(DateTime.Today))
        {
            return PlaceBetResult.PastDate;
        }

        var already = await db.GameNightGameBets
            .AsNoTracking()
            .AnyAsync(b => b.GameNightGameId == gameNightGameId && b.UserId == userId, ct);

        if (already)
        {
            return PlaceBetResult.AlreadyBet;
        }

        var isPlayer = await db.GameNightGamePlayers
            .AsNoTracking()
            .AnyAsync(p => p.GameNightGameId == gameNightGameId && p.MemberId == predictedWinnerMemberId, ct);

        if (!isPlayer)
        {
            return PlaceBetResult.InvalidWinner;
        }

        var oddsTimes100 = await db.GameNightGameOdds
            .AsNoTracking()
            .Where(o => o.GameNightGameId == gameNightGameId && o.MemberId == predictedWinnerMemberId)
            .Select(o => (int?)o.OddsTimes100)
            .FirstOrDefaultAsync(ct);

        if (oddsTimes100 is null)
        {
            return PlaceBetResult.MissingOdds;
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var spent = await _coins.TrySpendAsync(db, userId, amount, ct);
        if (!spent)
        {
            return PlaceBetResult.NotEnoughCoins;
        }

        db.GameNightGameBets.Add(new GameNightGameBetEntity
        {
            GameNightGameId = gameNightGameId,
            UserId = userId,
            PredictedWinnerMemberId = predictedWinnerMemberId,
            Amount = amount,
            OddsTimes100 = oddsTimes100.Value,
            IsResolved = false,
            Payout = 0,
            ResolvedOn = null,
            CreatedOn = DateTimeOffset.UtcNow
        });

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return PlaceBetResult.Ok;
    }

    public async Task<ResolveResult> ResolveGameAsync(Guid gameNightId, int gameNightGameId, CancellationToken ct = default)
    {
        // Only resolve for past nights where a winner has been selected.
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var game = await db.GameNightGames
            .AsNoTracking()
            .Include(g => g.GameNight)
            .FirstOrDefaultAsync(g => g.Id == gameNightGameId && g.GameNightId == gameNightId, ct);

        if (game is null)
        {
            return ResolveResult.NotFound;
        }

        var gameDate = GameNightService.FromDateKey(game.GameNight.DateKey);
        if (gameDate >= DateOnly.FromDateTime(DateTime.Today))
        {
            return ResolveResult.NotPast;
        }

        var winnerHasTeam = !string.IsNullOrWhiteSpace(game.WinnerTeamName);
        if (!winnerHasTeam && game.WinnerMemberId is null)
        {
            return ResolveResult.MissingWinner;
        }

        // Fast path: nothing unresolved.
        var hasUnresolved = await db.GameNightGameBets
            .AsNoTracking()
            .AnyAsync(b => b.GameNightGameId == gameNightGameId && !b.IsResolved, ct);

        if (!hasUnresolved)
        {
            return ResolveResult.AlreadyResolved;
        }

        // Load unresolved bets to compute payouts.
        var unresolved = await db.GameNightGameBets
            .Where(b => b.GameNightGameId == gameNightGameId && !b.IsResolved)
            .ToListAsync(ct);

        if (unresolved.Count == 0)
        {
            return ResolveResult.AlreadyResolved;
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var winnerId = game.WinnerMemberId;
        Dictionary<Guid, string?> playerTeams = new();
        if (winnerHasTeam)
        {
            playerTeams = await db.GameNightGamePlayers
                .AsNoTracking()
                .Where(p => p.GameNightGameId == gameNightGameId)
                .ToDictionaryAsync(p => p.MemberId, p => p.TeamName, ct);
        }
        var userPayouts = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var bet in unresolved)
        {
            var payout = 0;
            var isWinningBet = false;

            if (winnerHasTeam)
            {
                if (playerTeams.TryGetValue(bet.PredictedWinnerMemberId, out var teamName)
                    && !string.IsNullOrWhiteSpace(teamName)
                    && string.Equals(teamName, game.WinnerTeamName, StringComparison.OrdinalIgnoreCase))
                {
                    isWinningBet = true;
                }
            }
            else if (winnerId is Guid memberWinner && bet.PredictedWinnerMemberId == memberWinner)
            {
                isWinningBet = true;
            }

            if (isWinningBet)
            {
                var profit = ComputeProfit(bet.Amount, bet.OddsTimes100);
                payout = bet.Amount + profit;

                if (userPayouts.TryGetValue(bet.UserId, out var existing))
                {
                    userPayouts[bet.UserId] = existing + payout;
                }
                else
                {
                    userPayouts[bet.UserId] = payout;
                }
            }

            bet.IsResolved = true;
            bet.Payout = payout;
            bet.ResolvedOn = DateTimeOffset.UtcNow;
        }

        foreach (var kvp in userPayouts)
        {
            await _coins.TryAddAsync(db, kvp.Key, kvp.Value, ct);
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return ResolveResult.Ok;
    }

    public async Task<IReadOnlyList<NightNetResult>> GetNightNetResultsAsync(Guid gameNightId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var nets = await db.GameNightGameBets
            .AsNoTracking()
            .Where(b => b.GameNightGame.GameNightId == gameNightId && b.IsResolved)
            .GroupBy(b => b.UserId)
            .Select(g => new
            {
                UserId = g.Key,
                Net = g.Sum(b => b.Payout - b.Amount)
            })
            .ToListAsync(ct);

        if (nets.Count == 0)
        {
            return Array.Empty<NightNetResult>();
        }

        var userIds = nets.Select(x => x.UserId).Distinct().ToHashSet(StringComparer.Ordinal);

        var userNames = await db.Users
            .AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.UserName })
            .ToListAsync(ct);

        var displayNameClaims = await db.UserClaims
            .AsNoTracking()
            .Where(c => userIds.Contains(c.UserId) && c.ClaimType == BgmClaimTypes.DisplayName)
            .Select(c => new { c.UserId, c.ClaimValue })
            .ToListAsync(ct);

        var displayNameByUserId = displayNameClaims
            .Where(c => !string.IsNullOrWhiteSpace(c.ClaimValue))
            .GroupBy(c => c.UserId)
            .ToDictionary(g => g.Key, g => g.First().ClaimValue!.Trim(), StringComparer.Ordinal);

        var userNameByUserId = userNames
            .ToDictionary(x => x.Id, x => x.UserName ?? x.Id, StringComparer.Ordinal);

        return nets
            .Select(x =>
            {
                var name = displayNameByUserId.TryGetValue(x.UserId, out var dn) ? dn : userNameByUserId.GetValueOrDefault(x.UserId, x.UserId);
                return new NightNetResult(x.UserId, name, x.Net);
            })
            .OrderByDescending(x => x.Net)
            .ToArray();
    }

    private static int ComputeProfit(int amount, int oddsTimes100)
    {
        // oddsTimes100 represents decimal odds (x100). Fractional profit odds are (decimal - 1).
        // Convert to reduced fraction and compute integer profit.
        if (oddsTimes100 <= 100)
        {
            return 0;
        }

        var numerator = oddsTimes100 - 100;
        var denominator = 100;
        var gcd = Gcd(numerator, denominator);
        numerator /= gcd;
        denominator /= gcd;
        return (amount * numerator) / denominator;
    }

    private static int Gcd(int a, int b)
    {
        a = Math.Abs(a);
        b = Math.Abs(b);
        while (b != 0)
        {
            var t = a % b;
            a = b;
            b = t;
        }
        return a == 0 ? 1 : a;
    }

    public enum PlaceBetResult
    {
        Ok,
        NotFound,
        NotConfirmed,
        PastDate,
        InvalidAmount,
        InvalidWinner,
        MissingOdds,
        AlreadyBet,
        NotEnoughCoins
    }

    public enum ResolveResult
    {
        Ok,
        NotFound,
        NotPast,
        MissingWinner,
        AlreadyResolved
    }

    public sealed record UserBet(int Amount, Guid WinnerMemberId, string WinnerMemberName, int OddsTimes100);

    public sealed record NightNetResult(string UserId, string DisplayName, int Net);
}
