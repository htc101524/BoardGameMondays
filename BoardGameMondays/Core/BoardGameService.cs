using BoardGameMondays.Data;
using BoardGameMondays.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace BoardGameMondays.Core;

public sealed class BoardGameService
{
    private readonly IDbContextFactory<ApplicationDbContext> _dbFactory;

    public event Action? Changed;

    public BoardGameService(IDbContextFactory<ApplicationDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<BoardGame?> GetLatestReviewedAsync(CancellationToken ct = default)
    {
        // SQLite can't translate Max(DateTimeOffset). Instead, find the newest review and take its game.
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var latestGameId = await db.Reviews
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
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var state = await db.FeaturedState.FirstOrDefaultAsync(x => x.Id == 1, ct);
        if (state is null)
        {
            state = new FeaturedStateEntity { Id = 1 };
            db.FeaturedState.Add(state);
        }

        state.FeaturedGameId = gameId;
        await db.SaveChangesAsync(ct);
        Changed?.Invoke();
    }

    public async Task<Guid?> GetFeaturedGameIdAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return (await db.FeaturedState.AsNoTracking().FirstOrDefaultAsync(x => x.Id == 1, ct))?.FeaturedGameId;
    }

    public async Task<IReadOnlyList<BoardGame>> GetAllAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var games = await db.Games
            .AsNoTracking()
            .Include(g => g.VictoryRoutes)
            .ThenInclude(r => r.Options)
            .Include(g => g.Reviews)
            .ThenInclude(r => r.Reviewer)
            .OrderBy(g => g.Name)
            .ToListAsync(ct);

