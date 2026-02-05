# Game Recommendation Section Design Review

## Overview
This document reviews the design and implementation approach for a game recommendation feature that suggests games to players based on their agreement ratings with member reviews.

## Feature Requirements

### Core Functionality
- **Personalized Recommendations**: Suggest a single game based on how much a user agrees with other members' reviews
- **User-friendly**: Apply to both admins and regular users with appropriate filtering
- **Smart Filtering**:
  - **For Admins**: Only recommend games they haven't already reviewed
  - **For Users**: Only recommend games they haven't rated agreement scores for (as agreement ratings imply they've played the game)

## Current Architecture Analysis

### Data Models

#### ReviewAgreementEntity
```csharp
public sealed class ReviewAgreementEntity
{
    public int Id { get; set; }
    public Guid ReviewId { get; set; }
    public string UserId { get; set; }
    public int Score { get; set; }  // 1..5
    public DateTimeOffset CreatedOn { get; set; }
}
```
- Tracks how much a user agrees with a specific review (score 1-5)
- The user's `UserId` is their ASP.NET Core Identity ID

#### ReviewEntity
```csharp
public sealed class ReviewEntity
{
    public Guid Id { get; set; }
    public Guid GameId { get; set; }
    public Guid ReviewerId { get; set; }
    public double Rating { get; set; }
    public int TimesPlayed { get; set; }
    public string Description { get; set; }
    public DateTimeOffset CreatedOn { get; set; }
}
```
- A review is written by a `ReviewerId` (a Member) for a specific game
- One review per game per reviewer

#### BoardGameEntity
Has a collection of reviews. The current `BoardGameService` loads games with their reviews.

### Current Services

#### BoardGameService
- Provides game retrieval with caching
- Handles adding/updating games and reviews
- Includes review agreement management methods
- Uses `IMemoryCache` for performance

#### BgmMemberService
- User/member management
- Converts between entities and domain models

### Admin Identification
- Admins are identified via ASP.NET Core Identity roles ("Admin" role)
- Use `userManager.IsInRoleAsync(user, "Admin")` or claims in `User.IsInRole("Admin")`

## Proposed Implementation

### 1. Core Service: `GameRecommendationService`

Create a new service to handle recommendation logic:

```csharp
public sealed class GameRecommendationService
{
    private readonly IDbContextFactory<ApplicationDbContext> _dbFactory;

    public GameRecommendationService(IDbContextFactory<ApplicationDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    /// <summary>
    /// Gets a game recommendation for a user based on their agreement ratings with reviews.
    /// </summary>
    /// <param name="userId">The ASP.NET Core Identity user ID</param>
    /// <param name="isAdmin">Whether the user is an admin</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A recommended BoardGame, or null if no recommendations available</returns>
    public async Task<BoardGame?> GetRecommendationAsync(
        string userId,
        bool isAdmin,
        CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        // Step 1: Get games that should be excluded
        var excludedGameIds = await GetExcludedGameIdsAsync(db, userId, isAdmin, ct);

        // Step 2: Calculate recommendation scores for eligible games
        var recommendation = await CalculateRecommendationScoreAsync(db, userId, excludedGameIds, ct);

        // Step 3: Load and return the recommended game
        if (recommendation is null)
            return null;

        return await db.Games
            .AsNoTracking()
            .Where(g => g.Id == recommendation.GameId)
            .Include(g => g.Reviews)
            .ThenInclude(r => r.Reviewer)
            .Include(g => g.VictoryRoutes)
            .ThenInclude(r => r.Options)
            .Select(g => BoardGameService.ToDomain(g))
            .FirstOrDefaultAsync(ct);
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
            // Get the member ID for the admin user
            var memberId = await db.Members
                .AsNoTracking()
                .Where(m => m.UserId == userId)
                .Select(m => m.Id)
                .FirstOrDefaultAsync(ct);

            if (memberId == Guid.Empty)
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
    /// 1. For each reviewer the user has agreed with, sum their agreement scores
    /// 2. For each game not in the excluded set:
    ///    - For each review on that game:
    ///      - If the reviewer has reviews the user agreed with, boost the score
    ///      - Calculate: Number of agreements * Average agreement score / Number of reviewers
    /// 3. Return the game with the highest score
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
            .GroupBy(ra => ra.Review.ReviewerId)
            .Select(g => new
            {
                ReviewerId = g.Key,
                AverageAgreement = g.Average(ra => ra.Score),
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
                    // Score: (avg agreement + bias for multiple reviews by same reviewer) * reviewer agreement count
                    gameScore += reviewerAgreement.AverageAgreement * 1.5;
                }
                else
                {
                    // Reviewer is unknown to user - slight positive bias to encourage exploration
                    gameScore += 1.0;
                }
            }

            // Normalize by number of reviews (games with more reviews are weighted slightly higher)
            gameScore = gameScore / Math.Sqrt(game.Reviews.Count);

            gameScores.Add((game.Id, gameScore));
        }

        // Return the game with the highest score
        return gameScores.Any() 
            ? gameScores.OrderByDescending(x => x.Score).ThenBy(x => Guid.NewGuid()).First()
            : null;
    }
}
```

