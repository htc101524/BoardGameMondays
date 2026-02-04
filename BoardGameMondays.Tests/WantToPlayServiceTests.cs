using BoardGameMondays.Core;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BoardGameMondays.Tests;

public sealed class WantToPlayServiceTests
{
    [Fact]
    public async Task VoteAsync_AllowsUpToWeeklyLimit()
    {
        using var factory = new TestDbFactory();
        await using var db = factory.CreateDbContext();

        var game1 = TestData.AddGame(db, "Game 1");
        var game2 = TestData.AddGame(db, "Game 2");
        var game3 = TestData.AddGame(db, "Game 3");
        var game4 = TestData.AddGame(db, "Game 4");

        var service = new WantToPlayService(factory, NullLogger<WantToPlayService>.Instance);
        var today = new DateOnly(2026, 2, 4);

        var r1 = await service.VoteAsync("user-1", game1.Id, today);
        var r2 = await service.VoteAsync("user-1", game2.Id, today);
        var r3 = await service.VoteAsync("user-1", game3.Id, today);
        var r4 = await service.VoteAsync("user-1", game4.Id, today);

        Assert.True(r1.Success);
        Assert.True(r2.Success);
        Assert.True(r3.Success);
        Assert.False(r4.Success);
    }

    [Fact]
    public async Task VoteAsync_PreventsDuplicateVoteForSameGame()
    {
        using var factory = new TestDbFactory();
        await using var db = factory.CreateDbContext();

        var game = TestData.AddGame(db, "Game 1");

        var service = new WantToPlayService(factory, NullLogger<WantToPlayService>.Instance);
        var today = new DateOnly(2026, 2, 4);

        var first = await service.VoteAsync("user-1", game.Id, today);
        var second = await service.VoteAsync("user-1", game.Id, today);

        Assert.True(first.Success);
        Assert.False(second.Success);
    }

    [Fact]
    public async Task GetUserStatusAsync_ReturnsRemainingVotes()
    {
        using var factory = new TestDbFactory();
        await using var db = factory.CreateDbContext();

        var game1 = TestData.AddGame(db, "Game 1");
        var game2 = TestData.AddGame(db, "Game 2");

        var service = new WantToPlayService(factory, NullLogger<WantToPlayService>.Instance);
        var today = new DateOnly(2026, 2, 4);

        await service.VoteAsync("user-1", game1.Id, today);
        await service.VoteAsync("user-1", game2.Id, today);

        var status = await service.GetUserStatusAsync("user-1", today);

        Assert.Equal(1, status.VotesRemaining);
        Assert.Equal(3, status.WeeklyLimit);
        Assert.Equal(2, status.VotedGameIds.Count);
    }
}
