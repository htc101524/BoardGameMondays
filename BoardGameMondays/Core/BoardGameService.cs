using BoardGameMondays.Data;
using BoardGameMondays.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace BoardGameMondays.Core;

public sealed class BoardGameService
{
    private readonly ApplicationDbContext _db;

    public event Action? Changed;

    public BoardGameService(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<BoardGame?> GetLatestReviewedAsync(CancellationToken ct = default)
    {
        // SQLite can't translate Max(DateTimeOffset). Instead, find the newest review and take its game.
        var latestGameId = await _db.Reviews
            .AsNoTracking()
            .OrderByDescending(r => r.CreatedOn)
            .Select(r => (Guid?)r.GameId)
            .FirstOrDefaultAsync(ct);

        return latestGameId is null ? null : await GetByIdAsync(latestGameId.Value, ct);
    }

    public async Task<BoardGame?> GetFeaturedOrLatestReviewedAsync(CancellationToken ct = default)
    {
        var featuredId = await GetFeaturedGameIdAsync(ct);
        if (featuredId is { } id)
        {
            var featured = await GetByIdAsync(id, ct);
            if (featured is not null)
            {
                return featured;
            }

            await SetFeaturedGameAsync(null, ct);
        }

        return await GetLatestReviewedAsync(ct);
    }

    public async Task SetFeaturedGameAsync(Guid? gameId, CancellationToken ct = default)
    {
        var state = await _db.FeaturedState.FirstOrDefaultAsync(x => x.Id == 1, ct);
        if (state is null)
        {
            state = new FeaturedStateEntity { Id = 1 };
            _db.FeaturedState.Add(state);
        }

        state.FeaturedGameId = gameId;
        await _db.SaveChangesAsync(ct);
        Changed?.Invoke();
    }

    public async Task<Guid?> GetFeaturedGameIdAsync(CancellationToken ct = default)
        => (await _db.FeaturedState.AsNoTracking().FirstOrDefaultAsync(x => x.Id == 1, ct))?.FeaturedGameId;

    public async Task<IReadOnlyList<BoardGame>> GetAllAsync(CancellationToken ct = default)
    {
        var games = await _db.Games
            .AsNoTracking()
            .Include(g => g.Reviews)
            .ThenInclude(r => r.Reviewer)
            .OrderBy(g => g.Name)
            .ToListAsync(ct);

        return games.Select(ToDomain).ToArray();
    }

    public async Task<BoardGame?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var game = await _db.Games
            .AsNoTracking()
            .Include(g => g.Reviews)
            .ThenInclude(r => r.Reviewer)
            .FirstOrDefaultAsync(g => g.Id == id, ct);

        return game is null ? null : ToDomain(game);
    }

    public async Task<IReadOnlyList<BoardGame>> GetByStatusAsync(GameStatus status, CancellationToken ct = default)
    {
        var games = await _db.Games
            .AsNoTracking()
            .Include(g => g.Reviews)
            .ThenInclude(r => r.Reviewer)
            .Where(g => g.Status == (int)status)
            .OrderBy(g => g.Name)
            .ToListAsync(ct);

        return games.Select(ToDomain).ToArray();
    }

    public async Task<BoardGame> AddGameAsync(
        string name,
        GameStatus status,
        string? tagline = null,
        string? imageUrl = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Name is required.", nameof(name));
        }

        var entity = new BoardGameEntity
        {
            Id = Guid.NewGuid(),
            Name = name.Trim(),
            Status = (int)status,
            Tagline = tagline,
            ImageUrl = imageUrl
        };

        _db.Games.Add(entity);
        await _db.SaveChangesAsync(ct);
        Changed?.Invoke();
        return ToDomain(entity);
    }

    public async Task<BoardGame?> UpdateGameAsync(
        Guid id,
        string name,
        GameStatus status,
        string? tagline,
        string? imageUrl,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Name is required.", nameof(name));
        }

        var entity = await _db.Games.FirstOrDefaultAsync(g => g.Id == id, ct);
        if (entity is null)
        {
            return null;
        }

        entity.Name = name.Trim();
        entity.Status = (int)status;
        entity.Tagline = tagline;
        entity.ImageUrl = imageUrl;

        await _db.SaveChangesAsync(ct);
        Changed?.Invoke();

        return await GetByIdAsync(id, ct);
    }

    public async Task<BoardGame?> AddReviewAsync(Guid gameId, Review review, CancellationToken ct = default)
    {
        if (review is null)
        {
            throw new ArgumentNullException(nameof(review));
        }

        var gameExists = await _db.Games.AnyAsync(g => g.Id == gameId, ct);
        if (!gameExists)
        {
            return null;
        }

        var reviewerId = await GetOrCreateReviewerIdAsync(review.Reviewer, ct);

        var entity = new ReviewEntity
        {
            Id = Guid.NewGuid(),
            GameId = gameId,
            ReviewerId = reviewerId,
            Rating = review.Rating,
            Description = review.Description,
            CreatedOn = review.CreatedOn
        };

        _db.Reviews.Add(entity);
        await _db.SaveChangesAsync(ct);
        Changed?.Invoke();

        return await GetByIdAsync(gameId, ct);
    }

    private async Task<Guid> GetOrCreateReviewerIdAsync(BgmMember member, CancellationToken ct)
    {
        var name = member.Name.Trim();

        var existing = await _db.Members.FirstOrDefaultAsync(m => m.Name == name, ct);
        if (existing is not null)
        {
            return existing.Id;
        }

        var created = new MemberEntity
        {
            Id = Guid.NewGuid(),
            Name = name,
            Email = string.IsNullOrWhiteSpace(member.Email) ? $"{name.ToLowerInvariant()}@placeholder.com" : member.Email,
            Summary = member.Summary
        };

        _db.Members.Add(created);
        await _db.SaveChangesAsync(ct);
        return created.Id;
    }

    private static BoardGame ToDomain(BoardGameEntity entity)
    {
        var reviews = entity.Reviews
            .OrderByDescending(r => r.CreatedOn)
            .Select(r => (Review)new MemberReview(
                reviewer: new PersistedBgmMember(r.Reviewer.Name, r.Reviewer.Email, r.Reviewer.Summary, r.Reviewer.AvatarUrl),
                rating: r.Rating,
                description: r.Description,
                createdOn: r.CreatedOn))
            .ToArray();

        return new BoardGame(
            id: entity.Id,
            name: entity.Name,
            status: (GameStatus)entity.Status,
            overview: EmptyOverview.Instance,
            reviews: reviews,
            tagline: entity.Tagline,
            imageUrl: entity.ImageUrl);
    }
}
