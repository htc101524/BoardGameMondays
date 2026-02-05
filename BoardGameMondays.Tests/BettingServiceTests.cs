using System.Threading;
using System.Threading.Tasks;
using BoardGameMondays.Core;
using BoardGameMondays.Data.Entities;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace BoardGameMondays.Tests;

public sealed class BettingServiceTests
{
    [Fact]
    public async Task ResolveGameAsync_AllowsSameDay()
    {
        using var factory = new TestDbFactory();
        Guid nightId;
        int nightGameId;

        await using (var db = factory.CreateDbContext())
        {
            var user = TestData.AddUser(db, "user-1", "alice", coins: 0);
            var member = TestData.AddMember(db, "Alice");
            var game = TestData.AddGame(db, "Catan");
            var night = TestData.AddGameNight(db, DateOnly.FromDateTime(DateTime.Today));
            var nightGame = TestData.AddGameNightGame(db, night, game);

            db.GameNightGamePlayers.Add(new GameNightGamePlayerEntity
            {
                GameNightGameId = nightGame.Id,
                MemberId = member.Id,
                CreatedOn = DateTimeOffset.UtcNow
            });

            db.GameNightGameOdds.Add(new GameNightGameOddsEntity
            {
                GameNightGameId = nightGame.Id,
                MemberId = member.Id,
                OddsTimes100 = 200
            });

            nightGame.WinnerMemberId = member.Id;

            db.GameNightGameBets.Add(new GameNightGameBetEntity
            {
                GameNightGameId = nightGame.Id,
                UserId = user.Id,
                PredictedWinnerMemberId = member.Id,
                Amount = 10,
                OddsTimes100 = 200,
                IsResolved = false,
                Payout = 0,
                CreatedOn = DateTimeOffset.UtcNow
            });

            await db.SaveChangesAsync();

            nightId = night.Id;
            nightGameId = nightGame.Id;
        }

        var config = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var coins = new BgmCoinService(factory, config);
        var ranking = new RankingService(factory);
        var odds = new OddsService(factory, ranking);
        var service = new BettingService(factory, coins, ranking, odds, new NullHubContext());

        var result = await service.ResolveGameAsync(nightId, nightGameId);

        Assert.Equal(BettingService.ResolveResult.Ok, result);

        await using var verify = factory.CreateDbContext();
        var bet = Assert.Single(verify.GameNightGameBets);
        Assert.True(bet.IsResolved);
        Assert.NotNull(bet.ResolvedOn);
    }

    private sealed class NullHubContext : IHubContext<GameNightHub>
    {
        public IHubClients Clients { get; } = new NullHubClients();
        public IGroupManager Groups { get; } = new NullGroupManager();
    }

    private sealed class NullHubClients : IHubClients
    {
        private static readonly IClientProxy Proxy = new NullClientProxy();

        public IClientProxy All => Proxy;
        public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => Proxy;
        public IClientProxy Client(string connectionId) => Proxy;
        public IClientProxy Clients(IReadOnlyList<string> connectionIds) => Proxy;
        public IClientProxy Group(string groupName) => Proxy;
        public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => Proxy;
        public IClientProxy Groups(IReadOnlyList<string> groupNames) => Proxy;
        public IClientProxy User(string userId) => Proxy;
        public IClientProxy Users(IReadOnlyList<string> userIds) => Proxy;
    }

    private sealed class NullClientProxy : IClientProxy
    {
        public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class NullGroupManager : IGroupManager
    {
        public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
