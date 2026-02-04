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
}
