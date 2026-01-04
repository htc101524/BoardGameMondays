using BoardGameMondays.Data;
using BoardGameMondays.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace BoardGameMondays.Core;

public sealed class GameNightService
{
    private readonly ApplicationDbContext _db;

    public GameNightService(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<GameNight?> GetByDateAsync(DateOnly date, CancellationToken ct = default)
    {
        var dateKey = ToDateKey(date);

        var entity = await _db.GameNights
            .AsNoTracking()
            .Include(n => n.Attendees)
            .ThenInclude(a => a.Member)
            .Include(n => n.Games)
            .ThenInclude(g => g.Game)
            .Include(n => n.Games)
            .ThenInclude(g => g.Players)
            .ThenInclude(p => p.Member)
            .Include(n => n.Games)
            .ThenInclude(g => g.WinnerMember)
            .FirstOrDefaultAsync(n => n.DateKey == dateKey, ct);

        return entity is null ? null : ToDomain(entity);
    }

    public async Task<GameNight> CreateAsync(DateOnly date, CancellationToken ct = default)
    {
        var dateKey = ToDateKey(date);

        var existingId = await _db.GameNights
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

        _db.GameNights.Add(created);
        await _db.SaveChangesAsync(ct);

        // Reload with includes for consistent return shape.
        return (await GetByIdAsync(created.Id, ct))!;
    }

    public async Task<GameNight?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await _db.GameNights
            .AsNoTracking()
            .Include(n => n.Attendees)
            .ThenInclude(a => a.Member)
            .Include(n => n.Games)
            .ThenInclude(g => g.Game)
            .Include(n => n.Games)
            .ThenInclude(g => g.Players)
            .ThenInclude(p => p.Member)
            .Include(n => n.Games)
            .ThenInclude(g => g.WinnerMember)
            .FirstOrDefaultAsync(n => n.Id == id, ct);

        return entity is null ? null : ToDomain(entity);
    }

    public async Task<GameNight?> SetAttendingAsync(Guid gameNightId, Guid memberId, bool attending, CancellationToken ct = default)
    {
        var exists = await _db.GameNights.AnyAsync(n => n.Id == gameNightId, ct);
        if (!exists)
        {
            return null;
        }

        var existing = await _db.GameNightAttendees
            .FirstOrDefaultAsync(a => a.GameNightId == gameNightId && a.MemberId == memberId, ct);

        if (attending)
        {
            if (existing is null)
            {
                _db.GameNightAttendees.Add(new GameNightAttendeeEntity
                {
                    GameNightId = gameNightId,
                    MemberId = memberId,
                    CreatedOn = DateTimeOffset.UtcNow
                });

                await _db.SaveChangesAsync(ct);
            }
        }
        else
        {
            if (existing is not null)
            {
                _db.GameNightAttendees.Remove(existing);
                await _db.SaveChangesAsync(ct);
            }
        }

        return await GetByIdAsync(gameNightId, ct);
    }

    public async Task<GameNight?> AddGameAsync(Guid gameNightId, Guid gameId, bool isPlayed, CancellationToken ct = default)
    {
        var exists = await _db.GameNights.AnyAsync(n => n.Id == gameNightId, ct);
        if (!exists)
        {
            return null;
        }

        var already = await _db.GameNightGames
            .AnyAsync(g => g.GameNightId == gameNightId && g.GameId == gameId, ct);

        if (!already)
        {
            _db.GameNightGames.Add(new GameNightGameEntity
            {
                GameNightId = gameNightId,
                GameId = gameId,
                IsPlayed = isPlayed,
                CreatedOn = DateTimeOffset.UtcNow
            });

            await _db.SaveChangesAsync(ct);
        }
        else if (isPlayed)
        {
            var existing = await _db.GameNightGames
                .FirstOrDefaultAsync(g => g.GameNightId == gameNightId && g.GameId == gameId, ct);

            if (existing is not null && !existing.IsPlayed)
            {
                existing.IsPlayed = true;
                await _db.SaveChangesAsync(ct);
            }
        }

        return await GetByIdAsync(gameNightId, ct);
    }

    public Task<GameNight?> AddPlannedGameAsync(Guid gameNightId, Guid gameId, CancellationToken ct = default)
        => AddGameAsync(gameNightId, gameId, isPlayed: false, ct);

