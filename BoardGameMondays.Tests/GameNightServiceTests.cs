using BoardGameMondays.Core;
using Microsoft.Extensions.Caching.Memory;
using Xunit;

namespace BoardGameMondays.Tests;

public sealed class GameNightServiceTests
{
    [Fact]
    public async Task SetRsvpAsync_AddsAttendee_WhenAttending()
    {
        using var factory = new TestDbFactory();
        await using (var db = factory.CreateDbContext())
        {
            TestData.AddMember(db, "Alice", isBgmMember: true);
        }

        var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 100 });
        var gameNightService = new GameNightService(factory, cache);
        var rsvpService = new GameNightRsvpService(factory, gameNightService);

        var night = await gameNightService.CreateAsync(new DateOnly(2026, 2, 4));

        await using var db2 = factory.CreateDbContext();
        var memberId = db2.Members.Select(m => m.Id).Single();

        var updated = await rsvpService.SetRsvpAsync(night.Id, memberId, attending: true);

        Assert.NotNull(updated);
        Assert.Single(updated!.Attendees);
        Assert.Single(updated.Rsvps);
        Assert.True(updated.Rsvps.Single().IsAttending);
    }

    [Fact]
    public async Task SetRsvpAsync_RemovesAttendee_WhenNotAttending()
    {
        using var factory = new TestDbFactory();
        await using (var db = factory.CreateDbContext())
        {
            TestData.AddMember(db, "Alice", isBgmMember: true);
        }

        var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 100 });
        var gameNightService = new GameNightService(factory, cache);
        var rsvpService = new GameNightRsvpService(factory, gameNightService);

        var night = await gameNightService.CreateAsync(new DateOnly(2026, 2, 4));

        await using var db2 = factory.CreateDbContext();
        var memberId = db2.Members.Select(m => m.Id).Single();

        await rsvpService.SetRsvpAsync(night.Id, memberId, attending: true);
        var updated = await rsvpService.SetRsvpAsync(night.Id, memberId, attending: false);

        Assert.NotNull(updated);
        Assert.Empty(updated!.Attendees);
        Assert.Single(updated.Rsvps);
        Assert.False(updated.Rsvps.Single().IsAttending);
    }

    [Fact]
    public async Task CreateAsync_CreatesNewGameNight()
    {
        using var factory = new TestDbFactory();
        var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 100 });
        var service = new GameNightService(factory, cache);

        var date = new DateOnly(2026, 2, 11);
        var night = await service.CreateAsync(date);

        Assert.NotNull(night);
        Assert.NotEqual(Guid.Empty, night.Id);
        Assert.Empty(night.Attendees);
        Assert.Empty(night.Games);
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsGameNight()
    {
        using var factory = new TestDbFactory();
        Guid nightId;

        await using (var db = factory.CreateDbContext())
        {
            var night = TestData.AddGameNight(db, new DateOnly(2026, 2, 4));
            nightId = night.Id;
        }

        var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 100 });
        var service = new GameNightService(factory, cache);

        var retrieved = await service.GetByIdAsync(nightId);

        Assert.NotNull(retrieved);
        Assert.Equal(nightId, retrieved.Id);
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsNull_WhenGameNightNotFound()
    {
        using var factory = new TestDbFactory();
        var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 100 });
        var service = new GameNightService(factory, cache);

        var retrieved = await service.GetByIdAsync(Guid.NewGuid());

        Assert.Null(retrieved);
    }

    [Fact]
    public async Task AddGameAsync_AddsGameToGameNight()
    {
        using var factory = new TestDbFactory();
        Guid nightId;
        Guid gameId;

        await using (var db = factory.CreateDbContext())
        {
            var night = TestData.AddGameNight(db, new DateOnly(2026, 2, 4));
            var game = TestData.AddGame(db, "Catan");
            nightId = night.Id;
            gameId = game.Id;
        }

        var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 100 });
        var gameNightService = new GameNightService(factory, cache);
        var ranking = new RankingService(factory);
        var odds = new OddsService(factory, ranking);
        var playerService = new GameNightPlayerService(factory, gameNightService, odds);

        var updated = await playerService.AddGameAsync(nightId, gameId, isPlayed: false);

        Assert.NotNull(updated);
        Assert.NotEmpty(updated!.Games);
        Assert.Contains(updated.Games, g => g.GameId == gameId);
    }

    [Fact]
    public async Task RemoveGameAsync_RemovesGameFromGameNight()
    {
        using var factory = new TestDbFactory();
        Guid nightId;
        int gameNightGameId;

        await using (var db = factory.CreateDbContext())
        {
            var night = TestData.AddGameNight(db, new DateOnly(2026, 2, 4));
            var game = TestData.AddGame(db, "Catan");
            var nightGame = TestData.AddGameNightGame(db, night, game);
            nightId = night.Id;
            gameNightGameId = nightGame.Id;
        }

        var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 100 });
        var gameNightService = new GameNightService(factory, cache);
        var ranking = new RankingService(factory);
        var odds = new OddsService(factory, ranking);
        var playerService = new GameNightPlayerService(factory, gameNightService, odds);

        var updated = await playerService.RemoveGameAsync(nightId, gameNightGameId);

        Assert.NotNull(updated);
        Assert.Empty(updated!.Games);
    }

    [Fact]
    public async Task SetWinnerAsync_UpdatesGameWinner()
    {
        using var factory = new TestDbFactory();
        Guid memberId;
        Guid nightId;
        int gameNightGameId;

        await using (var db = factory.CreateDbContext())
        {
            var night = TestData.AddGameNight(db, new DateOnly(2026, 2, 4));
            var game = TestData.AddGame(db, "Catan");
            var member = TestData.AddMember(db, "Alice");
            var nightGame = TestData.AddGameNightGame(db, night, game);
            
            nightId = night.Id;
            gameNightGameId = nightGame.Id;
            memberId = member.Id;

            // Add player to the game
            db.GameNightGamePlayers.Add(new BoardGameMondays.Data.Entities.GameNightGamePlayerEntity
            {
                GameNightGameId = nightGame.Id,
                MemberId = member.Id,
                CreatedOn = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 100 });
        var gameNightService = new GameNightService(factory, cache);
        var teamService = new GameNightTeamService(factory, gameNightService);

        var updated = await teamService.SetWinnerAsync(nightId, gameNightGameId, memberId, score: null);

        Assert.NotNull(updated);
        var updatedGame = updated.Games.FirstOrDefault(g => g.Id == gameNightGameId);
        Assert.NotNull(updatedGame);
        Assert.NotNull(updatedGame!.Winner);
        Assert.Equal(memberId, updatedGame.Winner.MemberId);
    }
}
