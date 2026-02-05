using BoardGameMondays.Data;
using BoardGameMondays.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace BoardGameMondays.Core;

/// <summary>
/// Manages game composition: adding/removing games and players for a game night.
/// Separate service for clarity: focused on game and player roster management.
/// </summary>
public sealed class GameNightPlayerService
{
    private readonly IDbContextFactory<ApplicationDbContext> _dbFactory;
    private readonly GameNightService _gameNightService;
    private readonly OddsService _oddsService;

    public GameNightPlayerService(
        IDbContextFactory<ApplicationDbContext> dbFactory,
        GameNightService gameNightService,
        OddsService oddsService)
    {
        _dbFactory = dbFactory;
        _gameNightService = gameNightService;
        _oddsService = oddsService;
    }

    public async Task<GameNightService.GameNight?> RemoveGameAsync(Guid gameNightId, int gameNightGameId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var game = await db.GameNightGames
            .FirstOrDefaultAsync(g => g.Id == gameNightGameId && g.GameNightId == gameNightId, ct);

        if (game is null || game.IsConfirmed)
        {
            return null;
        }

        // Cascade delete: odds, bets, players, teams, victory route values
        db.GameNightGames.Remove(game);
        await db.SaveChangesAsync(ct);

        return await _gameNightService.GetByIdAsync(gameNightId, ct);
    }

    public async Task<GameNightService.GameNight?> AddGameAsync(Guid gameNightId, Guid gameId, bool isPlayed, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var exists = await db.GameNights.AnyAsync(n => n.Id == gameNightId, ct);
        if (!exists)
        {
            return null;
        }

        // Allow multiple instances of the same game on the same night
        db.GameNightGames.Add(new GameNightGameEntity
        {
            GameNightId = gameNightId,
            GameId = gameId,
            IsPlayed = isPlayed,
            CreatedOn = DateTimeOffset.UtcNow
        });

        await db.SaveChangesAsync(ct);

        return await _gameNightService.GetByIdAsync(gameNightId, ct);
    }

    public Task<GameNightService.GameNight?> AddPlannedGameAsync(Guid gameNightId, Guid gameId, CancellationToken ct = default)
        => AddGameAsync(gameNightId, gameId, isPlayed: false, ct);

    public async Task<GameNightService.GameNight?> AddPlayerAsync(Guid gameNightId, int gameNightGameId, Guid memberId, CancellationToken ct = default)
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
            return await _gameNightService.GetByIdAsync(gameNightId, ct);
        }

        // Only allow players who are marked as attending for this night.
        var isAttending = await db.GameNightAttendees
            .AsNoTracking()
            .AnyAsync(a => a.GameNightId == game.GameNightId && a.MemberId == memberId, ct);

        if (!isAttending)
        {
            return await _gameNightService.GetByIdAsync(gameNightId, ct);
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

        return await _gameNightService.GetByIdAsync(gameNightId, ct);
    }

    public async Task<GameNightService.GameNight?> RemovePlayerAsync(Guid gameNightId, int gameNightGameId, Guid memberId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var game = await db.GameNightGames
            .FirstOrDefaultAsync(g => g.Id == gameNightGameId && g.GameNightId == gameNightId, ct);

        if (game is null || game.IsConfirmed)
        {
            return null;
        }

        var player = await db.GameNightGamePlayers
            .FirstOrDefaultAsync(p => p.GameNightGameId == gameNightGameId && p.MemberId == memberId, ct);

        if (player is null)
        {
            return await _gameNightService.GetByIdAsync(gameNightId, ct);
        }

        db.GameNightGamePlayers.Remove(player);
        await db.SaveChangesAsync(ct);

        return await _gameNightService.GetByIdAsync(gameNightId, ct);
    }

    /// <summary>
    /// Removes a confirmed game from a game night. This is only allowed if:
    /// - No winner has been set yet
    /// - No bets have been resolved (paid out)
    /// All unresolved bets will be refunded before removal.
    /// </summary>
    public async Task<CancelConfirmedGameResult> CancelConfirmedGameAsync(Guid gameNightId, int gameNightGameId, BettingService bettingService, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var game = await db.GameNightGames
            .FirstOrDefaultAsync(g => g.Id == gameNightGameId && g.GameNightId == gameNightId, ct);

        if (game is null)
        {
            return new CancelConfirmedGameResult(CancelConfirmedGameStatus.NotFound, null);
        }

        // Only allow canceling confirmed games.
        if (!game.IsConfirmed)
        {
            return new CancelConfirmedGameResult(CancelConfirmedGameStatus.NotConfirmed, await _gameNightService.GetByIdAsync(gameNightId, ct));
        }

        // Don't allow if winner is already set (IsPlayed indicates result was confirmed).
        if (game.IsPlayed || game.WinnerMemberId is not null || game.WinnerTeamName is not null)
        {
            return new CancelConfirmedGameResult(CancelConfirmedGameStatus.WinnerAlreadySet, await _gameNightService.GetByIdAsync(gameNightId, ct));
        }

        // Check if any bets have already been resolved.
        var hasResolvedBets = await db.GameNightGameBets
            .AsNoTracking()
            .AnyAsync(b => b.GameNightGameId == gameNightGameId && b.IsResolved, ct);

        if (hasResolvedBets)
        {
            return new CancelConfirmedGameResult(CancelConfirmedGameStatus.BetsAlreadyResolved, await _gameNightService.GetByIdAsync(gameNightId, ct));
        }

        // Use execution strategy for production database resilience (required for Azure SQL retry logic).
        var strategy = db.Database.CreateExecutionStrategy();

        var cancelResult = await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            // Refund all unresolved bets using the same db context (participates in transaction).
            var result = await bettingService.CancelGameBetsAsync(db, gameNightGameId, ct);
            if (result == BettingService.CancelBetsResult.AlreadyResolved)
            {
                await tx.RollbackAsync(ct);
                return BettingService.CancelBetsResult.AlreadyResolved;
            }

            // Remove the game within the same transaction.
            db.GameNightGames.Remove(game);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            return BettingService.CancelBetsResult.Ok;
        });

        if (cancelResult == BettingService.CancelBetsResult.AlreadyResolved)
        {
            return new CancelConfirmedGameResult(CancelConfirmedGameStatus.BetsAlreadyResolved, await _gameNightService.GetByIdAsync(gameNightId, ct));
        }

        return new CancelConfirmedGameResult(CancelConfirmedGameStatus.Ok, await _gameNightService.GetByIdAsync(gameNightId, ct));
    }

    public enum CancelConfirmedGameStatus
    {
        Ok,
        NotFound,
        NotConfirmed,
        WinnerAlreadySet,
        BetsAlreadyResolved
    }

    public sealed record CancelConfirmedGameResult(CancelConfirmedGameStatus Status, GameNightService.GameNight? Night);

    public async Task<GameNightService.GameNight?> ConfirmGameAsync(Guid gameNightId, int gameNightGameId, CancellationToken ct = default)
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

            // Auto-generate initial odds based on player ratings
            await _oddsService.GenerateInitialOddsAsync(gameNightGameId, ct);
        }

        return await _gameNightService.GetByIdAsync(gameNightId, ct);
    }
}