    public async Task<GameNight?> AddPlayerAsync(Guid gameNightId, int gameNightGameId, Guid memberId, CancellationToken ct = default)
    {
        var exists = await _db.GameNightGames
            .AnyAsync(g => g.Id == gameNightGameId && g.GameNightId == gameNightId, ct);
        if (!exists)
        {
            return null;
        }

        var already = await _db.GameNightGamePlayers
            .AnyAsync(p => p.GameNightGameId == gameNightGameId && p.MemberId == memberId, ct);

        if (!already)
        {
            _db.GameNightGamePlayers.Add(new GameNightGamePlayerEntity
            {
                GameNightGameId = gameNightGameId,
                MemberId = memberId,
                CreatedOn = DateTimeOffset.UtcNow
            });
            await _db.SaveChangesAsync(ct);
        }

        return await GetByIdAsync(gameNightId, ct);
    }

    public async Task<GameNight?> RemovePlayerAsync(Guid gameNightId, int gameNightGameId, Guid memberId, CancellationToken ct = default)
    {
        var player = await _db.GameNightGamePlayers
            .FirstOrDefaultAsync(p => p.GameNightGameId == gameNightGameId && p.MemberId == memberId, ct);

        if (player is not null)
        {
            _db.GameNightGamePlayers.Remove(player);

            var game = await _db.GameNightGames.FirstOrDefaultAsync(g => g.Id == gameNightGameId && g.GameNightId == gameNightId, ct);
            if (game is not null && game.WinnerMemberId == memberId)
            {
                game.WinnerMemberId = null;
            }

            await _db.SaveChangesAsync(ct);
        }

        return await GetByIdAsync(gameNightId, ct);
    }

    public async Task<GameNight?> SetWinnerAsync(Guid gameNightId, int gameNightGameId, Guid? winnerMemberId, CancellationToken ct = default)
    {
        var game = await _db.GameNightGames
            .FirstOrDefaultAsync(g => g.Id == gameNightGameId && g.GameNightId == gameNightId, ct);

        if (game is null)
        {
            return null;
        }

        if (winnerMemberId is null)
        {
            game.WinnerMemberId = null;
            await _db.SaveChangesAsync(ct);
            return await GetByIdAsync(gameNightId, ct);
        }

        var isPlayer = await _db.GameNightGamePlayers
            .AnyAsync(p => p.GameNightGameId == gameNightGameId && p.MemberId == winnerMemberId.Value, ct);

        if (!isPlayer)
        {
            // Winner must be one of the players.
            return await GetByIdAsync(gameNightId, ct);
        }

        game.WinnerMemberId = winnerMemberId;
        await _db.SaveChangesAsync(ct);
        return await GetByIdAsync(gameNightId, ct);
    }

    public async Task<IReadOnlyList<MemberOption>> GetAllMembersAsync(CancellationToken ct = default)
        => await _db.Members
            .AsNoTracking()
            .OrderBy(m => m.Name)
            .Select(m => new MemberOption(m.Id, m.Name))
            .ToListAsync(ct);

    private static GameNight ToDomain(GameNightEntity entity)
    {
        var date = FromDateKey(entity.DateKey);

        var attendees = entity.Attendees
            .OrderBy(a => a.Member.Name)
            .Select(a => new GameNightAttendee(a.MemberId, a.Member.Name))
            .ToArray();

        var games = entity.Games
            .OrderBy(g => g.Game.Name)
            .Select(g =>
            {
                var players = g.Players
                    .OrderBy(p => p.Member.Name)
                    .Select(p => new GameNightGamePlayer(p.MemberId, p.Member.Name))
                    .ToArray();

                var winner = g.WinnerMemberId is { } w && g.WinnerMember is not null
                    ? new GameNightWinner(w, g.WinnerMember.Name)
                    : null;

                return new GameNightGame(g.Id, g.GameId, g.Game.Name, g.IsPlayed, players, winner);
            })
            .ToArray();

        return new GameNight(entity.Id, date, attendees, games);
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

    public sealed record GameNight(Guid Id, DateOnly Date, IReadOnlyList<GameNightAttendee> Attendees, IReadOnlyList<GameNightGame> Games);

    public sealed record GameNightAttendee(Guid MemberId, string MemberName);

    public sealed record GameNightGame(int Id, Guid GameId, string GameName, bool IsPlayed, IReadOnlyList<GameNightGamePlayer> Players, GameNightWinner? Winner);

    public sealed record GameNightGamePlayer(Guid MemberId, string MemberName);

    public sealed record GameNightWinner(Guid MemberId, string MemberName);

    public sealed record MemberOption(Guid Id, string Name);
}
