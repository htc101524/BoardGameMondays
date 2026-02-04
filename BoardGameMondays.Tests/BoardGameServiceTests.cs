using BoardGameMondays.Core;
using Microsoft.Extensions.Caching.Memory;
using Xunit;

namespace BoardGameMondays.Tests;

public sealed class BoardGameServiceTests
{
    [Fact]
    public async Task AddGameAsync_PersistsGame()
    {
        using var factory = new TestDbFactory();
        var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 100 });
        var service = new BoardGameService(factory, cache);

        var created = await service.AddGameAsync("Heat", GameStatus.Playing, tagline: "Fast", imageUrl: null);

        Assert.Equal("Heat", created.Name);
        Assert.Equal(GameStatus.Playing, created.Status);

        await using var db = factory.CreateDbContext();
        Assert.Single(db.Games.ToList());
    }

    [Fact]
    public async Task UpdateGameAsync_ChangesFields()
    {
        using var factory = new TestDbFactory();
        var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 100 });
        var service = new BoardGameService(factory, cache);

        var created = await service.AddGameAsync("Old", GameStatus.Playing, tagline: "Old", imageUrl: null);

        var updated = await service.UpdateGameAsync(
            created.Id,
            name: "New",
            status: GameStatus.Decided,
            tagline: "New",
            imageUrl: null,
            minPlayers: 2,
            maxPlayers: 4,
            runtimeMinutes: 60,
            firstPlayRuntimeMinutes: 90,
            complexity: 2.5,
            boardGameGeekScore: 7.8,
            boardGameGeekUrl: null,
            areScoresCountable: false);

        Assert.NotNull(updated);
        Assert.Equal("New", updated!.Name);
        Assert.Equal(GameStatus.Decided, updated.Status);
    }

    [Fact]
    public async Task AddReviewAsync_CreatesReview()
    {
        using var factory = new TestDbFactory();
        var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 100 });
        var service = new BoardGameService(factory, cache);

        var game = await service.AddGameAsync("Heat", GameStatus.Playing);

        var review = new MemberReview(new DemoBgmMember("Alice"), rating: 4.5, description: "Great!");
        var updated = await service.AddReviewAsync(game.Id, review);

        Assert.NotNull(updated);
        Assert.Single(updated!.Reviews);
    }

    [Fact]
    public async Task VictoryRoutes_CanAddAndRemove()
    {
        using var factory = new TestDbFactory();
        var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 100 });
        var service = new BoardGameService(factory, cache);

        var game = await service.AddGameAsync("Heat", GameStatus.Playing);
        var withRoute = await service.AddVictoryRouteAsync(game.Id, "VP", VictoryRouteType.Dropdown, isRequired: true);

        Assert.NotNull(withRoute);
        var route = withRoute!.VictoryRoutes.Single();

        var withOption = await service.AddVictoryRouteOptionAsync(game.Id, route.Id, "50");
        Assert.NotNull(withOption);
        Assert.Single(withOption!.VictoryRoutes.Single().Options);

        var removedOption = await service.RemoveVictoryRouteOptionAsync(game.Id, route.Id, withOption.VictoryRoutes.Single().Options.Single().Id);
        Assert.NotNull(removedOption);
        Assert.Empty(removedOption!.VictoryRoutes.Single().Options);

        var removedRoute = await service.RemoveVictoryRouteAsync(game.Id, route.Id);
        Assert.NotNull(removedRoute);
        Assert.Empty(removedRoute!.VictoryRoutes);
    }
}
