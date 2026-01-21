using BoardGameMondays.Data;
using BoardGameMondays.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace BoardGameMondays.Core;

/// <summary>
/// Manages ELO-style ratings for BGM members based on game outcomes.
/// </summary>
public sealed class RankingService
{
    private readonly IDbContextFactory<ApplicationDbContext> _dbFactory;

    /// <summary>
    /// The K-factor determines how much ratings change per game.
    /// Higher values = more volatile ratings.
    /// Chess uses 16-32; we use 32 for more dynamic changes in a casual setting.
    /// </summary>
    private const int KFactor = 32;

    /// <summary>
    /// Default rating for new members.
    /// </summary>
    public const int DefaultRating = 1200;

    /// <summary>
    /// Minimum rating floor to prevent ratings going too low.
    /// </summary>
    public const int MinRating = 100;

    public RankingService(IDbContextFactory<ApplicationDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    /// <summary>
    /// Gets the current ELO rating for a member.
    /// </summary>
    public async Task<int> GetRatingAsync(Guid memberId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var member = await db.Members
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == memberId, ct);

        return member?.EloRating ?? DefaultRating;
    }

    /// <summary>
    /// Gets the ratings for multiple members.
    /// </summary>
    public async Task<IReadOnlyDictionary<Guid, int>> GetRatingsAsync(IEnumerable<Guid> memberIds, CancellationToken ct = default)
    {
        var ids = memberIds.ToHashSet();
        if (ids.Count == 0)
        {
            return new Dictionary<Guid, int>();
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var members = await db.Members
            .AsNoTracking()
            .Where(m => ids.Contains(m.Id))
            .Select(m => new { m.Id, m.EloRating })
            .ToListAsync(ct);

        var result = new Dictionary<Guid, int>();
        foreach (var id in ids)
        {
            var member = members.FirstOrDefault(m => m.Id == id);
            result[id] = member?.EloRating ?? DefaultRating;
        }

        return result;
    }

    /// <summary>
    /// Updates ratings after a game with an individual winner.
    /// The winner gains points from each loser based on rating differences.
    /// </summary>
    /// <param name="winnerId">The member who won.</param>
    /// <param name="loserIds">The members who lost.</param>
    public async Task UpdateRatingsForIndividualGameAsync(
        Guid winnerId,
        IReadOnlyList<Guid> loserIds,
        CancellationToken ct = default)
    {
        if (loserIds.Count == 0)
        {
            return;
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var allIds = loserIds.Append(winnerId).Distinct().ToList();
        var members = await db.Members
            .Where(m => allIds.Contains(m.Id))
            .ToListAsync(ct);

        var winner = members.FirstOrDefault(m => m.Id == winnerId);
        if (winner is null)
        {
            return;
        }

        var losers = members.Where(m => loserIds.Contains(m.Id)).ToList();
        if (losers.Count == 0)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var winnerRating = winner.EloRating;
        var totalWinnerGain = 0;

        foreach (var loser in losers)
        {
            var (winnerGain, loserLoss) = CalculateRatingChange(winnerRating, loser.EloRating);
            totalWinnerGain += winnerGain;

            loser.EloRating = Math.Max(MinRating, loser.EloRating - loserLoss);
            loser.EloRatingUpdatedOn = now;
        }

        winner.EloRating += totalWinnerGain;
        winner.EloRatingUpdatedOn = now;

        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Updates ratings after a team-based game.
    /// Each member in the winning team gains points; each member in losing teams loses points.
    /// The change is based on the average rating of the winning team vs each losing team's average.
    /// </summary>
    /// <param name="winningTeamMemberIds">Members on the winning team.</param>
    /// <param name="losingTeamsMemberIds">Members on losing teams, grouped by team.</param>
    public async Task UpdateRatingsForTeamGameAsync(
        IReadOnlyList<Guid> winningTeamMemberIds,
        IReadOnlyList<IReadOnlyList<Guid>> losingTeamsMemberIds,
        CancellationToken ct = default)
    {
        if (winningTeamMemberIds.Count == 0 || losingTeamsMemberIds.Count == 0)
        {
            return;
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var allIds = winningTeamMemberIds
            .Concat(losingTeamsMemberIds.SelectMany(t => t))
            .Distinct()
            .ToList();

        var members = await db.Members
            .Where(m => allIds.Contains(m.Id))
            .ToListAsync(ct);

        var memberById = members.ToDictionary(m => m.Id);

        // Calculate winning team's average rating
        var winningTeamMembers = winningTeamMemberIds
            .Select(id => memberById.GetValueOrDefault(id))
            .Where(m => m is not null)
            .ToList();

        if (winningTeamMembers.Count == 0)
        {
            return;
        }

        var winningTeamAverage = winningTeamMembers.Average(m => m!.EloRating);

        var now = DateTimeOffset.UtcNow;
        var totalWinnerGainPerMember = 0;

        // Process each losing team
        foreach (var losingTeamIds in losingTeamsMemberIds)
        {
            var losingTeamMembers = losingTeamIds
                .Select(id => memberById.GetValueOrDefault(id))
                .Where(m => m is not null)
                .ToList();

            if (losingTeamMembers.Count == 0)
            {
                continue;
            }

            var losingTeamAverage = losingTeamMembers.Average(m => m!.EloRating);

            // Calculate change based on team averages
            var (winnerGain, loserLoss) = CalculateRatingChange((int)winningTeamAverage, (int)losingTeamAverage);
            totalWinnerGainPerMember += winnerGain;

            // Apply same loss to each member of the losing team
            foreach (var loser in losingTeamMembers)
            {
                loser!.EloRating = Math.Max(MinRating, loser.EloRating - loserLoss);
                loser.EloRatingUpdatedOn = now;
            }
        }

        // Apply same gain to each member of the winning team
        foreach (var winner in winningTeamMembers)
        {
            winner!.EloRating += totalWinnerGainPerMember;
            winner.EloRatingUpdatedOn = now;
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Updates ratings after a game with no winner (e.g., co-op game where everyone lost).
    /// All players lose a small amount of ELO as a humorous penalty.
    /// </summary>
    public async Task UpdateRatingsForNoWinnerGameAsync(IReadOnlyList<Guid> playerIds, CancellationToken ct = default)
    {
        if (playerIds.Count == 0)
        {
            return;
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        
        var members = await db.Members
            .Where(m => playerIds.Contains(m.Id))
            .ToListAsync(ct);

        if (members.Count == 0)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        const int penaltyPerPlayer = 10; // Fixed small penalty for everyone

        foreach (var member in members)
        {
            member.EloRating = Math.Max(MinRating, member.EloRating - penaltyPerPlayer);
            member.EloRatingUpdatedOn = now;
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Calculates the rating change for a winner/loser pair using the ELO formula.
    /// </summary>
    /// <param name="winnerRating">Current rating of the winner.</param>
    /// <param name="loserRating">Current rating of the loser.</param>
    /// <returns>Tuple of (winner gain, loser loss). These may differ slightly due to rounding.</returns>
    public static (int WinnerGain, int LoserLoss) CalculateRatingChange(int winnerRating, int loserRating)
    {
        // Expected score for winner (probability of winning based on ratings)
        var expectedWinner = ExpectedScore(winnerRating, loserRating);
        var expectedLoser = 1 - expectedWinner;

        // Actual score: winner = 1, loser = 0
        // Rating change = K * (actual - expected)
        var winnerChange = (int)Math.Round(KFactor * (1 - expectedWinner));
        var loserChange = (int)Math.Round(KFactor * (0 - expectedLoser));

        // Winner gains, loser loses (loserChange is negative, so we negate)
        return (winnerChange, -loserChange);
    }

    /// <summary>
    /// Calculates the expected score (probability of winning) for a player.
    /// Uses the standard ELO formula: E = 1 / (1 + 10^((Rb - Ra) / 400))
    /// </summary>
    private static double ExpectedScore(int ratingA, int ratingB)
    {
        return 1.0 / (1.0 + Math.Pow(10, (ratingB - ratingA) / 400.0));
    }

    /// <summary>
    /// Gets a leaderboard of members sorted by rating.
    /// </summary>
    public async Task<IReadOnlyList<MemberRanking>> GetLeaderboardAsync(int take = 20, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        
        var members = await db.Members
            .AsNoTracking()
            .Where(m => m.IsBgmMember)
            .OrderByDescending(m => m.EloRating)
            .Take(take)
            .Select(m => new MemberRanking(m.Id, m.Name, m.EloRating, m.EloRatingUpdatedOn))
            .ToListAsync(ct);

        return members;
    }

    public sealed record MemberRanking(Guid MemberId, string Name, int Rating, DateTimeOffset? LastUpdated);
}