        return games.Select(ToDomain).ToArray();
    }

    public async Task<BoardGame?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var game = await db.Games
            .AsNoTracking()
            .Include(g => g.VictoryRoutes)
            .ThenInclude(r => r.Options)
            .Include(g => g.Reviews)
            .ThenInclude(r => r.Reviewer)
            .FirstOrDefaultAsync(g => g.Id == id, ct);

        return game is null ? null : ToDomain(game);
    }

    public async Task<IReadOnlyList<BoardGame>> GetByStatusAsync(GameStatus status, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var games = await db.Games
            .AsNoTracking()
            .Include(g => g.VictoryRoutes)
            .ThenInclude(r => r.Options)
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
        int? minPlayers = null,
        int? maxPlayers = null,
        int? runtimeMinutes = null,
        int? firstPlayRuntimeMinutes = null,
        double? complexity = null,
        double? boardGameGeekScore = null,
        string? boardGameGeekUrl = null,
        CancellationToken ct = default)
    {
        name = InputGuards.RequireTrimmed(name, maxLength: 120, nameof(name), "Name is required.");
        tagline = InputGuards.OptionalTrimToNull(tagline, maxLength: 200, nameof(tagline));
        imageUrl = InputGuards.OptionalRootRelativeOrHttpUrl(imageUrl, maxLength: 500, nameof(imageUrl));
        boardGameGeekUrl = InputGuards.OptionalHttpUrl(boardGameGeekUrl, maxLength: 500, nameof(boardGameGeekUrl));

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var entity = new BoardGameEntity
        {
            Id = Guid.NewGuid(),
            Name = name,
            Status = (int)status,
            Tagline = tagline,
            ImageUrl = imageUrl,

            MinPlayers = minPlayers,
            MaxPlayers = maxPlayers,
            RuntimeMinutes = runtimeMinutes,
            FirstPlayRuntimeMinutes = firstPlayRuntimeMinutes,
            Complexity = complexity,
            BoardGameGeekScore = boardGameGeekScore,
            BoardGameGeekUrl = boardGameGeekUrl
        };

        db.Games.Add(entity);
        await db.SaveChangesAsync(ct);
        Changed?.Invoke();
        return ToDomain(entity);
    }

    public async Task<BoardGame?> UpdateGameAsync(
        Guid id,
        string name,
        GameStatus status,
        string? tagline,
        string? imageUrl,
        int? minPlayers,
        int? maxPlayers,
        int? runtimeMinutes,
        int? firstPlayRuntimeMinutes,
        double? complexity,
        double? boardGameGeekScore,
        string? boardGameGeekUrl,
        CancellationToken ct = default)
    {
        name = InputGuards.RequireTrimmed(name, maxLength: 120, nameof(name), "Name is required.");
        tagline = InputGuards.OptionalTrimToNull(tagline, maxLength: 200, nameof(tagline));
        imageUrl = InputGuards.OptionalRootRelativeOrHttpUrl(imageUrl, maxLength: 500, nameof(imageUrl));
        boardGameGeekUrl = InputGuards.OptionalHttpUrl(boardGameGeekUrl, maxLength: 500, nameof(boardGameGeekUrl));

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var entity = await db.Games.FirstOrDefaultAsync(g => g.Id == id, ct);
        if (entity is null)
        {
            return null;
        }

        entity.Name = name;
        entity.Status = (int)status;
        entity.Tagline = tagline;
        entity.ImageUrl = imageUrl;

        entity.MinPlayers = minPlayers;
        entity.MaxPlayers = maxPlayers;
        entity.RuntimeMinutes = runtimeMinutes;
        entity.FirstPlayRuntimeMinutes = firstPlayRuntimeMinutes;
        entity.Complexity = complexity;
        entity.BoardGameGeekScore = boardGameGeekScore;
        entity.BoardGameGeekUrl = boardGameGeekUrl;

        await db.SaveChangesAsync(ct);
        Changed?.Invoke();

        return await GetByIdAsync(id, ct);
    }

    public async Task<BoardGame?> AddReviewAsync(Guid gameId, Review review, CancellationToken ct = default)
    {
        if (review is null)
        {
            throw new ArgumentNullException(nameof(review));
        }

        var description = InputGuards.RequireTrimmed(review.Description, maxLength: 4_000, nameof(review), "Description is required.");

        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var gameExists = await db.Games.AnyAsync(g => g.Id == gameId, ct);
        if (!gameExists)
        {
            return null;
        }

        var reviewerId = await GetOrCreateReviewerIdAsync(db, review.Reviewer, ct);

        // One review per game per reviewer.
        var existing = await db.Reviews
            .FirstOrDefaultAsync(r => r.GameId == gameId && r.ReviewerId == reviewerId, ct);

        if (existing is null)
        {
            var entity = new ReviewEntity
            {
                Id = Guid.NewGuid(),
                GameId = gameId,
                ReviewerId = reviewerId,
                Rating = review.Rating,
                TimesPlayed = review.TimesPlayed,
                Description = description,
                CreatedOn = DateTimeOffset.UtcNow
            };

            db.Reviews.Add(entity);
        }
        else
        {
            existing.Rating = review.Rating;
            existing.Description = description;
            existing.CreatedOn = DateTimeOffset.UtcNow;
        }
        await db.SaveChangesAsync(ct);
        Changed?.Invoke();

        return await GetByIdAsync(gameId, ct);
    }

    public async Task<BoardGame?> AddVictoryRouteAsync(Guid gameId, string name, VictoryRouteType type, bool isRequired, CancellationToken ct = default)
    {
        name = InputGuards.RequireTrimmed(name, maxLength: 128, nameof(name), "Name is required.");

        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var exists = await db.Games.AnyAsync(g => g.Id == gameId, ct);
        if (!exists)
        {
            return null;
        }

        var nextSort = await db.VictoryRoutes
            .Where(r => r.GameId == gameId)
            .Select(r => (int?)r.SortOrder)
            .MaxAsync(ct) ?? -1;
        nextSort += 1;

        db.VictoryRoutes.Add(new VictoryRouteEntity
        {
            Id = Guid.NewGuid(),
            GameId = gameId,
            Name = name,
            Type = (int)type,
            IsRequired = isRequired,
            SortOrder = nextSort
        });

        await db.SaveChangesAsync(ct);
        Changed?.Invoke();
        return await GetByIdAsync(gameId, ct);
    }

    public async Task<BoardGame?> RemoveVictoryRouteAsync(Guid gameId, Guid routeId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var route = await db.VictoryRoutes
            .FirstOrDefaultAsync(r => r.Id == routeId && r.GameId == gameId, ct);

        if (route is null)
        {
            return await GetByIdAsync(gameId, ct);
        }

        db.VictoryRoutes.Remove(route);
        await db.SaveChangesAsync(ct);
        Changed?.Invoke();
        return await GetByIdAsync(gameId, ct);
    }

    public async Task<BoardGame?> AddVictoryRouteOptionAsync(Guid gameId, Guid routeId, string value, CancellationToken ct = default)
    {
        value = InputGuards.RequireTrimmed(value, maxLength: 128, nameof(value), "Value is required.");

        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var route = await db.VictoryRoutes.FirstOrDefaultAsync(r => r.Id == routeId && r.GameId == gameId, ct);
        if (route is null)
        {
            return null;
        }

        var nextSort = await db.VictoryRouteOptions
            .Where(o => o.VictoryRouteId == routeId)
            .Select(o => (int?)o.SortOrder)
            .MaxAsync(ct) ?? -1;
        nextSort += 1;

        db.VictoryRouteOptions.Add(new VictoryRouteOptionEntity
        {
            Id = Guid.NewGuid(),
            VictoryRouteId = routeId,
            Value = value,
            SortOrder = nextSort
        });

        await db.SaveChangesAsync(ct);
        Changed?.Invoke();
        return await GetByIdAsync(gameId, ct);
    }

    public async Task<BoardGame?> RemoveVictoryRouteOptionAsync(Guid gameId, Guid routeId, Guid optionId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var option = await db.VictoryRouteOptions
            .Include(o => o.VictoryRoute)
            .FirstOrDefaultAsync(o => o.Id == optionId && o.VictoryRouteId == routeId && o.VictoryRoute.GameId == gameId, ct);

        if (option is null)
        {
            return await GetByIdAsync(gameId, ct);
        }

        db.VictoryRouteOptions.Remove(option);
        await db.SaveChangesAsync(ct);
        Changed?.Invoke();
        return await GetByIdAsync(gameId, ct);
    }

    public async Task<int?> IncrementReviewTimesPlayedAsync(Guid reviewId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var affected = await db.Reviews
            .Where(r => r.Id == reviewId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(r => r.TimesPlayed, r => r.TimesPlayed + 1), ct);

        if (affected == 0)
        {
            return null;
        }

        var newValue = await db.Reviews
            .AsNoTracking()
            .Where(r => r.Id == reviewId)
            .Select(r => (int?)r.TimesPlayed)
            .FirstOrDefaultAsync(ct);

        Changed?.Invoke();
        return newValue;
    }

    private static async Task<Guid> GetOrCreateReviewerIdAsync(ApplicationDbContext db, BgmMember member, CancellationToken ct)
    {
        var name = member.Name.Trim();

        var existing = await db.Members.FirstOrDefaultAsync(m => m.Name == name, ct);
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

        db.Members.Add(created);
        await db.SaveChangesAsync(ct);
        return created.Id;
    }

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
            boardGameGeekUrl: entity.BoardGameGeekUrl);
    }
}
