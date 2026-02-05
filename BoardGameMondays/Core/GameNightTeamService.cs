using BoardGameMondays.Data;
using BoardGameMondays.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace BoardGameMondays.Core;

/// <summary>
/// Manages team-based games: team colors, team winners, players assigned to teams.
/// Also manages victory routes (custom scoring/achievement systems for individual games).
/// </summary>
public sealed class GameNightTeamService
{
    private readonly IDbContextFactory<ApplicationDbContext> _dbFactory;
    private readonly GameNightService _gameNightService;

    public GameNightTeamService(
        IDbContextFactory<ApplicationDbContext> dbFactory,
        GameNightService gameNightService)
    {
        _dbFactory = dbFactory;
        _gameNightService = gameNightService;
    }

    public async Task<GameNightService.GameNight?> SetWinnerAsync(Guid gameNightId, int gameNightGameId, Guid? winnerMemberId, int? score, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var game = await db.GameNightGames
            .Include(g => g.Game)
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
            return await _gameNightService.GetByIdAsync(gameNightId, ct);
        }

        if (winnerMemberId is null)
        {
            game.WinnerMemberId = null;
            game.WinnerTeamName = null;
            game.IsPlayed = true;
            game.Score = null;
            game.IsHighScore = false;
            await db.SaveChangesAsync(ct);
            return await _gameNightService.GetByIdAsync(gameNightId, ct);
        }

        var isPlayer = await db.GameNightGamePlayers
            .AnyAsync(p => p.GameNightGameId == gameNightGameId && p.MemberId == winnerMemberId.Value, ct);

        if (!isPlayer)
        {
            return await _gameNightService.GetByIdAsync(gameNightId, ct);
        }

        game.WinnerMemberId = winnerMemberId;
        game.WinnerTeamName = null;
        game.IsPlayed = true;

        if (game.Game.AreScoresCountable)
        {
            game.Score = score;

            if (score.HasValue)
            {
                var currentHigh = game.Game.HighScore;
                var isNewHigh = !currentHigh.HasValue || score.Value > currentHigh.Value;
                game.IsHighScore = isNewHigh;

                if (isNewHigh)
                {
                    game.Game.HighScore = score.Value;
                    game.Game.HighScoreMemberId = winnerMemberId;
                    game.Game.HighScoreAchievedOn = DateTimeOffset.UtcNow;

                    var winner = await db.Members
                        .AsNoTracking()
                        .FirstOrDefaultAsync(m => m.Id == winnerMemberId.Value, ct);
                    game.Game.HighScoreMemberName = winner?.Name ?? "Unknown";
                }
            }
            else
            {
                game.IsHighScore = false;
            }
        }
        else
        {
            game.Score = null;
            game.IsHighScore = false;
        }

