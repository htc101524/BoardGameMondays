using BoardGameMondays.Data;
using BoardGameMondays.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace BoardGameMondays.Core;

/// <summary>
/// Calculates and manages betting odds based on member ratings and bet cashflows.
/// </summary>
public sealed class OddsService
{
    private readonly IDbContextFactory<ApplicationDbContext> _dbFactory;
    private readonly RankingService _rankingService;
    private readonly Random _random = new();

    /// <summary>
    /// Minimum odds (decimal odds x100). 1.05 = 105 means 5% profit if they win.
    /// </summary>
    private const int MinOddsTimes100 = 105;

    /// <summary>
    /// Maximum odds (decimal odds x100). 20.00 = 2000 means 19x profit.
    /// </summary>
    private const int MaxOddsTimes100 = 2000;

    /// <summary>
    /// How much randomness to add to initial odds (+/- this percentage of the base odds).
    /// 0.08 = 8% variance.
    /// </summary>
    private const double InitialOddsRandomnessFactor = 0.08;

    /// <summary>
    /// How aggressively to adjust odds based on cashflow imbalance.
    /// Lower = more generous (less movement). We use a small factor for fun.
    /// </summary>
    private const double CashflowAdjustmentFactor = 0.15;

    /// <summary>
    /// The "margin" we apply - how generous we are. 1.0 = fair odds, lower = more generous.
    /// 0.92 means we're giving 8% more generous odds than mathematically fair.
    /// </summary>
    private const double GenerosityFactor = 0.92;

    /// <summary>
    /// Standard appealing betting fractions (as decimal odds x100), sorted.
    /// These produce nice fractions like 1/5, 2/5, 1/2, 4/5, 1/1, 6/5, 5/4, 11/8, 6/4, 13/8, 7/4, 15/8, 2/1, 9/4, 5/2, 11/4, 3/1, 7/2, 4/1, 9/2, 5/1, 6/1, 7/1, 8/1, 9/1, 10/1, 12/1, 14/1, 16/1, 20/1
    /// </summary>
    private static readonly int[] AppealingOdds = new[]
    {
        110,  // 1/10
        115,  // 3/20
        120,  // 1/5
        125,  // 1/4
        130,  // 3/10
        140,  // 2/5
        150,  // 1/2
        160,  // 3/5
        170,  // 7/10
        180,  // 4/5
        190,  // 9/10
        200,  // 1/1 (evens)
        210,  // 11/10
        220,  // 6/5
        225,  // 5/4
        240,  // 7/5
        250,  // 6/4 (3/2)
        275,  // 7/4
        300,  // 2/1
        325,  // 9/4
        350,  // 5/2
        375,  // 11/4
        400,  // 3/1
        450,  // 7/2
        500,  // 4/1
        550,  // 9/2
        600,  // 5/1
        650,  // 11/2
        700,  // 6/1
        800,  // 7/1
        900,  // 8/1
        1000, // 9/1
        1100, // 10/1
        1200, // 11/1
        1400, // 13/1
        1600, // 15/1
        1800, // 17/1
        2000, // 19/1
    };

    public OddsService(IDbContextFactory<ApplicationDbContext> dbFactory, RankingService rankingService)
    {
        _dbFactory = dbFactory;
        _rankingService = rankingService;
    }

