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

        var ranking = new RankingService(factory);
        var odds = new OddsService(factory, ranking);
        var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 100 });
        var service = new GameNightService(factory, odds, cache);

        var night = await service.CreateAsync(new DateOnly(2026, 2, 4));

        await using var db2 = factory.CreateDbContext();
        var memberId = db2.Members.Select(m => m.Id).Single();

        var updated = await service.SetRsvpAsync(night.Id, memberId, attending: true);

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

        var ranking = new RankingService(factory);
        var odds = new OddsService(factory, ranking);
        var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 100 });
        var service = new GameNightService(factory, odds, cache);

        var night = await service.CreateAsync(new DateOnly(2026, 2, 4));

        await using var db2 = factory.CreateDbContext();
        var memberId = db2.Members.Select(m => m.Id).Single();

        await service.SetRsvpAsync(night.Id, memberId, attending: true);
        var updated = await service.SetRsvpAsync(night.Id, memberId, attending: false);

        Assert.NotNull(updated);
        Assert.Empty(updated!.Attendees);
        Assert.Single(updated.Rsvps);
        Assert.False(updated.Rsvps.Single().IsAttending);
    }
}
