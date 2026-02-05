using BoardGameMondays.Data;
using BoardGameMondays.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace BoardGameMondays.Core;

/// <summary>
/// Provides personalized game recommendations based on user agreement patterns with member reviews.
/// </summary>
public sealed class GameRecommendationService
{
    private readonly IDbContextFactory<ApplicationDbContext> _dbFactory;
    private const string MemberIdClaimType = "bgm:memberId";

    public GameRecommendationService(IDbContextFactory<ApplicationDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    /// <summary>
    /// Gets a personalized game recommendation for a user based on their agreement ratings with reviews.
    /// </summary>
    /// <param name="userId">The ASP.NET Core Identity user ID</param>
    /// <param name="isAdmin">Whether the user is an admin</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A recommended BoardGame, or null if no recommendations are available</returns>
    /// <remarks>
    /// For admins: Excludes games they have already reviewed.
    /// For users: Excludes games they have rated agreement scores for (implying they've played).
    /// 
    /// Recommendation scoring:
    /// - Games with reviews from reviewers the user has agreed with score higher
    /// - Games with reviews from new reviewers get a small exploration bonus
    /// - Final score is normalized by √(review count) to prevent over-weighting popular games
    /// </remarks>
    public async Task<BoardGame?> GetRecommendationAsync(
        string userId,
        bool isAdmin,
        CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(userId))
            return null;

        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        // Step 1: Get games that should be excluded
        var excludedGameIds = await GetExcludedGameIdsAsync(db, userId, isAdmin, ct);

        // Step 2: Calculate recommendation scores for eligible games
        var recommendation = await CalculateRecommendationScoreAsync(db, userId, excludedGameIds, ct);

        // Step 3: Load and return the recommended game
        if (recommendation is null)
            return null;

        var gameEntity = await db.Games
            .AsNoTracking()
            .Where(g => g.Id == recommendation.Value.GameId)
            .Include(g => g.Reviews)
            .ThenInclude(r => r.Reviewer)
            .Include(g => g.VictoryRoutes)
            .ThenInclude(r => r.Options)
            .FirstOrDefaultAsync(ct);

        return gameEntity is null ? null : ToDomain(gameEntity);
    }

    /// <summary>
    /// Gets games that should be excluded from recommendations.
    /// - For admins: games they have reviewed
    /// - For users: games they have rated agreement scores for
    /// </summary>
    private async Task<HashSet<Guid>> GetExcludedGameIdsAsync(
        ApplicationDbContext db,
        string userId,
        bool isAdmin,
        CancellationToken ct)
    {
        if (isAdmin)
        {
            // Get the member ID for the admin user from user claims
            var memberIdValue = await db.UserClaims
                .AsNoTracking()
                .Where(c => c.UserId == userId && c.ClaimType == MemberIdClaimType)
                .Select(c => c.ClaimValue)
                .FirstOrDefaultAsync(ct);

            if (!Guid.TryParse(memberIdValue, out var memberId) || memberId == Guid.Empty)
                return new HashSet<Guid>();

            // Get all games the admin has reviewed
            return (await db.Reviews
                .AsNoTracking()
                .Where(r => r.ReviewerId == memberId)
                .Select(r => r.GameId)
                .ToListAsync(ct))
                .ToHashSet();
        }
        else
        {
            // Get all games the user has rated agreement scores for
            return (await db.ReviewAgreements
                .AsNoTracking()
                .Where(ra => ra.UserId == userId)
                .Include(ra => ra.Review)
                .Select(ra => ra.Review.GameId)
                .Distinct()
                .ToListAsync(ct))
                .ToHashSet();
        }
    }