### 2. Authentication and User Context

Update the component/page that displays recommendations to pass user context:

```csharp
// In a Razor component with AuthorizeView
@inject GameRecommendationService RecommendationService
@inject AuthenticationStateProvider AuthStateProvider

@code {
    private BoardGame? recommendedGame;

    protected override async Task OnInitializedAsync()
    {
        var authState = await AuthStateProvider.GetAuthenticationStateAsync();
        var user = authState.User;

        if (user.Identity?.IsAuthenticated == true)
        {
            var userId = user.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            var isAdmin = user.IsInRole("Admin");

            if (!string.IsNullOrEmpty(userId))
            {
                recommendedGame = await RecommendationService.GetRecommendationAsync(
                    userId,
                    isAdmin);
            }
        }
    }
}
```

### 3. Integration Options

#### Option A: Dedicated Recommendation Component
Create a new component `Components/Sections/GameRecommendation.razor`:
- Display a single recommended game
- Show why it was recommended (e.g., "Based on reviewers you've agreed with")
- Show reviewer details and user's agreement ratings
- Link to full game page

#### Option B: Integrate into Existing Game Listing
- Add a "Recommended for You" section above the main game list
- Use same styling as other game cards
- Only show if recommendation exists

#### Option C: Dashboard/Overview Widget
- Add as a card on a user dashboard
- Show alongside other personalized content
- Link to game details

### 4. Caching Considerations

Since recommendation calculation involves multiple database queries, consider adding caching:

```csharp
private const string RecommendationCacheKeyPrefix = "GameRecommendation:";
private static readonly TimeSpan RecommendationCacheDuration = TimeSpan.FromHours(1);

public async Task<BoardGame?> GetRecommendationAsync(
    string userId,
    bool isAdmin,
    IMemoryCache cache,
    CancellationToken ct = default)
{
    var cacheKey = $"{RecommendationCacheKeyPrefix}{userId}";
    
    return await cache.GetOrCreateAsync(cacheKey, async entry =>
    {
        entry.AbsoluteExpirationRelativeToNow = RecommendationCacheDuration;
        entry.Size = 1;
        
        // ... existing implementation
    });
}
```

**Cache Invalidation Strategy**:
- Invalidate when a user adds a review agreement
- Invalidate when a review is added or updated
- Let time-based expiration handle gradual staleness

## Edge Cases and Considerations

### 1. New Users
- Users with no agreement ratings: Should return `null` (no recommendations possible)
- Handled by returning `null` when `agreementsByReviewer` is empty

### 2. Games with No Reviews
- Won't affect recommendations (they have no scores)
- Filtered out naturally by the algorithm

### 3. Users Who've Rated Every Game
- If all games are excluded: Returns `null`
- Could improve by suggesting games with "opposite" reviewers (disagreement patterns)

### 4. Reviewer with Single Review
- Still valid for recommendations
- Weighted equally in the algorithm

### 5. Score Tie-Breaking
- Current: `ThenBy(x => Guid.NewGuid())` for random tie-breaking
- Could instead use: `ThenByDescending(x => x.Review.CreatedOn)` for most recent reviews
- Or: `ThenByDescending(x => x.Review.Rating)` for highest-rated reviews

