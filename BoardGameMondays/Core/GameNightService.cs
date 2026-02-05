using BoardGameMondays.Data;
using BoardGameMondays.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace BoardGameMondays.Core;

/// <summary>
/// Core CRUD operations for game nights.
/// Other responsibilities split into focused services:
/// - GameNightRsvpService: RSVP, attendance, snacks
/// - GameNightPlayerService: Game and player roster management
/// - GameNightTeamService: Team-based games, victory routes, winner determination
/// </summary>
public sealed class GameNightService
{
    private readonly IDbContextFactory<ApplicationDbContext> _dbFactory;
    private readonly IMemoryCache _cache;

    // Cache keys
    private const string AllMembersCacheKey = "GameNight:AllMembers";
    private const string RecentDatesCacheKeyPrefix = "GameNight:RecentDates:";

    // Cache durations
    private static readonly TimeSpan MembersCacheDuration = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan RecentDatesCacheDuration = TimeSpan.FromMinutes(5);

    public GameNightService(
        IDbContextFactory<ApplicationDbContext> dbFactory,
        IMemoryCache cache)
    {
        _dbFactory = dbFactory;
        _cache = cache;
    }

    private static IQueryable<GameNightEntity> WithDetails(IQueryable<GameNightEntity> query)
        => query
            .Include(n => n.Attendees)
            .ThenInclude(a => a.Member)
            .Include(n => n.Rsvps)
            .ThenInclude(r => r.Member)
            .Include(n => n.Games)
            .ThenInclude(g => g.Game)
            .Include(n => n.Games)
            .ThenInclude(g => g.Players)
            .ThenInclude(p => p.Member)
            .Include(n => n.Games)
            .ThenInclude(g => g.Teams)
            .Include(n => n.Games)
            .ThenInclude(g => g.WinnerMember)
            .Include(n => n.Games)
            .ThenInclude(g => g.Odds)
            .ThenInclude(o => o.Member)
            .Include(n => n.Games)
            .ThenInclude(g => g.Bets);

    // ===== Core CRUD Operations =====

    public async Task<GameNight?> GetByDateAsync(DateOnly date, CancellationToken ct = default)
    {
        var dateKey = ToDateKey(date);

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var entity = await WithDetails(db.GameNights)
            .AsNoTracking()
            .FirstOrDefaultAsync(n => n.DateKey == dateKey, ct);

        return entity is null ? null : ToDomain(entity);
    }

    public async Task<GameNight?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var entity = await WithDetails(db.GameNights)
            .AsNoTracking()
            .FirstOrDefaultAsync(n => n.Id == id, ct);