    /// <summary>
    /// Generates initial odds for all players/teams in a game based on their ELO ratings.
    /// Includes a small random factor for fun.
    /// </summary>
    /// <param name="gameNightGameId">The game to generate odds for.</param>
    /// <returns>True if odds were generated, false if the game wasn't found or has no players.</returns>
    public async Task<bool> GenerateInitialOddsAsync(int gameNightGameId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var game = await db.GameNightGames
            .Include(g => g.Players)
            .FirstOrDefaultAsync(g => g.Id == gameNightGameId, ct);

        if (game is null || game.Players.Count == 0)
        {
            return false;
        }

        var playerIds = game.Players.Select(p => p.MemberId).ToList();
        var ratings = await _rankingService.GetRatingsAsync(playerIds, ct);

        // Check if this is a team game
        var teams = game.Players
            .Where(p => !string.IsNullOrWhiteSpace(p.TeamName))
            .GroupBy(p => p.TeamName!)
            .ToList();

        Dictionary<Guid, int> oddsMap;

        if (teams.Count >= 2)
        {
            // Team-based odds
            oddsMap = CalculateTeamOdds(teams, ratings);
        }
        else
        {
            // Individual odds
            oddsMap = CalculateIndividualOdds(playerIds, ratings);
        }

        // Remove existing odds and add new ones
        var existingOdds = await db.GameNightGameOdds
            .Where(o => o.GameNightGameId == gameNightGameId)
            .ToListAsync(ct);

        db.GameNightGameOdds.RemoveRange(existingOdds);

        var now = DateTimeOffset.UtcNow;
        foreach (var (memberId, odds) in oddsMap)
        {
            db.GameNightGameOdds.Add(new GameNightGameOddsEntity
            {
                GameNightGameId = gameNightGameId,
                MemberId = memberId,
                OddsTimes100 = odds,
                CreatedOn = now
            });
        }

        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>
    /// Recalculates odds for a game based on current bet cashflows.
    /// Should be called within the same transaction as placing a bet.
    /// </summary>
    /// <param name="db">The database context (for transaction consistency).</param>
    /// <param name="gameNightGameId">The game to recalculate odds for.</param>
    public async Task RecalculateOddsForCashflowAsync(ApplicationDbContext db, int gameNightGameId, CancellationToken ct = default)
    {
        var game = await db.GameNightGames
            .Include(g => g.Players)
            .Include(g => g.Bets)
            .Include(g => g.Odds)
            .FirstOrDefaultAsync(g => g.Id == gameNightGameId, ct);

        if (game is null || game.Players.Count == 0 || game.Odds.Count == 0)
        {
            return;
        }

        // Get current bet totals per outcome
        var teams = game.Players
            .Where(p => !string.IsNullOrWhiteSpace(p.TeamName))
            .GroupBy(p => p.TeamName!)
            .ToList();

        var isTeamGame = teams.Count >= 2;

        // Calculate total liability for each outcome
        var liabilityByOutcome = new Dictionary<string, (int TotalBet, int PotentialPayout)>();

        if (isTeamGame)
        {
            foreach (var team in teams)
            {
                var teamMemberIds = team.Select(p => p.MemberId).ToHashSet();
                var teamBets = game.Bets
                    .Where(b => !b.IsResolved && teamMemberIds.Contains(b.PredictedWinnerMemberId))
                    .ToList();

                var totalBet = teamBets.Sum(b => b.Amount);
                var potentialPayout = teamBets.Sum(b => ComputePayout(b.Amount, b.OddsTimes100));

                liabilityByOutcome[team.Key] = (totalBet, potentialPayout);
            }
        }
        else
        {
            foreach (var player in game.Players)
            {
                var playerBets = game.Bets
                    .Where(b => !b.IsResolved && b.PredictedWinnerMemberId == player.MemberId)
                    .ToList();

                var totalBet = playerBets.Sum(b => b.Amount);
                var potentialPayout = playerBets.Sum(b => ComputePayout(b.Amount, b.OddsTimes100));

                liabilityByOutcome[player.MemberId.ToString()] = (totalBet, potentialPayout);
            }
        }

        var totalBetAmount = liabilityByOutcome.Values.Sum(x => x.TotalBet);
        if (totalBetAmount == 0)
        {
            // No bets yet, odds stay the same
            return;
        }

        var maxPotentialPayout = liabilityByOutcome.Values.Max(x => x.PotentialPayout);

        // Adjust odds based on liability imbalance
        // If one outcome has much higher potential payout, reduce its odds
        var now = DateTimeOffset.UtcNow;

        foreach (var odds in game.Odds)
        {
            string outcomeKey;
            if (isTeamGame)
            {
                var playerTeam = game.Players.FirstOrDefault(p => p.MemberId == odds.MemberId)?.TeamName;
                if (string.IsNullOrWhiteSpace(playerTeam))
                {
                    continue;
                }
                outcomeKey = playerTeam;
            }
            else
            {
                outcomeKey = odds.MemberId.ToString();
            }

            if (!liabilityByOutcome.TryGetValue(outcomeKey, out var liability))
            {
                continue;
            }

            // Calculate adjustment factor based on relative liability
            // Higher payout exposure = lower odds
            var payoutRatio = maxPotentialPayout > 0
                ? (double)liability.PotentialPayout / maxPotentialPayout
                : 0;

            // Reduce odds proportionally to the payout exposure
            var adjustmentMultiplier = 1 - (payoutRatio * CashflowAdjustmentFactor);
            adjustmentMultiplier = Math.Max(0.7, Math.Min(1.3, adjustmentMultiplier)); // Clamp adjustment

            var newOdds = (int)(odds.OddsTimes100 * adjustmentMultiplier);
            newOdds = Math.Clamp(newOdds, MinOddsTimes100, MaxOddsTimes100);
            
            // Snap to nearest appealing fraction
            newOdds = SnapToAppealingOdds(newOdds);

            odds.OddsTimes100 = newOdds;
            odds.CreatedOn = now; // Track when odds were last updated
        }

        // In team games, ensure all team members have the same odds
        if (isTeamGame)
        {
            foreach (var team in teams)
            {
                var teamMemberIds = team.Select(p => p.MemberId).ToHashSet();
                var teamOdds = game.Odds.Where(o => teamMemberIds.Contains(o.MemberId)).ToList();
                
                if (teamOdds.Count > 0)
                {
                    var representativeOdds = teamOdds.First().OddsTimes100;
                    foreach (var o in teamOdds)
                    {
                        o.OddsTimes100 = representativeOdds;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Calculates odds for individual players based on ratings.
    /// </summary>
    private Dictionary<Guid, int> CalculateIndividualOdds(
        IReadOnlyList<Guid> playerIds,
        IReadOnlyDictionary<Guid, int> ratings)
    {
        // Calculate win probabilities based on ratings
        var probabilities = CalculateWinProbabilities(playerIds, id => ratings.GetValueOrDefault(id, RankingService.DefaultRating));

        var result = new Dictionary<Guid, int>();
        foreach (var (playerId, probability) in probabilities)
        {
            var odds = ProbabilityToOdds(probability);
            odds = ApplyRandomness(odds);
            result[playerId] = odds;
        }

        return result;
    }

    /// <summary>
    /// Calculates odds for teams based on average team ratings.
    /// All members of a team get the same odds.
    /// </summary>
    private Dictionary<Guid, int> CalculateTeamOdds(
        IReadOnlyList<IGrouping<string, GameNightGamePlayerEntity>> teams,
        IReadOnlyDictionary<Guid, int> ratings)
    {
        // Calculate average rating per team
        var teamRatings = teams.Select(t => new
        {
            TeamName = t.Key,
            Members = t.ToList(),
            AverageRating = t.Average(p => ratings.GetValueOrDefault(p.MemberId, RankingService.DefaultRating))
        }).ToList();

        // Calculate team win probabilities
        var teamProbabilities = CalculateWinProbabilities(
            teamRatings,
            t => (int)t.AverageRating);

        var result = new Dictionary<Guid, int>();
        foreach (var team in teamRatings)
        {
            var probability = teamProbabilities.First(p => p.Key.TeamName == team.TeamName).Value;
            var odds = ProbabilityToOdds(probability);
            odds = ApplyRandomness(odds);

            // Apply same odds to all team members
            foreach (var member in team.Members)
            {
                result[member.MemberId] = odds;
            }
        }

        return result;
    }

    /// <summary>
    /// Calculates win probabilities for a multi-way contest using pairwise ELO comparisons.
    /// </summary>
    private Dictionary<T, double> CalculateWinProbabilities<T>(
        IEnumerable<T> contestants,
        Func<T, int> getRating) where T : notnull
    {
        var list = contestants.ToList();
        var probabilities = new Dictionary<T, double>();

        if (list.Count == 0)
        {
            return probabilities;
        }

        if (list.Count == 1)
        {
            probabilities[list[0]] = 1.0;
            return probabilities;
        }

        // Calculate relative strength using pairwise expected scores
        // This is a simplification: sum up expected scores against all opponents
        var totalStrength = 0.0;
        var strengths = new Dictionary<T, double>();

        foreach (var contestant in list)
        {
            var rating = getRating(contestant);
            var strength = 0.0;

            foreach (var opponent in list)
            {
                if (!EqualityComparer<T>.Default.Equals(contestant, opponent))
                {
                    var opponentRating = getRating(opponent);
                    strength += ExpectedScore(rating, opponentRating);
                }
            }

            strengths[contestant] = strength;
            totalStrength += strength;
        }

        // Normalize to probabilities
        foreach (var contestant in list)
        {
            probabilities[contestant] = totalStrength > 0
                ? strengths[contestant] / totalStrength
                : 1.0 / list.Count;
        }

        return probabilities;
    }

    /// <summary>
    /// Converts a win probability to decimal odds (x100).
    /// Includes generosity factor to make odds better for bettors.
    /// Snaps to nearest appealing betting fraction.
    /// </summary>
    private static int ProbabilityToOdds(double probability)
    {
        if (probability <= 0)
        {
            return MaxOddsTimes100;
        }

        if (probability >= 1)
        {
            return MinOddsTimes100;
        }

        // Fair decimal odds = 1 / probability
        // Apply generosity factor (lower = more generous to bettors)
        var fairOdds = 1.0 / probability;
        var generousOdds = fairOdds / GenerosityFactor;

        var oddsTimes100 = (int)(generousOdds * 100);
        oddsTimes100 = Math.Clamp(oddsTimes100, MinOddsTimes100, MaxOddsTimes100);
        
        // Snap to nearest appealing fraction
        return SnapToAppealingOdds(oddsTimes100);
    }

    /// <summary>
    /// Applies small random variance to odds for fun.
    /// </summary>
    private int ApplyRandomness(int oddsTimes100)
    {
        var variance = oddsTimes100 * InitialOddsRandomnessFactor;
        var adjustment = (_random.NextDouble() * 2 - 1) * variance; // -variance to +variance
        var result = (int)(oddsTimes100 + adjustment);
        return Math.Clamp(result, MinOddsTimes100, MaxOddsTimes100);
    }

    /// <summary>
    /// Standard ELO expected score formula.
    /// </summary>
    private static double ExpectedScore(int ratingA, int ratingB)
    {
        return 1.0 / (1.0 + Math.Pow(10, (ratingB - ratingA) / 400.0));
    }

    /// <summary>
    /// Snaps odds to the nearest appealing betting fraction.
    /// </summary>
    private static int SnapToAppealingOdds(int oddsTimes100)
    {
        // Binary search for closest value
        var index = Array.BinarySearch(AppealingOdds, oddsTimes100);
        
        if (index >= 0)
        {
            // Exact match
            return AppealingOdds[index];
        }

        // BinarySearch returns bitwise complement of the index where the value would be inserted
        var insertIndex = ~index;
        
        if (insertIndex == 0)
        {
            return AppealingOdds[0];
        }
        
        if (insertIndex >= AppealingOdds.Length)
        {
            return AppealingOdds[^1];
        }

        // Find the closer of the two adjacent values
        var lower = AppealingOdds[insertIndex - 1];
        var upper = AppealingOdds[insertIndex];
        
        return (oddsTimes100 - lower) <= (upper - oddsTimes100) ? lower : upper;
    }

    /// <summary>
    /// Computes total payout for a winning bet.
    /// </summary>
    private static int ComputePayout(int amount, int oddsTimes100)
    {
        if (oddsTimes100 <= 100)
        {
            return amount;
        }

        // Decimal odds: payout = stake * odds
        return (int)((amount * oddsTimes100) / 100.0);
    }

    /// <summary>
    /// Gets the current odds for all players in a game.
    /// </summary>
    public async Task<IReadOnlyDictionary<Guid, int>> GetOddsForGameAsync(int gameNightGameId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var odds = await db.GameNightGameOdds
            .AsNoTracking()
            .Where(o => o.GameNightGameId == gameNightGameId)
            .ToDictionaryAsync(o => o.MemberId, o => o.OddsTimes100, ct);

        return odds;
    }
}
