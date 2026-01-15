using BoardGameMondays.Data;
using BoardGameMondays.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace BoardGameMondays.Core;

public sealed class WantToPlayService
{
    private const int WeeklyLimit = 3;
    private readonly IDbContextFactory<ApplicationDbContext> _dbFactory;

    public event Action? Changed;

    public WantToPlayService(IDbContextFactory<ApplicationDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<WantToPlayUserStatus> GetUserStatusAsync(string userId, DateOnly today, CancellationToken ct = default)
    {
        var weekKey = GetWeekKey(today);
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var votes = await db.WantToPlayVotes
            .AsNoTracking()
            .Where(v => v.UserId == userId && v.WeekKey == weekKey)
            .Select(v => v.GameId)
            .ToListAsync(ct);

        var remaining = Math.Max(0, WeeklyLimit - votes.Count);
        return new WantToPlayUserStatus(remaining, WeeklyLimit, weekKey, votes);
    }

    public async Task<WantToPlayVoteResult> VoteAsync(string userId, Guid gameId, DateOnly today, CancellationToken ct = default)
    {
        var weekKey = GetWeekKey(today);
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var existing = await db.WantToPlayVotes
            .AsNoTracking()
            .Where(v => v.UserId == userId && v.GameId == gameId && v.WeekKey == weekKey)
            .AnyAsync(ct);

        if (existing)
        {
            return WantToPlayVoteResult.Failed("You've already voted for this game this week.");
        }

        var used = await db.WantToPlayVotes
            .AsNoTracking()
            .CountAsync(v => v.UserId == userId && v.WeekKey == weekKey, ct);

        if (used >= WeeklyLimit)
        {
            return WantToPlayVoteResult.Failed("You've used all your votes for this week.");
        }

        var vote = new WantToPlayVoteEntity
        {
            GameId = gameId,
            UserId = userId,
            WeekKey = weekKey,
            CreatedOn = DateTimeOffset.UtcNow
        };

        db.WantToPlayVotes.Add(vote);
        await db.SaveChangesAsync(ct);
        Changed?.Invoke();

        return WantToPlayVoteResult.Ok;
    }

    public async Task<IReadOnlyList<WantToPlayEntry>> GetTopWantedAsync(int take, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var votes = await db.WantToPlayVotes
            .AsNoTracking()
            .ToListAsync(ct);

        if (votes.Count == 0)
        {
            return Array.Empty<WantToPlayEntry>();
        }

        var played = await db.GameNightGames
            .AsNoTracking()
            .Where(g => g.IsPlayed)
            .Select(g => new { g.GameId, g.GameNight.DateKey })
            .ToListAsync(ct);

        var lastPlayedByGame = played
            .GroupBy(x => x.GameId)
            .ToDictionary(g => g.Key, g => g.Max(x => x.DateKey));

        var games = await db.Games
            .AsNoTracking()
            .Select(g => new { g.Id, g.Name, g.ImageUrl })
            .ToListAsync(ct);

        var gameLookup = games.ToDictionary(g => g.Id, g => g);
        var counts = new Dictionary<Guid, int>();

        foreach (var vote in votes)
        {
            if (lastPlayedByGame.TryGetValue(vote.GameId, out var lastPlayedKey))
            {
                var voteKey = GameNightService.ToDateKey(DateOnly.FromDateTime(vote.CreatedOn.UtcDateTime));
                if (voteKey <= lastPlayedKey)
                {
                    continue;
                }
            }

            counts[vote.GameId] = counts.TryGetValue(vote.GameId, out var current) ? current + 1 : 1;
        }

        return counts
            .OrderByDescending(x => x.Value)
            .ThenBy(x => gameLookup.TryGetValue(x.Key, out var g) ? g.Name : string.Empty)
            .Take(take)
            .Select(x =>
            {
                if (!gameLookup.TryGetValue(x.Key, out var game))
                {
                    return new WantToPlayEntry(x.Key, "Unknown", null, x.Value);
                }

                return new WantToPlayEntry(game.Id, game.Name, game.ImageUrl, x.Value);
            })
            .ToArray();
    }

    public static int GetWeekKey(DateOnly date)
    {
        var monday = GetMondayOnOrBefore(date);
        return GameNightService.ToDateKey(monday);
    }

    private static DateOnly GetMondayOnOrBefore(DateOnly date)
    {
        var delta = ((int)date.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
        return date.AddDays(-delta);
    }

    public sealed record WantToPlayUserStatus(int VotesRemaining, int WeeklyLimit, int WeekKey, IReadOnlyList<Guid> VotedGameIds);

    public sealed record WantToPlayEntry(Guid GameId, string Name, string? ImageUrl, int Votes);

    public sealed class WantToPlayVoteResult
    {
        public bool Success { get; init; }
        public string? Message { get; init; }

        private WantToPlayVoteResult(bool success, string? message)
        {
            Success = success;
            Message = message;
        }

        public static WantToPlayVoteResult Ok { get; } = new(true, null);
        public static WantToPlayVoteResult Failed(string message) => new(false, message);
    }
}
