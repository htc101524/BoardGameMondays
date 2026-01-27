using BoardGameMondays.Data;
using BoardGameMondays.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace BoardGameMondays.Core;

public sealed class BettingService
{
    private readonly IDbContextFactory<ApplicationDbContext> _dbFactory;
    private readonly BgmCoinService _coins;
    private readonly RankingService _ranking;
    private readonly OddsService _odds;

    public BettingService(
        IDbContextFactory<ApplicationDbContext> dbFactory,
        BgmCoinService coins,
        RankingService ranking,
        OddsService odds)
    {
        _dbFactory = dbFactory;
        _coins = coins;
        _ranking = ranking;
        _odds = odds;
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

        var strategy = db.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            var spent = await _coins.TrySpendAsync(db, userId, amount, ct);
            if (!spent)
            {
                await tx.RollbackAsync(ct);
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

            // Recalculate odds based on cashflow within the same transaction
            await _odds.RecalculateOddsForCashflowAsync(db, gameNightGameId, ct);

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            return PlaceBetResult.Ok;
        });
    }

    public async Task<ResolveResult> ResolveGameAsync(Guid gameNightId, int gameNightGameId, CancellationToken ct = default)
    {
        // Only resolve for past nights. A winner may be unset (no winner); in that case all bets are lost.
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
        // If no winner is selected (neither team nor member), we still resolve: nobody wins.

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

        // Use execution strategy for Azure SQL retry logic - wrap entire transaction in ExecuteAsync.
        var strategy = db.Database.CreateExecutionStrategy();
        var winnerId = game.WinnerMemberId;

        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);

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
        });

        // Update ELO rankings based on the game outcome
        await UpdateRankingsForGameAsync(gameNightGameId, game.WinnerMemberId, game.WinnerTeamName, ct);

        return ResolveResult.Ok;
    }

    /// <summary>
    /// Updates ELO rankings for all players in a game based on the outcome.
    /// </summary>
    private async Task UpdateRankingsForGameAsync(
        int gameNightGameId,
        Guid? winnerMemberId,
        string? winnerTeamName,
        CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var players = await db.GameNightGamePlayers
            .AsNoTracking()
            .Where(p => p.GameNightGameId == gameNightGameId)
            .Select(p => new { p.MemberId, p.TeamName })
            .ToListAsync(ct);

        if (players.Count < 2)
        {
            return; // Need at least 2 players for rankings
        }

        // If there's no winner (co-op game lost), all players lose a bit of ELO (for fun)
        if (winnerMemberId is null && string.IsNullOrWhiteSpace(winnerTeamName))
        {
            var allPlayerIds = players.Select(p => p.MemberId).ToList();
            await _ranking.UpdateRatingsForNoWinnerGameAsync(allPlayerIds, ct);
            return;
        }

        var isTeamGame = !string.IsNullOrWhiteSpace(winnerTeamName);

        if (isTeamGame)
        {
            // Group players by team
            var teams = players
                .Where(p => !string.IsNullOrWhiteSpace(p.TeamName))
                .GroupBy(p => p.TeamName!)
                .ToList();

            if (teams.Count < 2)
            {
                return;
            }

            var winningTeamMembers = teams
                .FirstOrDefault(t => string.Equals(t.Key, winnerTeamName, StringComparison.OrdinalIgnoreCase))
                ?.Select(p => p.MemberId)
                .ToList();

            if (winningTeamMembers is null || winningTeamMembers.Count == 0)
            {
                return;
            }

            var losingTeams = teams
                .Where(t => !string.Equals(t.Key, winnerTeamName, StringComparison.OrdinalIgnoreCase))
                .Select(t => (IReadOnlyList<Guid>)t.Select(p => p.MemberId).ToList())
                .ToList();

            await _ranking.UpdateRatingsForTeamGameAsync(winningTeamMembers, losingTeams, ct);
        }
        else if (winnerMemberId is Guid winnerId)
        {
            // Individual game
            var loserIds = players
                .Where(p => p.MemberId != winnerId)
                .Select(p => p.MemberId)
                .ToList();

            await _ranking.UpdateRatingsForIndividualGameAsync(winnerId, loserIds, ct);
        }
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

    public async Task<IReadOnlyList<UserBetHistory>> GetUserBetHistoryAsync(string userId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var bets = await db.GameNightGameBets
            .AsNoTracking()
            .Where(b => b.UserId == userId)
            .OrderByDescending(b => b.CreatedOn)
            .Select(b => new
            {
                b.GameNightGameId,
                b.Amount,
                b.OddsTimes100,
                b.IsResolved,
                b.Payout,
                b.CreatedOn,
                b.ResolvedOn,
                WinnerName = b.PredictedWinnerMember.Name,
                GameName = b.GameNightGame.Game.Name,
                GameDateKey = b.GameNightGame.GameNight.DateKey,
                ActualWinnerMemberName = b.GameNightGame.WinnerMember != null ? b.GameNightGame.WinnerMember.Name : null,
                b.GameNightGame.WinnerTeamName
            })
            .ToListAsync(ct);

        return bets
            .Select(b => new UserBetHistory(
                b.GameNightGameId,
                GameNightService.FromDateKey(b.GameDateKey),
                b.GameName,
                b.WinnerName,
                b.Amount,
                b.OddsTimes100,
                b.IsResolved,
                b.Payout,
                b.CreatedOn,
                b.ResolvedOn,
                b.ActualWinnerMemberName,
                b.WinnerTeamName))
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

    public enum CancelBetsResult
    {
        Ok,
        NotFound,
        AlreadyResolved
    }

    /// <summary>
    /// Cancels all unresolved bets for a game and refunds the bet amounts to each user.
    /// This is used when a confirmed game is being removed before a winner is set.
    /// </summary>
    public async Task<CancelBetsResult> CancelGameBetsAsync(int gameNightGameId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var gameExists = await db.GameNightGames
            .AsNoTracking()
            .AnyAsync(g => g.Id == gameNightGameId, ct);

        if (!gameExists)
        {
            return CancelBetsResult.NotFound;
        }

        // Check if any bets have already been resolved (winner was set and paid out).
        var hasResolved = await db.GameNightGameBets
            .AsNoTracking()
            .AnyAsync(b => b.GameNightGameId == gameNightGameId && b.IsResolved, ct);

        if (hasResolved)
        {
            return CancelBetsResult.AlreadyResolved;
        }

        // Load all unresolved bets to refund.
        var bets = await db.GameNightGameBets
            .Where(b => b.GameNightGameId == gameNightGameId && !b.IsResolved)
            .ToListAsync(ct);

        if (bets.Count == 0)
        {
            return CancelBetsResult.Ok;
        }

        var strategy = db.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            // Refund each user's bet amount.
            foreach (var bet in bets)
            {
                await _coins.TryAddAsync(db, bet.UserId, bet.Amount, ct);
            }

            // Remove all the bets.
            db.GameNightGameBets.RemoveRange(bets);

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            return CancelBetsResult.Ok;
        });
    }

    /// <summary>
    /// Cancels all unresolved bets for a game and refunds the bet amounts to each user.
    /// This overload uses the provided DbContext to participate in an external transaction.
    /// The caller is responsible for committing the transaction.
    /// </summary>
    public async Task<CancelBetsResult> CancelGameBetsAsync(ApplicationDbContext db, int gameNightGameId, CancellationToken ct = default)
    {
        // Check if any bets have already been resolved (winner was set and paid out).
        var hasResolved = await db.GameNightGameBets
            .AsNoTracking()
            .AnyAsync(b => b.GameNightGameId == gameNightGameId && b.IsResolved, ct);

        if (hasResolved)
        {
            return CancelBetsResult.AlreadyResolved;
        }

        // Load all unresolved bets to refund.
        var bets = await db.GameNightGameBets
            .Where(b => b.GameNightGameId == gameNightGameId && !b.IsResolved)
            .ToListAsync(ct);

        if (bets.Count == 0)
        {
            return CancelBetsResult.Ok;
        }

        // Refund each user's bet amount.
        foreach (var bet in bets)
        {
            await _coins.TryAddAsync(db, bet.UserId, bet.Amount, ct);
        }

        // Remove all the bets.
        db.GameNightGameBets.RemoveRange(bets);

        return CancelBetsResult.Ok;
    }

    public sealed record UserBet(int Amount, Guid WinnerMemberId, string WinnerMemberName, int OddsTimes100);

    public sealed record UserBetHistory(
        int GameNightGameId,
        DateOnly GameDate,
        string GameName,
        string PredictedWinnerName,
        int Amount,
        int OddsTimes100,
        bool IsResolved,
        int Payout,
        DateTimeOffset CreatedOn,
        DateTimeOffset? ResolvedOn,
        string? ActualWinnerMemberName,
        string? ActualWinnerTeamName);

    public sealed record NightNetResult(string UserId, string DisplayName, int Net);
}
