using BoardGameMondays.Core;
using BoardGameMondays.Data;
using BoardGameMondays.Data.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace BoardGameMondays.Tests;

public sealed class TestDbFactory : IDbContextFactory<ApplicationDbContext>, IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<ApplicationDbContext> _options;

    public TestDbFactory()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        _options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .Options;

        using var db = new ApplicationDbContext(_options);
        db.Database.EnsureCreated();
    }

    public ApplicationDbContext CreateDbContext() => new(_options);

    public void Dispose() => _connection.Dispose();
}

public static class TestData
{
    public static ApplicationUser AddUser(ApplicationDbContext db, string userId, string userName, int coins = 100)
    {
        var user = new ApplicationUser
        {
            Id = userId,
            UserName = userName,
            NormalizedUserName = userName.ToUpperInvariant(),
            BgmCoins = coins
        };

        db.Users.Add(user);
        db.SaveChanges();
        return user;
    }

    public static MemberEntity AddMember(ApplicationDbContext db, string name, bool isBgmMember = true)
    {
        var member = new MemberEntity
        {
            Id = Guid.NewGuid(),
            Name = name,
            IsBgmMember = isBgmMember
        };

        db.Members.Add(member);
        db.SaveChanges();
        return member;
    }

    public static BoardGameEntity AddGame(ApplicationDbContext db, string name, int status = 1)
    {
        var game = new BoardGameEntity
        {
            Id = Guid.NewGuid(),
            Name = name,
            Status = status,
            AreScoresCountable = false
        };

        db.Games.Add(game);
        db.SaveChanges();
        return game;
    }

    public static GameNightEntity AddGameNight(ApplicationDbContext db, DateOnly date)
    {
        var night = new GameNightEntity
        {
            Id = Guid.NewGuid(),
            DateKey = (date.Year * 10000) + (date.Month * 100) + date.Day
        };

        db.GameNights.Add(night);
        db.SaveChanges();
        return night;
    }

    public static GameNightGameEntity AddGameNightGame(ApplicationDbContext db, GameNightEntity night, BoardGameEntity game)
    {
        var entity = new GameNightGameEntity
        {
            GameNightId = night.Id,
            GameId = game.Id,
            IsPlayed = true,
            IsConfirmed = true,
            IsHighScore = false,
            CreatedOn = DateTimeOffset.UtcNow
        };

        db.GameNightGames.Add(entity);
        db.SaveChanges();
        return entity;
    }
}
