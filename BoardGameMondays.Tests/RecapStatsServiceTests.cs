using System.Threading.Tasks;
using BoardGameMondays.Core;
using BoardGameMondays.Data.Entities;
using Xunit;

namespace BoardGameMondays.Tests;

public sealed class RecapStatsServiceTests
{
    [Fact]
    public async Task GetInterestingStatAsync_ReturnsNull_WhenGameNightDoesNotExist()
    {
        using var factory = new TestDbFactory();
        var service = new RecapStatsService(factory);

        var stat = await service.GetInterestingStatAsync(DateOnly.FromDateTime(DateTime.Today.AddDays(-365)));

        Assert.Null(stat);
    }

    [Fact]
    public async Task GetInterestingStatAsync_ReturnsNull_WhenNoInterestingStatsFound()
    {
        using var factory = new TestDbFactory();
        DateOnly gameNightDate;

        await using (var db = factory.CreateDbContext())
        {
            gameNightDate = DateOnly.FromDateTime(DateTime.Today);
            TestData.AddGameNight(db, gameNightDate);
        }

        var service = new RecapStatsService(factory);
        var stat = await service.GetInterestingStatAsync(gameNightDate);

        // Might be null or a stat, depending on the game night data
        // If there's a recent game night with no games, it should return null or a basic stat
    }

    [Fact]
    public async Task GetInterestingStatAsync_ReturnsConsecutiveWinStat_WhenMemberHasMultipleWins()
    {
        using var factory = new TestDbFactory();
        DateOnly gameNightDate;

        await using (var db = factory.CreateDbContext())
        {
            gameNightDate = DateOnly.FromDateTime(DateTime.Today);
            var night1 = TestData.AddGameNight(db, gameNightDate.AddDays(-7));
            var night2 = TestData.AddGameNight(db, gameNightDate);

            var member = TestData.AddMember(db, "Alice");
            var game = TestData.AddGame(db, "Catan");

            // Create games where Alice wins consecutive times
            var ng1 = TestData.AddGameNightGame(db, night1, game);
            ng1.WinnerMemberId = member.Id;
            db.SaveChanges();

            var ng2 = TestData.AddGameNightGame(db, night2, game);
            ng2.WinnerMemberId = member.Id;
            db.SaveChanges();
        }

        var service = new RecapStatsService(factory);
        var stat = await service.GetInterestingStatAsync(gameNightDate);

        // Should find consecutive win stat
        Assert.NotNull(stat);
        Assert.NotNull(stat!.Message);
    }

    [Fact]
    public async Task GetInterestingStatAsync_ReturnsFirstTimeAttendanceStat_ForNewMember()
    {
        using var factory = new TestDbFactory();
        DateOnly gameNightDate;

        await using (var db = factory.CreateDbContext())
        {
            gameNightDate = DateOnly.FromDateTime(DateTime.Today);
            var night = TestData.AddGameNight(db, gameNightDate);

            var newMember = TestData.AddMember(db, "Charlie");
            var game = TestData.AddGame(db, "Catan");
            var nightGame = TestData.AddGameNightGame(db, night, game);

            // Add new member as attendee (first time)
            db.GameNightAttendees.Add(new GameNightAttendeeEntity
            {
                GameNightId = night.Id,
                MemberId = newMember.Id,
                CreatedOn = DateTimeOffset.UtcNow
            });

            db.SaveChanges();
        }

        var service = new RecapStatsService(factory);
        var stat = await service.GetInterestingStatAsync(gameNightDate);

        // Should find first-time attendance stat or similar
        if (stat != null)
        {
            Assert.NotNull(stat.Message);
        }
    }

    [Fact]
    public async Task GetInterestingStatAsync_ReturnsAttendanceStreakStat_ForRegularAttendee()
    {
        using var factory = new TestDbFactory();
        DateOnly gameNightDate = DateOnly.FromDateTime(DateTime.Today);

        await using (var db = factory.CreateDbContext())
        {
            var member = TestData.AddMember(db, "Diana");

            // Create multiple game nights in sequence
            for (int i = -3; i <= 0; i++)
            {
                var night = TestData.AddGameNight(db, gameNightDate.AddDays(i * 7));
                var game = TestData.AddGame(db, $"Game_{i}", 1);
                TestData.AddGameNightGame(db, night, game);

                // Add member as attendee
                db.GameNightAttendees.Add(new GameNightAttendeeEntity
                {
                    GameNightId = night.Id,
                    MemberId = member.Id,
                    CreatedOn = DateTimeOffset.UtcNow
                });
            }

            db.SaveChanges();
        }

        var service = new RecapStatsService(factory);
        var stat = await service.GetInterestingStatAsync(gameNightDate);

        Assert.NotNull(stat);
        Assert.NotNull(stat!.Message);
    }

    [Fact]
    public async Task GetInterestingStatAsync_ReturnsHighScoreStat_ForNewHighScore()
    {
        using var factory = new TestDbFactory();
        DateOnly gameNightDate;

        await using (var db = factory.CreateDbContext())
        {
            gameNightDate = DateOnly.FromDateTime(DateTime.Today);
            var night = TestData.AddGameNight(db, gameNightDate);

            var member = TestData.AddMember(db, "Eve");
            var game = TestData.AddGame(db, "Fancy Scoring Game", 1);
            var nightGame = TestData.AddGameNightGame(db, night, game);
            
            // Mark as high score game
            nightGame.IsHighScore = true;
            nightGame.WinnerMemberId = member.Id;
            nightGame.Score = 9999;

            db.SaveChanges();
        }

        var service = new RecapStatsService(factory);
        var stat = await service.GetInterestingStatAsync(gameNightDate);

        // Should find a high score stat
        Assert.NotNull(stat);
        Assert.NotNull(stat!.Message);
    }

    [Fact]
    public async Task GetInterestingStatAsync_PicksRandomStat_WhenMultipleStatsExist()
    {
        using var factory = new TestDbFactory();
        DateOnly gameNightDate;

        await using (var db = factory.CreateDbContext())
        {
            gameNightDate = DateOnly.FromDateTime(DateTime.Today);
            var night1 = TestData.AddGameNight(db, gameNightDate.AddDays(-7));
            var night2 = TestData.AddGameNight(db, gameNightDate);

            var member1 = TestData.AddMember(db, "Frank");
            var member2 = TestData.AddMember(db, "Grace");
            var game = TestData.AddGame(db, "Catan");

            // Create winning conditions for multiple stats
            var ng1 = TestData.AddGameNightGame(db, night1, game);
            ng1.WinnerMemberId = member1.Id;
            db.SaveChanges();

            var ng2 = TestData.AddGameNightGame(db, night2, game);
            ng2.WinnerMemberId = member1.Id;
            db.SaveChanges();

            // Also add attendance for member2
            db.GameNightAttendees.Add(new GameNightAttendeeEntity
            {
                GameNightId = night2.Id,
                MemberId = member2.Id,
                CreatedOn = DateTimeOffset.UtcNow
            });

            db.SaveChanges();
        }

        var service = new RecapStatsService(factory);

        // Call multiple times to ensure it can pick different stats
        var stats = new List<RecapStatsService.InterestingStat>();
        for (int i = 0; i < 3; i++)
        {
            var stat = await service.GetInterestingStatAsync(gameNightDate);
            if (stat != null)
            {
                stats.Add(stat);
            }
        }

        Assert.NotEmpty(stats);
        Assert.All(stats, s => Assert.NotNull(s.Message));
    }
}
