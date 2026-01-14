using BoardGameMondays.Data;
using BoardGameMondays.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace BoardGameMondays.Core;

public sealed class GameNightService
{
    private readonly IDbContextFactory<ApplicationDbContext> _dbFactory;

    public GameNightService(IDbContextFactory<ApplicationDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
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

    public async Task<GameNight?> GetByDateAsync(DateOnly date, CancellationToken ct = default)
    {
        var dateKey = ToDateKey(date);

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var entity = await WithDetails(db.GameNights)
            .AsNoTracking()
            .FirstOrDefaultAsync(n => n.DateKey == dateKey, ct);

        return entity is null ? null : ToDomain(entity);
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

        // Reload with includes for consistent return shape.
        return (await GetByIdAsync(created.Id, ct))!;
    }

    public async Task<GameNight?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var entity = await WithDetails(db.GameNights)
            .AsNoTracking()
            .FirstOrDefaultAsync(n => n.Id == id, ct);

        return entity is null ? null : ToDomain(entity);
    }

    /// <summary>
    /// Updates a member's RSVP for this night.
    /// - Records explicit "not going" and "going" decisions.
    /// - Keeps the Attendees list in sync (IsAttending=true => attendee exists; false => attendee removed).
    /// </summary>
    public async Task<GameNight?> SetRsvpAsync(Guid gameNightId, Guid memberId, bool attending, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var exists = await db.GameNights.AnyAsync(n => n.Id == gameNightId, ct);
        if (!exists)
        {
            return null;
        }

        var now = DateTimeOffset.UtcNow;

        var rsvp = await db.GameNightRsvps
            .FirstOrDefaultAsync(r => r.GameNightId == gameNightId && r.MemberId == memberId, ct);

        if (rsvp is null)
        {
            rsvp = new GameNightRsvpEntity
            {
                GameNightId = gameNightId,
                MemberId = memberId,
                IsAttending = attending,
                CreatedOn = now
            };
            db.GameNightRsvps.Add(rsvp);
        }
        else
        {
            rsvp.IsAttending = attending;
            rsvp.CreatedOn = now;
        }

        // Keep attendee table in sync.
        var existing = await db.GameNightAttendees
            .FirstOrDefaultAsync(a => a.GameNightId == gameNightId && a.MemberId == memberId, ct);

        if (attending)
        {
            if (existing is null)
            {
                db.GameNightAttendees.Add(new GameNightAttendeeEntity
                {
                    GameNightId = gameNightId,
                    MemberId = memberId,
                    CreatedOn = now
                });
            }
        }
        else
        {
            if (existing is not null)
            {
                db.GameNightAttendees.Remove(existing);
            }
        }

        await db.SaveChangesAsync(ct);
        return await GetByIdAsync(gameNightId, ct);
    }

    /// <summary>
    /// Updates attendance without changing RSVP intent.
    /// Intended for admin after-the-fact corrections on past nights.
    /// </summary>
    public async Task<GameNight?> SetAttendanceAsync(Guid gameNightId, Guid memberId, bool attending, bool respectExplicitDeclines = false, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var exists = await db.GameNights.AnyAsync(n => n.Id == gameNightId, ct);
        if (!exists)
        {
            return null;
        }

        if (attending && respectExplicitDeclines)
        {
            var declined = await db.GameNightRsvps
                .AsNoTracking()
                .AnyAsync(r => r.GameNightId == gameNightId && r.MemberId == memberId && !r.IsAttending, ct);

            if (declined)
            {
                return await GetByIdAsync(gameNightId, ct);
            }
        }

        var now = DateTimeOffset.UtcNow;
        var existing = await db.GameNightAttendees
            .FirstOrDefaultAsync(a => a.GameNightId == gameNightId && a.MemberId == memberId, ct);

        if (attending)
        {
            if (existing is null)
            {
                db.GameNightAttendees.Add(new GameNightAttendeeEntity
                {
                    GameNightId = gameNightId,
                    MemberId = memberId,
                    CreatedOn = now
                });
                await db.SaveChangesAsync(ct);
            }
        }
        else
        {
            if (existing is not null)
            {
                db.GameNightAttendees.Remove(existing);
                await db.SaveChangesAsync(ct);
            }
        }

        return await GetByIdAsync(gameNightId, ct);
    }

