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

        // Preload last played dates to filter votes efficiently
        var lastPlayedByGame = await db.GameNightGames
            .AsNoTracking()
            .Where(g => g.IsPlayed)
            .GroupBy(g => g.GameId)
            .Select(g => new { GameId = g.Key, LastPlayedDateKey = g.Max(x => x.GameNight.DateKey) })
            .ToDictionaryAsync(x => x.GameId, x => x.LastPlayedDateKey, ct);

        // Get votes for existing games only, filtering out votes older than last play
        // (Join ensures votes for deleted games are excluded)
        var voteCounts = await db.WantToPlayVotes
            .AsNoTracking()
            .Join(db.Games, v => v.GameId, g => g.Id, (v, g) => new { v, g })
            .GroupBy(x => x.v.GameId)
            .Select(g => new
            {
                GameId = g.Key,
                Votes = g.Select(x => new { x.v.CreatedOn }).ToList()
            })
            .ToListAsync(ct);

        // Filter votes that are after the game was last played
        var validCounts = new Dictionary<Guid, int>();
        foreach (var voteGroup in voteCounts)
        {
            if (lastPlayedByGame.TryGetValue(voteGroup.GameId, out var lastPlayedKey))
            {
                var validVotes = voteGroup.Votes.Count(v =>
                {
                    var voteKey = GameNightService.ToDateKey(DateOnly.FromDateTime(v.CreatedOn.UtcDateTime));
                    return voteKey > lastPlayedKey;
                });

                if (validVotes > 0)
                {
                    validCounts[voteGroup.GameId] = validVotes;
                }
            }
            else
            {
                // Game has never been played, all votes count
                validCounts[voteGroup.GameId] = voteGroup.Votes.Count;
            }
        }

        if (validCounts.Count == 0)
        {
            return Array.Empty<WantToPlayEntry>();
        }

        // Get top game IDs
        var topGameIds = validCounts
            .OrderByDescending(x => x.Value)
            .Take(take)
            .Select(x => x.Key)
            .ToList();

        // Fetch game details for only the top games
        // Note: Direct Contains() with GUIDs can fail with SQLite, so fetch all and filter in-memory
        var allGames = await db.Games
            .AsNoTracking()
            .Select(g => new { g.Id, g.Name, g.ImageUrl })
            .ToListAsync(ct);
        
        var games = allGames.Where(g => topGameIds.Contains(g.Id)).ToList();

        var gameLookup = games.ToDictionary(g => g.Id, g => g);

        // DEBUG: Log mismatches
        foreach (var gameId in topGameIds)
        {
            if (!gameLookup.ContainsKey(gameId))
            {
                Console.WriteLine($"WARNING: Game ID {gameId} in votes but not found in Games table");
            }
        }

        return validCounts
            .OrderByDescending(x => x.Value)
            .ThenBy(x => gameLookup.TryGetValue(x.Key, out var g) ? g.Name : string.Empty)
            .Take(take)
            .Select(x =>
            {
                if (!gameLookup.TryGetValue(x.Key, out var game))
                {
                    Console.WriteLine($"WARNING: Returning Unknown for GameId {x.Key} with {x.Value} votes");
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