## Testing Strategy

### Unit Tests for `GameRecommendationService`

```csharp
[TestFixture]
public sealed class GameRecommendationServiceTests
{
    private GameRecommendationService _service;
    private IDbContextFactory<ApplicationDbContext> _dbFactory;

    [SetUp]
    public void Setup()
    {
        _dbFactory = new TestDbFactory();
        _service = new GameRecommendationService(_dbFactory);
    }

    [Test]
    public async Task GetRecommendationAsync_NoAgreements_ReturnsNull()
    {
        // User with no agreements should get no recommendation
    }

    [Test]
    public async Task GetRecommendationAsync_UserAgreesWithReviews_ReturnsScoredGame()
    {
        // User with agreements should get a game recommendation
    }

    [Test]
    public async Task GetRecommendationAsync_AdminExcludesOwnReviews_ReturnsGameWithoutAdminReview()
    {
        // Admin's own reviews should be excluded
    }

    [Test]
    public async Task GetRecommendationAsync_UserExcludesAgreedGames_ReturnsGameWithoutUserAgreements()
    {
        // User's games with agreement ratings should be excluded
    }

    [Test]
    public async Task GetRecommendationAsync_AllGamesExcluded_ReturnsNull()
    {
        // If all games are excluded, return null
    }
}
```

## Recommendation Scoring Algorithm Details

### Current Implementation
The algorithm scores games based on:

1. **Known Reviewer Bias** (1.5x multiplier)
   - If the game has a review from someone the user has agreed with, boost it
   - Formula: `averageAgreement × 1.5`

2. **Unknown Reviewer Bias** (1.0x)
   - If the game has a review from someone new, slight positive bias
   - Formula: `1.0`

3. **Normalization**
   - Divide by √(number of reviews) to avoid over-weighting games with many reviews
   - Formula: `totalScore / √reviewCount`

### Example Scoring Scenario

**User Profile:**
- Agreed (4/5) with Reviewer A on 3 reviews (avg: 4.0)
- Agreed (2/5) with Reviewer B on 2 reviews (avg: 2.0)

**Game 1:** Reviews from A (5/5) and B (3/5)
- Score: (4.0 × 1.5 + 2.0 × 1.5) / √2 = (6.0 + 3.0) / 1.41 = **6.36**

**Game 2:** Reviews from A (4/5) and Unknown (4/5)
- Score: (4.0 × 1.5 + 1.0) / √2 = (6.0 + 1.0) / 1.41 = **4.96**

**Recommendation:** Game 1 (highest score)

## Future Enhancements

1. **Negative Feedback**: Consider disagreement patterns too
   - Users who disagree with certain reviewers shouldn't get their recommendations
   
2. **Confidence Scores**: Return confidence level with recommendation
   - "High confidence" if based on many agreements
   - "Low confidence" if based on few data points

3. **Alternative Recommendations**: Return top 3-5 recommendations
   - Let users see why each is recommended
   - Provide browsing suggestions

4. **Decay Function**: Weight recent agreements more heavily
   - Users' tastes evolve over time
   - Older agreements less relevant

5. **Genre/Mechanic Filtering**: Recommend only certain types of games
   - User preferences for game mechanics
   - Avoid recommending games too similar to ones they've played

6. **Collaborative Filtering**: Find similar users
   - Users who agree with reviewer X often also agree with Y
   - Recommend games that "similar users" enjoyed

## Migration Notes

- No database schema changes needed (uses existing ReviewAgreement entity)
- New service only: `GameRecommendationService`
- New component(s) for UI presentation
- No breaking changes to existing services

## Summary

The recommendation system leverages existing agreement ratings to suggest games based on user preferences. The implementation is straightforward, efficient, and can be enhanced iteratively with more sophisticated scoring algorithms and UI presentations.

Key benefits:
- ✅ Uses existing data structures
- ✅ No database migration needed
- ✅ Efficient query patterns
- ✅ Extensible algorithm
- ✅ Clear separation of concerns
- ✅ Testable design