    public async Task<GameNight?> SetSnackBroughtAsync(Guid gameNightId, Guid memberId, string? snackBrought, CancellationToken ct = default)
    {
        snackBrought = InputGuards.OptionalTrimToNull(snackBrought, maxLength: 128, nameof(snackBrought));

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var exists = await db.GameNights.AnyAsync(n => n.Id == gameNightId, ct);
        if (!exists)
        {
            return null;
        }

        var attendee = await db.GameNightAttendees
            .FirstOrDefaultAsync(a => a.GameNightId == gameNightId && a.MemberId == memberId, ct);

        if (attendee is null)
        {
            // Snack is only tracked for attending members.
            return await GetByIdAsync(gameNightId, ct);
        }

        attendee.SnackBrought = snackBrought;
        await db.SaveChangesAsync(ct);
        return await GetByIdAsync(gameNightId, ct);
    }

    // Back-compat: older callers use SetAttendingAsync for RSVP.
    public Task<GameNight?> SetAttendingAsync(Guid gameNightId, Guid memberId, bool attending, CancellationToken ct = default)
        => SetRsvpAsync(gameNightId, memberId, attending, ct);

    public async Task<GameNight?> RemoveGameAsync(Guid gameNightId, int gameNightGameId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var game = await db.GameNightGames
            .FirstOrDefaultAsync(g => g.Id == gameNightGameId && g.GameNightId == gameNightId, ct);

        if (game is null)
        {
            return null;
        }

        // Safety: don't allow removing confirmed games.
        if (game.IsConfirmed)
        {
            return await GetByIdAsync(gameNightId, ct);
        }

        db.GameNightGames.Remove(game);
        await db.SaveChangesAsync(ct);
        return await GetByIdAsync(gameNightId, ct);
    }

    public async Task<GameNight?> AddGameAsync(Guid gameNightId, Guid gameId, bool isPlayed, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var exists = await db.GameNights.AnyAsync(n => n.Id == gameNightId, ct);
        if (!exists)
        {
            return null;
        }

        var already = await db.GameNightGames
            .AnyAsync(g => g.GameNightId == gameNightId && g.GameId == gameId, ct);

        if (!already)
        {
            db.GameNightGames.Add(new GameNightGameEntity
            {
                GameNightId = gameNightId,
                GameId = gameId,
                IsPlayed = isPlayed,
                CreatedOn = DateTimeOffset.UtcNow
            });

            await db.SaveChangesAsync(ct);
        }
        else if (isPlayed)
        {
            var existing = await db.GameNightGames
                .FirstOrDefaultAsync(g => g.GameNightId == gameNightId && g.GameId == gameId, ct);

            if (existing is not null && !existing.IsPlayed)
            {
                existing.IsPlayed = true;
                await db.SaveChangesAsync(ct);
            }
        }

        return await GetByIdAsync(gameNightId, ct);
    }

    public Task<GameNight?> AddPlannedGameAsync(Guid gameNightId, Guid gameId, CancellationToken ct = default)
        => AddGameAsync(gameNightId, gameId, isPlayed: false, ct);

