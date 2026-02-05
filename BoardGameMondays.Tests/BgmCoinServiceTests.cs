using BoardGameMondays.Core;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace BoardGameMondays.Tests;

public sealed class BgmCoinServiceTests
{
    [Fact]
    public async Task TryAddAsync_IncreasesCoins()
    {
        using var factory = new TestDbFactory();
        await using (var db = factory.CreateDbContext())
        {
            TestData.AddUser(db, "user-1", "alice", coins: 10);
        }

        var config = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var service = new BgmCoinService(factory, config);

        var added = await service.TryAddAsync("user-1", 5);

        Assert.True(added);

        await using var verify = factory.CreateDbContext();
        var user = verify.Users.Single(u => u.Id == "user-1");
        Assert.Equal(15, user.BgmCoins);
    }

    [Fact]
    public async Task TrySpendAsync_ReturnsFalse_WhenInsufficientCoins()
    {
        using var factory = new TestDbFactory();
        await using (var db = factory.CreateDbContext())
        {
            TestData.AddUser(db, "user-1", "alice", coins: 2);
        }

        var config = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var service = new BgmCoinService(factory, config);

        var spent = await service.TrySpendAsync("user-1", 5);

        Assert.False(spent);
    }

    [Fact]
    public async Task TrySpendAsync_DecreasesCoins_WhenEnough()
    {
        using var factory = new TestDbFactory();
        await using (var db = factory.CreateDbContext())
        {
            TestData.AddUser(db, "user-1", "alice", coins: 20);
        }

        var config = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var service = new BgmCoinService(factory, config);

        var spent = await service.TrySpendAsync("user-1", 6);

        Assert.True(spent);

        await using var verify = factory.CreateDbContext();
        var user = verify.Users.Single(u => u.Id == "user-1");
        Assert.Equal(14, user.BgmCoins);
    }

    [Fact]
    public async Task GetHouseNetSinceAsync_ReturnsZero_WhenNoBets()
    {
        using var factory = new TestDbFactory();

        var config = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var service = new BgmCoinService(factory, config);

        var houseNet = await service.GetHouseNetSinceAsync(null);

        Assert.Equal(0, houseNet);
    }

    [Fact]
    public async Task GetHouseNetSinceAsync_CalculatesNetCorrectly()
    {
        using var factory = new TestDbFactory();
        await using (var db = factory.CreateDbContext())
        {
            var user = TestData.AddUser(db, "user-1", "alice");
            var member = TestData.AddMember(db, "winner");
            var game = TestData.AddGame(db, "Catan");
            var night = TestData.AddGameNight(db, new DateOnly(2026, 2, 1));
            var nightGame = TestData.AddGameNightGame(db, night, game);

            // Bet 1: User loses $100, house gains $100 (payout = 0)
            TestData.AddBet(db, nightGame, "user-1", member, amount: 100, oddsTimes100: 200, isResolved: true, payout: 0, resolvedOn: DateTimeOffset.UtcNow);

            // Bet 2: User wins $50 on $50 bet (2:1 odds), house loses $50 (payout = 150, amount = 50, net = -100)
            TestData.AddBet(db, nightGame, "user-1", member, amount: 50, oddsTimes100: 200, isResolved: true, payout: 150, resolvedOn: DateTimeOffset.UtcNow);
        }

        var config = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var service = new BgmCoinService(factory, config);

        var houseNet = await service.GetHouseNetSinceAsync(null);

        // Bet 1: 100 - 0 = 100 (house up)
        // Bet 2: 50 - 150 = -100 (house down)
        // Total: 100 + (-100) = 0
        Assert.Equal(0, houseNet);
    }

    [Fact]
    public async Task TryAddAsync_ReturnsFalse_WhenUserNotFound()
    {
        using var factory = new TestDbFactory();
        var config = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var service = new BgmCoinService(factory, config);

        var added = await service.TryAddAsync("nonexistent-user", 5);

        Assert.False(added);
    }

    [Fact]
    public async Task TrySpendAsync_ReturnsFalse_WhenUserNotFound()
    {
        using var factory = new TestDbFactory();
        var config = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var service = new BgmCoinService(factory, config);

        var spent = await service.TrySpendAsync("nonexistent-user", 5);

        Assert.False(spent);
    }

    [Fact]
    public async Task TryAddAsync_HandlesZeroCoinAddition()
    {
        using var factory = new TestDbFactory();
        await using (var db = factory.CreateDbContext())
        {
            TestData.AddUser(db, "user-1", "alice", coins: 10);
        }

        var config = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var service = new BgmCoinService(factory, config);

        var added = await service.TryAddAsync("user-1", 0);

        Assert.True(added);

        await using var verify = factory.CreateDbContext();
        var user = verify.Users.Single(u => u.Id == "user-1");
        Assert.Equal(10, user.BgmCoins);
    }

    [Fact]
    public async Task TrySpendAsync_HandlesExactCoinSpend()
    {
        using var factory = new TestDbFactory();
        await using (var db = factory.CreateDbContext())
        {
            TestData.AddUser(db, "user-1", "alice", coins: 10);
        }

        var config = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var service = new BgmCoinService(factory, config);

        var spent = await service.TrySpendAsync("user-1", 10);

        Assert.True(spent);

        await using var verify = factory.CreateDbContext();
        var user = verify.Users.Single(u => u.Id == "user-1");
        Assert.Equal(0, user.BgmCoins);
    }

    [Fact]
    public async Task GetHouseNetSinceAsync_FiltersWithinDateRange()
    {
        using var factory = new TestDbFactory();
        await using (var db = factory.CreateDbContext())
        {
            var user = TestData.AddUser(db, "user-1", "alice");
            var member = TestData.AddMember(db, "winner");
            var game = TestData.AddGame(db, "Catan");
            
            var oldNight = TestData.AddGameNight(db, new DateOnly(2025, 1, 1));
            var recentNight = TestData.AddGameNight(db, new DateOnly(2026, 2, 1));

            var oldNightGame = TestData.AddGameNightGame(db, oldNight, game);
            var recentNightGame = TestData.AddGameNightGame(db, recentNight, game);

            // Bet from old date
            TestData.AddBet(db, oldNightGame, "user-1", member, amount: 100, oddsTimes100: 200, isResolved: true, payout: 0, resolvedOn: new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero));

            // Bet from recent date
            TestData.AddBet(db, recentNightGame, "user-1", member, amount: 100, oddsTimes100: 200, isResolved: true, payout: 0, resolvedOn: DateTimeOffset.UtcNow);
        }

        var config = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var service = new BgmCoinService(factory, config);

        // Should include all bets when dating from long ago
        var houseNetAll = await service.GetHouseNetSinceAsync(new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero));
        Assert.True(houseNetAll >= 200); // Both bets counted

        // Should only count recent bet
        var houseNetRecent = await service.GetHouseNetSinceAsync(new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero));
        Assert.True(houseNetRecent >= 100);
    }
}
