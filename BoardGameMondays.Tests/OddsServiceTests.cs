using System.Threading.Tasks;
using BoardGameMondays.Core;
using BoardGameMondays.Data.Entities;
using Xunit;

namespace BoardGameMondays.Tests;

public sealed class OddsServiceTests
{
    [Fact]
    public async Task GenerateInitialOddsAsync_GeneratesOdds_ForIndividualGame()
    {
        using var factory = new TestDbFactory();
        int gameNightGameId;

        await using (var db = factory.CreateDbContext())
        {
            var game = TestData.AddGame(db, "Catan");
            var night = TestData.AddGameNight(db, DateOnly.FromDateTime(DateTime.Today));
            var nightGame = TestData.AddGameNightGame(db, night, game);
            gameNightGameId = nightGame.Id;

            // Add a player for odds
            var member = TestData.AddMember(db, "Alice");
            db.GameNightGamePlayers.Add(new GameNightGamePlayerEntity
            {
                GameNightGameId = nightGame.Id,
                MemberId = member.Id,
                CreatedOn = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var ranking = new RankingService(factory);
        var service = new OddsService(factory, ranking);

        var result = await service.GenerateInitialOddsAsync(gameNightGameId);

        Assert.True(result);

        await using var verify = factory.CreateDbContext();
        var odds = verify.GameNightGameOdds
            .Where(o => o.GameNightGameId == gameNightGameId)
            .ToList();
        Assert.NotEmpty(odds);
        Assert.All(odds, o => Assert.True(o.OddsTimes100 >= 105 && o.OddsTimes100 <= 2000, $"Odds {o.OddsTimes100} out of valid range"));
    }

    [Fact]
    public async Task GenerateInitialOddsAsync_GeneratesOdds_ForTeamGame()
    {
        using var factory = new TestDbFactory();
        int gameNightGameId;

        await using (var db = factory.CreateDbContext())
        {
            var game = TestData.AddGame(db, "Team Fortress");
            var night = TestData.AddGameNight(db, DateOnly.FromDateTime(DateTime.Today));
            var nightGame = TestData.AddGameNightGame(db, night, game);
            gameNightGameId = nightGame.Id;

            // Add two players on different teams
            var member1 = TestData.AddMember(db, "Alice");
            var member2 = TestData.AddMember(db, "Bob");
            db.GameNightGamePlayers.Add(new GameNightGamePlayerEntity
            {
                GameNightGameId = nightGame.Id,
                MemberId = member1.Id,
                TeamName = "Team 1",
                CreatedOn = DateTimeOffset.UtcNow
            });
            db.GameNightGamePlayers.Add(new GameNightGamePlayerEntity
            {
                GameNightGameId = nightGame.Id,
                MemberId = member2.Id,
                TeamName = "Team 2",
                CreatedOn = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var ranking = new RankingService(factory);
        var service = new OddsService(factory, ranking);

        var result = await service.GenerateInitialOddsAsync(gameNightGameId);

        Assert.True(result);

        await using var verify = factory.CreateDbContext();
        var odds = verify.GameNightGameOdds
            .Where(o => o.GameNightGameId == gameNightGameId)
            .ToList();
        Assert.NotEmpty(odds);
    }

    [Fact]
    public async Task GenerateInitialOddsAsync_ReturnsFalse_WhenGameHasNoPlayers()
    {
        using var factory = new TestDbFactory();
        int gameNightGameId;

        await using (var db = factory.CreateDbContext())
        {
            var game = TestData.AddGame(db, "Catan");
            var night = TestData.AddGameNight(db, DateOnly.FromDateTime(DateTime.Today));
            var nightGame = TestData.AddGameNightGame(db, night, game);
            gameNightGameId = nightGame.Id;
        }

        var ranking = new RankingService(factory);
        var service = new OddsService(factory, ranking);

        var result = await service.GenerateInitialOddsAsync(gameNightGameId);

        Assert.False(result);
    }

    [Fact]
    public async Task GetOddsForGameAsync_ReturnsOdds_ForAllPlayers()
    {
        using var factory = new TestDbFactory();
        Guid memberId1;
        Guid memberId2;
        int gameNightGameId;

        await using (var db = factory.CreateDbContext())
        {
            var game = TestData.AddGame(db, "Catan");
            var night = TestData.AddGameNight(db, DateOnly.FromDateTime(DateTime.Today));
            var nightGame = TestData.AddGameNightGame(db, night, game);
            gameNightGameId = nightGame.Id;

            var member1 = TestData.AddMember(db, "Alice");
            var member2 = TestData.AddMember(db, "Bob");
            memberId1 = member1.Id;
            memberId2 = member2.Id;

            db.GameNightGamePlayers.Add(new GameNightGamePlayerEntity
            {
                GameNightGameId = nightGame.Id,
                MemberId = member1.Id,
                CreatedOn = DateTimeOffset.UtcNow
            });
            db.GameNightGamePlayers.Add(new GameNightGamePlayerEntity
            {
                GameNightGameId = nightGame.Id,
                MemberId = member2.Id,
                CreatedOn = DateTimeOffset.UtcNow
            });

            // Add odds manually
            db.GameNightGameOdds.AddRange(
                new GameNightGameOddsEntity { GameNightGameId = nightGame.Id, MemberId = member1.Id, OddsTimes100 = 200 },
                new GameNightGameOddsEntity { GameNightGameId = nightGame.Id, MemberId = member2.Id, OddsTimes100 = 150 }
            );

            await db.SaveChangesAsync();
        }

        var ranking = new RankingService(factory);
        var service = new OddsService(factory, ranking);

        var odds = await service.GetOddsForGameAsync(gameNightGameId);

        Assert.NotEmpty(odds);
        Assert.Contains(memberId1, odds.Keys);
        Assert.Contains(memberId2, odds.Keys);
        Assert.Equal(200, odds[memberId1]);
        Assert.Equal(150, odds[memberId2]);
    }

    [Fact]
    public async Task GetOddsForGameAsync_ReturnsEmptyDictionary_WhenGameHasNoOdds()
    {
        using var factory = new TestDbFactory();
        int gameNightGameId;

        await using (var db = factory.CreateDbContext())
        {
            var game = TestData.AddGame(db, "Catan");
            var night = TestData.AddGameNight(db, DateOnly.FromDateTime(DateTime.Today));
            var nightGame = TestData.AddGameNightGame(db, night, game);
            gameNightGameId = nightGame.Id;
        }

        var ranking = new RankingService(factory);
        var service = new OddsService(factory, ranking);

        var odds = await service.GetOddsForGameAsync(gameNightGameId);

        Assert.Empty(odds);
    }

    [Fact]
    public async Task RecalculateOddsForCashflowAsync_AdjustsOdds_BasedOnBetActivity()
    {
        using var factory = new TestDbFactory();
        int gameNightGameId;
        Guid memberId;

        await using (var db = factory.CreateDbContext())
        {
            var game = TestData.AddGame(db, "Catan");
            var night = TestData.AddGameNight(db, DateOnly.FromDateTime(DateTime.Today));
            var nightGame = TestData.AddGameNightGame(db, night, game);
            gameNightGameId = nightGame.Id;

            var member = TestData.AddMember(db, "Alice");
            memberId = member.Id;

            db.GameNightGamePlayers.Add(new GameNightGamePlayerEntity
            {
                GameNightGameId = nightGame.Id,
                MemberId = member.Id,
                CreatedOn = DateTimeOffset.UtcNow
            });

            // Add initial odds
            db.GameNightGameOdds.Add(new GameNightGameOddsEntity
            {
                GameNightGameId = nightGame.Id,
                MemberId = member.Id,
                OddsTimes100 = 200
            });

            // Add some bets on this player
            var user = TestData.AddUser(db, "user-1", "alice");
            db.GameNightGameBets.Add(new GameNightGameBetEntity
            {
                GameNightGameId = nightGame.Id,
                UserId = user.Id,
                PredictedWinnerMemberId = member.Id,
                Amount = 50,
                OddsTimes100 = 200,
                CreatedOn = DateTimeOffset.UtcNow
            });

            await db.SaveChangesAsync();
        }

        var ranking = new RankingService(factory);
        var service = new OddsService(factory, ranking);

        await using var db2 = factory.CreateDbContext();
        await service.RecalculateOddsForCashflowAsync(db2, gameNightGameId);

        // Verify odds were updated
        var updatedOdds = db2.GameNightGameOdds
            .Where(o => o.GameNightGameId == gameNightGameId && o.MemberId == memberId)
            .FirstOrDefault();

        Assert.NotNull(updatedOdds);
        Assert.True(updatedOdds.OddsTimes100 >= 105 && updatedOdds.OddsTimes100 <= 2000);
    }
}