    public async Task<GameNight?> AddPlayerAsync(Guid gameNightId, int gameNightGameId, Guid memberId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var game = await db.GameNightGames
            .Include(g => g.GameNight)
            .FirstOrDefaultAsync(g => g.Id == gameNightGameId && g.GameNightId == gameNightId, ct);
        if (game is null)
        {
            return null;
        }

        if (game.IsConfirmed)
        {
            return await GetByIdAsync(gameNightId, ct);
        }

        // Only allow players who are marked as attending for this night.
        var isAttending = await db.GameNightAttendees
            .AsNoTracking()
            .AnyAsync(a => a.GameNightId == game.GameNightId && a.MemberId == memberId, ct);

        if (!isAttending)
        {
            return await GetByIdAsync(gameNightId, ct);
        }

        var already = await db.GameNightGamePlayers
            .AnyAsync(p => p.GameNightGameId == gameNightGameId && p.MemberId == memberId, ct);

        if (!already)
        {
            db.GameNightGamePlayers.Add(new GameNightGamePlayerEntity
            {
                GameNightGameId = gameNightGameId,
                MemberId = memberId,
                CreatedOn = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync(ct);
        }

        return await GetByIdAsync(gameNightId, ct);
    }

    public async Task<GameNight?> RemovePlayerAsync(Guid gameNightId, int gameNightGameId, Guid memberId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var game = await db.GameNightGames
            .FirstOrDefaultAsync(g => g.Id == gameNightGameId && g.GameNightId == gameNightId, ct);

        if (game is null)
        {
            return null;
        }

        if (game.IsConfirmed)
        {
            return await GetByIdAsync(gameNightId, ct);
        }

        var player = await db.GameNightGamePlayers
            .FirstOrDefaultAsync(p => p.GameNightGameId == gameNightGameId && p.MemberId == memberId, ct);

        if (player is not null)
        {
            db.GameNightGamePlayers.Remove(player);

            if (game.WinnerMemberId == memberId)
            {
                game.WinnerMemberId = null;
            }

            await db.SaveChangesAsync(ct);
        }

        return await GetByIdAsync(gameNightId, ct);
    }

    public async Task<GameNight?> SetWinnerAsync(Guid gameNightId, int gameNightGameId, Guid? winnerMemberId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var game = await db.GameNightGames
            .FirstOrDefaultAsync(g => g.Id == gameNightGameId && g.GameNightId == gameNightId, ct);

        if (game is null)
        {
            return null;
        }

        // Once any bets have been resolved for this game, the winner is locked.
        var isLocked = await db.GameNightGameBets
            .AsNoTracking()
            .AnyAsync(b => b.GameNightGameId == gameNightGameId && b.IsResolved, ct);
        if (isLocked)
        {
            return await GetByIdAsync(gameNightId, ct);
        }

        if (winnerMemberId is null)
        {
            game.WinnerMemberId = null;
            game.WinnerTeamName = null;
            await db.SaveChangesAsync(ct);
            return await GetByIdAsync(gameNightId, ct);
        }

        var isPlayer = await db.GameNightGamePlayers
            .AnyAsync(p => p.GameNightGameId == gameNightGameId && p.MemberId == winnerMemberId.Value, ct);

        if (!isPlayer)
        {
            // Winner must be one of the players.
            return await GetByIdAsync(gameNightId, ct);
        }

        game.WinnerMemberId = winnerMemberId;
        game.WinnerTeamName = null;
        await db.SaveChangesAsync(ct);
        return await GetByIdAsync(gameNightId, ct);
    }

    public async Task<GameNight?> SetPlayerTeamAsync(Guid gameNightId, int gameNightGameId, Guid memberId, string? teamName, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var game = await db.GameNightGames
            .FirstOrDefaultAsync(g => g.Id == gameNightGameId && g.GameNightId == gameNightId, ct);

        if (game is null)
        {
            return null;
        }

        if (game.IsConfirmed)
        {
            return await GetByIdAsync(gameNightId, ct);
        }

        var player = await db.GameNightGamePlayers
            .FirstOrDefaultAsync(p => p.GameNightGameId == gameNightGameId && p.MemberId == memberId, ct);

        if (player is null)
        {
            return await GetByIdAsync(gameNightId, ct);
        }

        teamName = InputGuards.OptionalTrimToNull(teamName, maxLength: 64, nameof(teamName));
        player.TeamName = teamName;

        await db.SaveChangesAsync(ct);
        return await GetByIdAsync(gameNightId, ct);
    }

    public async Task<GameNight?> SetTeamWinnerAsync(Guid gameNightId, int gameNightGameId, string? teamName, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var game = await db.GameNightGames
            .FirstOrDefaultAsync(g => g.Id == gameNightGameId && g.GameNightId == gameNightId, ct);

        if (game is null)
        {
            return null;
        }

        var isLocked = await db.GameNightGameBets
            .AsNoTracking()
            .AnyAsync(b => b.GameNightGameId == gameNightGameId && b.IsResolved, ct);
        if (isLocked)
        {
            return await GetByIdAsync(gameNightId, ct);
        }

        teamName = InputGuards.OptionalTrimToNull(teamName, maxLength: 64, nameof(teamName));
        if (teamName is null)
        {
            game.WinnerTeamName = null;
            game.WinnerMemberId = null;
            await db.SaveChangesAsync(ct);
            return await GetByIdAsync(gameNightId, ct);
        }

        var exists = await db.GameNightGamePlayers
            .AsNoTracking()
            .AnyAsync(p => p.GameNightGameId == gameNightGameId && p.TeamName == teamName, ct);

        if (!exists)
        {
            return await GetByIdAsync(gameNightId, ct);
        }

        game.WinnerTeamName = teamName;
        game.WinnerMemberId = null;

        await db.SaveChangesAsync(ct);
        return await GetByIdAsync(gameNightId, ct);
    }

    public async Task<GameNight?> SetTeamColorAsync(Guid gameNightId, int gameNightGameId, string teamName, string? colorHex, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(teamName)) return await GetByIdAsync(gameNightId, ct);

        teamName = teamName.Trim();
        colorHex = string.IsNullOrWhiteSpace(colorHex) ? null : colorHex.Trim();

        if (colorHex is not null && colorHex.Length > 16)
        {
            colorHex = colorHex.Substring(0, 16);
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var game = await db.GameNightGames
            .FirstOrDefaultAsync(g => g.Id == gameNightGameId && g.GameNightId == gameNightId, ct);

        if (game is null)
        {
            return null;
        }

        // Ensure a record exists for this team on this game-night-game.
        var existing = await db.GameNightGameTeams
            .FirstOrDefaultAsync(t => t.GameNightGameId == gameNightGameId && t.TeamName == teamName, ct);

        if (existing is null)
        {
            if (colorHex is null)
            {
                // nothing to do
                return await GetByIdAsync(gameNightId, ct);
            }

            existing = new GameNightGameTeamEntity
            {
                GameNightGameId = gameNightGameId,
                TeamName = teamName,
                ColorHex = colorHex
            };

            db.GameNightGameTeams.Add(existing);
        }
        else
        {
            if (colorHex is null)
            {
                // Treat null as "use default/background"; remove the row to keep the table clean.
                db.GameNightGameTeams.Remove(existing);
            }
            else
            {
                existing.ColorHex = colorHex;
            }
        }

        await db.SaveChangesAsync(ct);
        return await GetByIdAsync(gameNightId, ct);
    }

    public async Task<IReadOnlyList<MemberOption>> GetAllMembersAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.Members
            .AsNoTracking()
            .Where(m => m.IsBgmMember)
            .OrderBy(m => m.Name)
            .Select(m => new MemberOption(m.Id, m.Name))
            .ToListAsync(ct);
    }

    public async Task<GameNight?> ConfirmGameAsync(Guid gameNightId, int gameNightGameId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var game = await db.GameNightGames
            .FirstOrDefaultAsync(g => g.Id == gameNightGameId && g.GameNightId == gameNightId, ct);

        if (game is null)
        {
            return null;
        }

        if (!game.IsConfirmed)
        {
            game.IsConfirmed = true;
            await db.SaveChangesAsync(ct);
        }

        return await GetByIdAsync(gameNightId, ct);
    }

    public async Task<GameNight?> SetOddsAsync(Guid gameNightId, int gameNightGameId, Guid memberId, int oddsTimes100, CancellationToken ct = default)
    {
        if (oddsTimes100 < 101 || oddsTimes100 > 10000)
        {
            return await GetByIdAsync(gameNightId, ct);
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var game = await db.GameNightGames
            .FirstOrDefaultAsync(g => g.Id == gameNightGameId && g.GameNightId == gameNightId, ct);
        if (game is null)
        {
            return null;
        }

        var player = await db.GameNightGamePlayers
            .FirstOrDefaultAsync(p => p.GameNightGameId == gameNightGameId && p.MemberId == memberId, ct);
        if (player is null)
        {
            return await GetByIdAsync(gameNightId, ct);
        }
        var teamName = player.TeamName;

        if (!string.IsNullOrWhiteSpace(teamName))
        {
            var memberIds = await db.GameNightGamePlayers
                .AsNoTracking()
                .Where(p => p.GameNightGameId == gameNightGameId && p.TeamName == teamName)
                .Select(p => p.MemberId)
                .ToListAsync(ct);

            var existingAll = await db.GameNightGameOdds
                .Where(o => o.GameNightGameId == gameNightGameId && memberIds.Contains(o.MemberId))
                .ToListAsync(ct);

            var existingByMember = existingAll.ToDictionary(o => o.MemberId, o => o);

            foreach (var id in memberIds)
            {
                if (!existingByMember.TryGetValue(id, out var odds))
                {
                    odds = new GameNightGameOddsEntity
                    {
                        GameNightGameId = gameNightGameId,
                        MemberId = id,
                        CreatedOn = DateTimeOffset.UtcNow
                    };
                    db.GameNightGameOdds.Add(odds);
                }

                odds.OddsTimes100 = oddsTimes100;
            }
        }
        else
        {
            var existing = await db.GameNightGameOdds
                .FirstOrDefaultAsync(o => o.GameNightGameId == gameNightGameId && o.MemberId == memberId, ct);

            if (existing is null)
            {
                db.GameNightGameOdds.Add(new GameNightGameOddsEntity
                {
                    GameNightGameId = gameNightGameId,
                    MemberId = memberId,
                    OddsTimes100 = oddsTimes100,
                    CreatedOn = DateTimeOffset.UtcNow
                });
            }
            else
            {
                existing.OddsTimes100 = oddsTimes100;
            }
        }

        await db.SaveChangesAsync(ct);
        return await GetByIdAsync(gameNightId, ct);
    }

    public async Task<IReadOnlyList<DateOnly>> GetRecentPastGameNightDatesAsync(DateOnly beforeDate, int take, CancellationToken ct = default)
    {
        if (take <= 0)
        {
            return Array.Empty<DateOnly>();
        }

        var beforeKey = ToDateKey(beforeDate);

        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var keys = await db.GameNights
            .AsNoTracking()
            .Where(n => n.DateKey < beforeKey)
            .OrderByDescending(n => n.DateKey)
            .Select(n => n.DateKey)
            .Take(take)
            .ToListAsync(ct);

        return keys.Select(FromDateKey).OrderBy(d => d).ToArray();
    }

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

                var isSettled = g.Bets.Count == 0 || g.Bets.All(b => b.IsResolved);

                return new GameNightGame(g.Id, g.GameId, g.Game.Name, g.IsPlayed, g.IsConfirmed, isSettled, players, odds, teams, winner);
            })
            .ToArray();

