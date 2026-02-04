using BoardGameMondays.Core;
using BoardGameMondays.Data.Entities;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace BoardGameMondays.Tests;

public sealed class ShopServiceTests
{
    [Fact]
    public async Task PurchaseItemAsync_DeductsCoins_AndCreatesPurchase()
    {
        using var factory = new TestDbFactory();
        await using (var db = factory.CreateDbContext())
        {
            TestData.AddUser(db, "user-1", "alice", coins: 50);
        }

        var config = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var coinService = new BgmCoinService(factory, config);
        var service = new ShopService(factory, coinService);

        var itemId = await service.CreateItemAsync("Emoji Pack", "", 10, "EmojiPack", "😀,🔥", false, true);
        var result = await service.PurchaseItemAsync("user-1", itemId);

        Assert.Equal(PurchaseResult.Success, result);

        await using var verify = factory.CreateDbContext();
        var user = verify.Users.Single(u => u.Id == "user-1");
        Assert.Equal(40, user.BgmCoins);
        Assert.Single(verify.UserPurchases.ToList());
    }

    [Fact]
    public async Task PurchaseItemAsync_ReturnsInsufficientCoins_WhenBalanceLow()
    {
        using var factory = new TestDbFactory();
        await using (var db = factory.CreateDbContext())
        {
            TestData.AddUser(db, "user-1", "alice", coins: 2);
        }

        var config = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var coinService = new BgmCoinService(factory, config);
        var service = new ShopService(factory, coinService);

        var itemId = await service.CreateItemAsync("Emoji Pack", "", 10, "EmojiPack", "😀", false, true);
        var result = await service.PurchaseItemAsync("user-1", itemId);

        Assert.Equal(PurchaseResult.InsufficientCoins, result);
    }

    [Fact]
    public async Task GetUserEmojisAsync_ReturnsEmojiPackContents()
    {
        using var factory = new TestDbFactory();
        await using (var db = factory.CreateDbContext())
        {
            TestData.AddUser(db, "user-1", "alice", coins: 10);
        }

        var config = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var coinService = new BgmCoinService(factory, config);
        var service = new ShopService(factory, coinService);

        var itemId = await service.CreateItemAsync("Emoji Pack", "", 0, "EmojiPack", "😀,🔥", false, true);
        await service.PurchaseItemAsync("user-1", itemId);

        var emojis = await service.GetUserEmojisAsync("user-1");

        Assert.Contains("😀", emojis);
        Assert.Contains("🔥", emojis);
    }

    [Fact]
    public async Task AddReactionAsync_AddsAndToggles()
    {
        using var factory = new TestDbFactory();
        await using (var db = factory.CreateDbContext())
        {
            TestData.AddUser(db, "user-1", "alice", coins: 10);
            var game = TestData.AddGame(db, "Heat");
            var night = TestData.AddGameNight(db, new DateOnly(2026, 2, 4));
            TestData.AddGameNightGame(db, night, game);
        }

        var config = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var coinService = new BgmCoinService(factory, config);
        var service = new ShopService(factory, coinService);

        var itemId = await service.CreateItemAsync("Emoji Pack", "", 0, "EmojiPack", "🔥", false, true);
        await service.PurchaseItemAsync("user-1", itemId);

        await using var verify = factory.CreateDbContext();
        var gameNightGameId = verify.GameNightGames.Select(g => g.Id).Single();

        var added = await service.AddReactionAsync(gameNightGameId, "user-1", "🔥");
        Assert.True(added);

        var toggled = await service.AddReactionAsync(gameNightGameId, "user-1", "🔥");
        Assert.True(toggled);

        await using var verify2 = factory.CreateDbContext();
        Assert.Empty(verify2.GameResultReactions.ToList());
    }
}
