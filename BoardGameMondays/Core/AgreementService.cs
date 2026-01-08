using BoardGameMondays.Data;
using Microsoft.EntityFrameworkCore;

namespace BoardGameMondays.Core;

public sealed class AgreementService
{
    private readonly IDbContextFactory<ApplicationDbContext> _dbFactory;

    private const string MemberIdClaimType = "bgm:memberId";

    public AgreementService(IDbContextFactory<ApplicationDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public sealed record ReviewerAlignment(string ReviewerName, double AverageScore, int Ratings);

    public async Task<int?> GetUserAgreementAsync(string userId, Guid reviewId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userId) || reviewId == Guid.Empty)
        {
            return null;
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.ReviewAgreements
            .AsNoTracking()
            .Where(a => a.UserId == userId && a.ReviewId == reviewId)
            .Select(a => (int?)a.Score)
            .FirstOrDefaultAsync(ct);
    }

    public async Task SetUserAgreementAsync(string userId, Guid reviewId, int score, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new InvalidOperationException("User must be logged in.");
        }

        if (reviewId == Guid.Empty)
        {
            throw new ArgumentException("Review id is required.", nameof(reviewId));
        }

        if (score is < 1 or > 5)
        {
            throw new ArgumentOutOfRangeException(nameof(score), "Score must be between 1 and 5.");
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        // Prevent users from rating agreement with their own review.
        var reviewerId = await db.Reviews
            .AsNoTracking()
            .Where(r => r.Id == reviewId)
            .Select(r => (Guid?)r.ReviewerId)
            .FirstOrDefaultAsync(ct);

        if (reviewerId is null)
        {
            throw new ArgumentException("Review was not found.", nameof(reviewId));
        }

        var memberIdValue = await db.UserClaims
            .AsNoTracking()
            .Where(c => c.UserId == userId && c.ClaimType == MemberIdClaimType)
            .Select(c => c.ClaimValue)
            .FirstOrDefaultAsync(ct);

        if (Guid.TryParse(memberIdValue, out var memberId) && memberId == reviewerId.Value)
        {
            throw new InvalidOperationException("You can't rate agreement with your own review.");
        }

        var existing = await db.ReviewAgreements
            .FirstOrDefaultAsync(a => a.UserId == userId && a.ReviewId == reviewId, ct);

        if (existing is null)
        {
            db.ReviewAgreements.Add(new Data.Entities.ReviewAgreementEntity
            {
                UserId = userId,
                ReviewId = reviewId,
                Score = score,
                CreatedOn = DateTimeOffset.UtcNow
            });
        }
        else
        {
            existing.Score = score;
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task<bool> CanUserAgreeAsync(string userId, Guid reviewId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userId) || reviewId == Guid.Empty)
        {
            return false;
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var reviewerId = await db.Reviews
            .AsNoTracking()
            .Where(r => r.Id == reviewId)
            .Select(r => (Guid?)r.ReviewerId)
            .FirstOrDefaultAsync(ct);

        if (reviewerId is null)
        {
            return false;
        }

        var memberIdValue = await db.UserClaims
            .AsNoTracking()
            .Where(c => c.UserId == userId && c.ClaimType == MemberIdClaimType)
            .Select(c => c.ClaimValue)
            .FirstOrDefaultAsync(ct);

        // If we can't determine memberId for the current user, default to allowing agreement.
        return !Guid.TryParse(memberIdValue, out var memberId) || memberId != reviewerId.Value;
    }

    public async Task<IReadOnlyList<ReviewerAlignment>> GetAlignmentAsync(string userId, int take = 5, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Array.Empty<ReviewerAlignment>();
        }

        if (take <= 0)
        {
            return Array.Empty<ReviewerAlignment>();
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        // SQLite + GUID joins can be finicky for translation depending on provider/runtime.
        // This query is naturally bounded per-user, so we load the minimal shape and aggregate in-memory.
        var rows = await db.ReviewAgreements
            .AsNoTracking()
            .Where(a => a.UserId == userId)
            .Include(a => a.Review)
            .ThenInclude(r => r.Reviewer)
            .Select(a => new { a.Score, ReviewerName = a.Review.Reviewer.Name })
            .ToListAsync(ct);

        return rows
            .Where(x => !string.IsNullOrWhiteSpace(x.ReviewerName))
            .GroupBy(x => x.ReviewerName, StringComparer.OrdinalIgnoreCase)
            .Select(g => new ReviewerAlignment(
                ReviewerName: g.Key,
                AverageScore: g.Average(x => (double)x.Score),
                Ratings: g.Count()))
            .OrderByDescending(x => x.AverageScore)
            .ThenByDescending(x => x.Ratings)
            .ThenBy(x => x.ReviewerName)
            .Take(take)
            .ToArray();
    }
}