        return new GameNight(entity.Id, date, entity.Recap, attendees, rsvps, games);
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

    public sealed record GameNight(Guid Id, DateOnly Date, string? Recap, IReadOnlyList<GameNightAttendee> Attendees, IReadOnlyList<GameNightRsvp> Rsvps, IReadOnlyList<GameNightGame> Games);

    public sealed record GameNightAttendee(Guid MemberId, string MemberName, string? SnackBrought);

    public sealed record GameNightRsvp(Guid MemberId, string MemberName, bool IsAttending);

    public sealed record GameNightGame(int Id, Guid GameId, string GameName, bool IsPlayed, bool IsConfirmed, bool IsSettled, IReadOnlyList<GameNightGamePlayer> Players, IReadOnlyList<GameNightGameOdds> Odds, IReadOnlyList<GameNightGameTeam> Teams, GameNightWinner? Winner);

    public sealed record GameNightGamePlayer(Guid MemberId, string MemberName, string? TeamName);

    public sealed record GameNightGameOdds(Guid MemberId, string MemberName, int OddsTimes100);

    public sealed record GameNightGameTeam(string TeamName, string? ColorHex);

    public sealed record GameNightWinner(Guid? MemberId, string Name, bool IsTeam);

    public sealed record MemberOption(Guid Id, string Name);
}