        return entity is null ? null : ToDomain(entity);
    }

    public async Task<GameNight> CreateAsync(DateOnly date, CancellationToken ct = default)
    {
        var dateKey = ToDateKey(date);

        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var existingId = await db.GameNights
            .AsNoTracking()
            .Where(n => n.DateKey == dateKey)
            .Select(n => (Guid?)n.Id)
            .FirstOrDefaultAsync(ct);

        if (existingId is { } id)
        {
            var existing = await GetByIdAsync(id, ct);
            if (existing is not null)
            {
                return existing;
            }
        }

        var created = new GameNightEntity
        {
            Id = Guid.NewGuid(),
            DateKey = dateKey
        };

        db.GameNights.Add(created);
        await db.SaveChangesAsync(ct);

        return (await GetByIdAsync(created.Id, ct))!;
    }

    public async Task<GameNight?> SetRecapAsync(Guid gameNightId, string? recap, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var entity = await db.GameNights
            .FirstOrDefaultAsync(n => n.Id == gameNightId, ct);

        if (entity is null)
        {
            return null;
        }

        recap = InputGuards.OptionalTrimToNull(recap, maxLength: 4_000, nameof(recap));
        entity.Recap = recap;
        await db.SaveChangesAsync(ct);

        return await GetByIdAsync(gameNightId, ct);
    }

    public async Task<GameNight?> SetHasStartedAsync(Guid gameNightId, bool hasStarted, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var entity = await db.GameNights
            .FirstOrDefaultAsync(n => n.Id == gameNightId, ct);

        if (entity is null)
        {
            return null;
        }

        entity.HasStarted = hasStarted;
        await db.SaveChangesAsync(ct);

        return await GetByIdAsync(gameNightId, ct);
    }

    // ===== Helper Methods =====

    public async Task<IReadOnlyList<MemberOption>> GetAllMembersAsync(CancellationToken ct = default)
    {
        return await _cache.GetOrCreateAsync(AllMembersCacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = MembersCacheDuration;
            
            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            var result = await db.Members
                .AsNoTracking()
                .Where(m => m.IsBgmMember)
                .OrderBy(m => m.Name)
                .Select(m => new MemberOption(m.Id, m.Name))
                .ToListAsync(ct);
            
            entry.Size = result.Count + 1;
            return (IReadOnlyList<MemberOption>)result;
        }) ?? [];
    }

    public async Task<IReadOnlyList<DateOnly>> GetRecentPastGameNightDatesAsync(DateOnly beforeDate, int take, CancellationToken ct = default)
    {
        if (take <= 0)
        {
            return Array.Empty<DateOnly>();
        }

        var cacheKey = $"{RecentDatesCacheKeyPrefix}{beforeDate:yyyyMMdd}:{take}";
        return await _cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = RecentDatesCacheDuration;
            
            var beforeKey = ToDateKey(beforeDate);

            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            var keys = await db.GameNights
                .AsNoTracking()
                .Where(n => n.DateKey < beforeKey)
                .OrderByDescending(n => n.DateKey)
                .Select(n => n.DateKey)
                .Take(take)
                .ToListAsync(ct);

            var result = (IReadOnlyList<DateOnly>)keys.Select(FromDateKey).OrderBy(d => d).ToArray();
            entry.Size = result.Count + 1;
            return result;
        }) ?? [];
    }

    // ===== Domain Mapping =====

    private static GameNight ToDomain(GameNightEntity entity)
    {
        var date = FromDateKey(entity.DateKey);

        var attendees = entity.Attendees
            .OrderBy(a => a.Member.Name)
            .Select(a => new GameNightAttendee(a.MemberId, a.Member.Name, a.SnackBrought))
            .ToArray();

        var rsvps = entity.Rsvps
            .OrderBy(r => r.Member.Name)
            .Select(r => new GameNightRsvp(r.MemberId, r.Member.Name, r.IsAttending))
            .ToArray();

        var games = entity.Games
            .OrderBy(g => g.Game.Name)
            .Select(g =>
            {
                var players = g.Players
                    .OrderBy(p => p.Member.Name)
                    .Select(p => new GameNightGamePlayer(p.MemberId, p.Member.Name, p.TeamName))
                    .ToArray();

                var odds = g.Odds
                    .OrderBy(o => o.Member.Name)
                    .Select(o => new GameNightGameOdds(o.MemberId, o.Member.Name, o.OddsTimes100))
                    .ToArray();

                var teams = g.Teams
                    .OrderBy(t => t.Id)
                    .Select(t => new GameNightGameTeam(t.TeamName, t.ColorHex))
                    .ToArray();

                GameNightWinner? winner = null;
                if (!string.IsNullOrWhiteSpace(g.WinnerTeamName))
                {
                    winner = new GameNightWinner(null, g.WinnerTeamName!, true);
                }
                else if (g.WinnerMemberId is { } w && g.WinnerMember is not null)
                {
                    winner = new GameNightWinner(w, g.WinnerMember.Name, false);
                }

                var hasBets = g.Bets.Count != 0;
                var isSettled = hasBets && g.Bets.All(b => b.IsResolved);

                return new GameNightGame(
                    g.Id,
                    g.GameId,
                    g.Game.Name,
                    g.IsPlayed,
                    g.IsConfirmed,
                    hasBets,
                    isSettled,
                    g.Game.AreScoresCountable,
                    g.Score,
                    g.IsHighScore,
                    g.Game.HighScore,
                    players,
                    odds,
                    teams,
                    winner);
            })
            .ToArray();

        return new GameNight(entity.Id, date, entity.Recap, entity.HasStarted, attendees, rsvps, games);
    }

    public static int ToDateKey(DateOnly date)
        => (date.Year * 10000) + (date.Month * 100) + date.Day;

    public static DateOnly FromDateKey(int dateKey)
    {
        var year = dateKey / 10000;
        var month = (dateKey / 100) % 100;
        var day = dateKey % 100;
        return new DateOnly(year, month, day);
    }

    // ===== Domain Records =====

    public sealed record GameNight(Guid Id, DateOnly Date, string? Recap, bool HasStarted, IReadOnlyList<GameNightAttendee> Attendees, IReadOnlyList<GameNightRsvp> Rsvps, IReadOnlyList<GameNightGame> Games);

    public sealed record GameNightAttendee(Guid MemberId, string MemberName, string? SnackBrought);

    public sealed record GameNightRsvp(Guid MemberId, string MemberName, bool IsAttending);

    public sealed record GameNightGame(int Id, Guid GameId, string GameName, bool IsPlayed, bool IsConfirmed, bool HasBets, bool IsSettled, bool AreScoresCountable, int? Score, bool IsHighScore, int? CurrentHighScore, IReadOnlyList<GameNightGamePlayer> Players, IReadOnlyList<GameNightGameOdds> Odds, IReadOnlyList<GameNightGameTeam> Teams, GameNightWinner? Winner);

    public sealed record GameNightGamePlayer(Guid MemberId, string MemberName, string? TeamName);

    public sealed record GameNightGameOdds(Guid MemberId, string MemberName, int OddsTimes100);

    public sealed record GameNightGameTeam(string TeamName, string? ColorHex);

    public sealed record GameNightWinner(Guid? MemberId, string Name, bool IsTeam);

    public sealed record MemberOption(Guid Id, string Name);
}
