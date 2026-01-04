using BoardGameMondays.Data;
using Microsoft.EntityFrameworkCore;

namespace BoardGameMondays.Core;

public sealed class AgreementService
{
    private readonly ApplicationDbContext _db;

    public AgreementService(ApplicationDbContext db)
    {
        _db = db;
    }

    public sealed record ReviewerAlignment(string ReviewerName, double AverageScore, int Ratings);

    public async Task<int?> GetUserAgreementAsync(string userId, Guid reviewId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userId) || reviewId == Guid.Empty)
        {
            return null;
        }

        return await _db.ReviewAgreements
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

        var existing = await _db.ReviewAgreements
            .FirstOrDefaultAsync(a => a.UserId == userId && a.ReviewId == reviewId, ct);

        if (existing is null)
        {
            _db.ReviewAgreements.Add(new Data.Entities.ReviewAgreementEntity
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

        await _db.SaveChangesAsync(ct);
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

        // SQLite + GUID joins can be finicky for translation depending on provider/runtime.
        // This query is naturally bounded per-user, so we load the minimal shape and aggregate in-memory.
        var rows = await _db.ReviewAgreements
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