        await db.SaveChangesAsync(ct);
        return await _gameNightService.GetByIdAsync(gameNightId, ct);
    }

    public async Task<GameNightService.GameNight?> SetTeamWinnerAsync(Guid gameNightId, int gameNightGameId, string? teamName, int? score, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var game = await db.GameNightGames
            .Include(g => g.Game)
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
            return await _gameNightService.GetByIdAsync(gameNightId, ct);
        }

        teamName = InputGuards.OptionalTrimToNull(teamName, maxLength: 64, nameof(teamName));
        if (teamName is null)
        {
            game.WinnerTeamName = null;
            game.WinnerMemberId = null;
            game.IsPlayed = true;
            game.Score = null;
            game.IsHighScore = false;
            await db.SaveChangesAsync(ct);
            return await _gameNightService.GetByIdAsync(gameNightId, ct);
        }

        var exists = await db.GameNightGamePlayers
            .AsNoTracking()
            .AnyAsync(p => p.GameNightGameId == gameNightGameId && p.TeamName == teamName, ct);

        if (!exists)
        {
            return await _gameNightService.GetByIdAsync(gameNightId, ct);
        }

        game.WinnerTeamName = teamName;
        game.WinnerMemberId = null;
        game.IsPlayed = true;

        if (game.Game.AreScoresCountable)
        {
            game.Score = score;

            if (score.HasValue)
            {
                var currentHigh = game.Game.HighScore;
                var isNewHigh = !currentHigh.HasValue || score.Value > currentHigh.Value;
                game.IsHighScore = isNewHigh;

                if (isNewHigh)
                {
                    game.Game.HighScore = score.Value;
                    game.Game.HighScoreMemberId = null;
                    game.Game.HighScoreAchievedOn = DateTimeOffset.UtcNow;
                    game.Game.HighScoreMemberName = teamName;
                }
            }
            else
            {
                game.IsHighScore = false;
            }
        }
        else
        {
            game.Score = null;
            game.IsHighScore = false;
        }

        await db.SaveChangesAsync(ct);
        return await _gameNightService.GetByIdAsync(gameNightId, ct);
    }

    public async Task<GameNightService.GameNight?> SetPlayerTeamAsync(Guid gameNightId, int gameNightGameId, Guid memberId, string? teamName, CancellationToken ct = default)
    {
        teamName = string.IsNullOrWhiteSpace(teamName) ? null : teamName.Trim();

        if (teamName is not null && teamName.Length > 32)
        {
            teamName = teamName.Substring(0, 32);
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var player = await db.GameNightGamePlayers
            .FirstOrDefaultAsync(p => p.GameNightGameId == gameNightGameId && p.MemberId == memberId, ct);

        if (player is null)
        {
            return null;
        }

        player.TeamName = teamName;
        await db.SaveChangesAsync(ct);

        return await _gameNightService.GetByIdAsync(gameNightId, ct);
    }

    public async Task<GameNightService.GameNight?> SetTeamColorAsync(Guid gameNightId, int gameNightGameId, string teamName, string? colorHex, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(teamName)) return await _gameNightService.GetByIdAsync(gameNightId, ct);

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
                return await _gameNightService.GetByIdAsync(gameNightId, ct);
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
        return await _gameNightService.GetByIdAsync(gameNightId, ct);
    }

    public async Task<IReadOnlyList<VictoryRouteTemplate>> GetVictoryRoutesForGameAsync(Guid gameId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var routes = await db.VictoryRoutes
            .AsNoTracking()
            .Include(r => r.Options)
            .Where(r => r.GameId == gameId)
            .OrderBy(r => r.SortOrder)
            .ToListAsync(ct);

        return routes
            .Select(r => new VictoryRouteTemplate(
                r.Id,
                r.GameId,
                r.Name,
                (VictoryRouteType)r.Type,
                r.IsRequired,
                r.SortOrder,
                r.Options
                    .OrderBy(o => o.SortOrder)
                    .Select(o => new VictoryRouteTemplateOption(o.Id, o.VictoryRouteId, o.Value, o.SortOrder))
                    .ToArray()))
            .ToList();
    }

    public async Task<IReadOnlyList<VictoryRouteValue>> GetVictoryRouteValuesAsync(int gameNightGameId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var values = await db.GameNightGameVictoryRouteValues
            .AsNoTracking()
            .Where(v => v.GameNightGameId == gameNightGameId)
            .ToListAsync(ct);

        return values
            .Select(v => new VictoryRouteValue(v.VictoryRouteId, v.ValueString, v.ValueBool))
            .ToList();
    }

    public async Task<bool> UpsertVictoryRouteValuesAsync(Guid gameNightId, int gameNightGameId, IReadOnlyList<VictoryRouteValue> values, CancellationToken ct = default)
    {
        if (values is null)
        {
            throw new ArgumentNullException(nameof(values));
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var game = await db.GameNightGames
            .AsNoTracking()
            .FirstOrDefaultAsync(g => g.Id == gameNightGameId && g.GameNightId == gameNightId, ct);

        if (game is null)
        {
            return false;
        }

        var routeIds = values.Select(v => v.VictoryRouteId).Distinct().ToArray();

        var candidateIds = await db.VictoryRoutes
            .AsNoTracking()
            .Where(r => r.GameId == game.GameId)
            .Select(r => r.Id)
            .ToListAsync(ct);

        var validRouteIds = candidateIds.Intersect(routeIds).ToList();

        foreach (var v in values)
        {
            if (!validRouteIds.Contains(v.VictoryRouteId))
            {
                continue;
            }

            var existing = await db.GameNightGameVictoryRouteValues
                .FirstOrDefaultAsync(x => x.GameNightGameId == gameNightGameId && x.VictoryRouteId == v.VictoryRouteId, ct);

            var valueString = InputGuards.OptionalTrimToNull(v.ValueString, maxLength: 256, nameof(v.ValueString));

            if (existing is null)
            {
                db.GameNightGameVictoryRouteValues.Add(new GameNightGameVictoryRouteValueEntity
                {
                    GameNightGameId = gameNightGameId,
                    VictoryRouteId = v.VictoryRouteId,
                    ValueString = valueString,
                    ValueBool = v.ValueBool
                });
            }
            else
            {
                existing.ValueString = valueString;
                existing.ValueBool = v.ValueBool;
            }
        }

        await db.SaveChangesAsync(ct);
        return true;
    }

}