    /// <summary>
    /// Calculates recommendation score based on user's agreement patterns.
    /// 
    /// Algorithm:
    /// 1. Get all reviewers the user has agreed with and their average agreement score
    /// 2. For each game not in the excluded set:
    ///    - For each review on that game:
    ///      - If the reviewer has reviews the user agreed with, boost the score (1.5x)
    ///      - Otherwise, add a small exploration bonus (1.0x)
    /// 3. Normalize by √(review count) to prevent games with many reviews from dominating
    /// 4. Return the game with the highest score, with tie-breaking for deterministic results
    /// </summary>
    private async Task<(Guid GameId, double Score)?> CalculateRecommendationScoreAsync(
        ApplicationDbContext db,
        string userId,
        HashSet<Guid> excludedGameIds,
        CancellationToken ct)
    {
        // Get all reviewers the user has agreed with and their average agreement score
        var agreementsByReviewer = await db.ReviewAgreements
            .AsNoTracking()
            .Where(ra => ra.UserId == userId)
            .Include(ra => ra.Review)
            .GroupBy(ra => ra.Review.ReviewerId)
            .Select(g => new
            {
                ReviewerId = g.Key,
                AverageAgreement = g.Average(ra => (double)ra.Score),
                AgreementCount = g.Count()
            })
            .ToListAsync(ct);

        if (!agreementsByReviewer.Any())
            return null;

        // Get all games with their reviews (excluding the user's games)
        var gamesWithReviews = await db.Games
            .AsNoTracking()
            .Where(g => !excludedGameIds.Contains(g.Id))
            .Include(g => g.Reviews)
            .ToListAsync(ct);

        if (!gamesWithReviews.Any())
            return null;

        // Calculate scores for each game
        var gameScores = new List<(Guid GameId, double Score)>();

        foreach (var game in gamesWithReviews)
        {
            double gameScore = 0;

            foreach (var review in game.Reviews)
            {
                // Check if user has agreed with this reviewer before
                var reviewerAgreement = agreementsByReviewer
                    .FirstOrDefault(r => r.ReviewerId == review.ReviewerId);

                if (reviewerAgreement != null)
                {
                    // Reviewer is someone the user has agreed with
                    // Score: average agreement * boost factor (1.5x promotes known reviewers)
                    gameScore += reviewerAgreement.AverageAgreement * 1.5;
                }
                else
                {
                    // Reviewer is unknown to user - small positive bias to encourage exploration
                    gameScore += 1.0;
                }
            }

            // Normalize by sqrt(number of reviews) to avoid over-weighting games with many reviews
            gameScore = gameScore / Math.Sqrt(game.Reviews.Count);

            gameScores.Add((game.Id, gameScore));
        }

        // Return the game with the highest score
        // Tie-breaking: use GameId for deterministic sorting (so tests can rely on consistent results)
        return gameScores.Any()
            ? gameScores.OrderByDescending(x => x.Score).ThenBy(x => x.GameId).First()
            : null;
    }

    /// <summary>
    /// Converts a BoardGameEntity to a BoardGame domain model.
    /// </summary>
    private static BoardGame ToDomain(BoardGameEntity entity)
    {
        var victoryRoutes = entity.VictoryRoutes
            .OrderBy(r => r.SortOrder)
            .Select(r => new VictoryRoute(
                r.Id,
                r.Name,
                (VictoryRouteType)r.Type,
                r.IsRequired,
                r.SortOrder,
                r.Options
                    .OrderBy(o => o.SortOrder)
                    .Select(o => new VictoryRouteOption(o.Id, o.Value, o.SortOrder))
                    .ToArray()))
            .ToArray();

        var reviews = entity.Reviews
            .OrderByDescending(r => r.CreatedOn)
            .Select(r => (Review)new MemberReview(
                reviewer: new PersistedBgmMember(r.Reviewer.Name, r.Reviewer.Email, r.Reviewer.Summary, r.Reviewer.AvatarUrl),
                rating: r.Rating,
                description: r.Description,
                timesPlayed: r.TimesPlayed,
                createdOn: r.CreatedOn,
                id: r.Id))
            .ToArray();

        return new BoardGame(
            id: entity.Id,
            name: entity.Name,
            status: (GameStatus)entity.Status,
            overview: EmptyOverview.Instance,
            reviews: reviews,
            victoryRoutes: victoryRoutes,
            tagline: entity.Tagline,
            imageUrl: entity.ImageUrl,
            minPlayers: entity.MinPlayers,
            maxPlayers: entity.MaxPlayers,
            runtimeMinutes: entity.RuntimeMinutes,
            firstPlayRuntimeMinutes: entity.FirstPlayRuntimeMinutes,
            complexity: entity.Complexity,
            boardGameGeekScore: entity.BoardGameGeekScore,
            boardGameGeekUrl: entity.BoardGameGeekUrl,
            areScoresCountable: entity.AreScoresCountable,
            highScore: entity.HighScore,
            highScoreMemberId: entity.HighScoreMemberId,
            highScoreMemberName: entity.HighScoreMemberName,
            highScoreAchievedOn: entity.HighScoreAchievedOn);
    }
}
