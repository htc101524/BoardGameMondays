using BoardGameMondays.Core;
using BoardGameMondays.Data.Entities;
using Xunit;

namespace BoardGameMondays.Tests;

public sealed class TicketServiceTests
{
    [Fact]
    public async Task CreateAsync_PersistsTicket()
    {
        using var factory = new TestDbFactory();
        var service = new TicketService(factory);

        var created = await service.CreateAsync(
            type: TicketType.Bug,
            title: "  Fix login  ",
            description: "  stack trace  ",
            createdByUserId: "user-1");

        Assert.Equal(TicketType.Bug, created.Type);
        Assert.Equal("Fix login", created.Title);
        Assert.Equal("stack trace", created.Description);

        await using var db = factory.CreateDbContext();
        var entity = db.Tickets.Single();
        Assert.Equal(created.Id, entity.Id);
        Assert.Equal("Fix login", entity.Title);
        Assert.Equal("stack trace", entity.Description);
    }

    [Fact]
    public async Task MarkDoneAsync_SetsDoneOn_AndClearsPriorities()
    {
        using var factory = new TestDbFactory();
        var service = new TicketService(factory);

        var created = await service.CreateAsync(TicketType.Feature, "Add mode", null, "admin");

        await using (var db = factory.CreateDbContext())
        {
            db.TicketPriorities.Add(new TicketPriorityEntity
            {
                TicketId = created.Id,
                AdminUserId = "admin",
                Type = (int)TicketType.Feature,
                Rank = 1
            });
            await db.SaveChangesAsync();
        }

        var done = await service.MarkDoneAsync(created.Id);

        Assert.True(done);

        await using var verify = factory.CreateDbContext();
        var entity = verify.Tickets.Single();
        Assert.NotNull(entity.DoneOn);
        Assert.Empty(verify.TicketPriorities.ToList());
    }

    [Fact]
    public async Task ReopenAsync_ClearsDoneOn()
    {
        using var factory = new TestDbFactory();
        var service = new TicketService(factory);

        var created = await service.CreateAsync(TicketType.Bug, "Crash", null, "user");
        await service.MarkDoneAsync(created.Id);

        var reopened = await service.ReopenAsync(created.Id);

        Assert.True(reopened);

        await using var db = factory.CreateDbContext();
        var entity = db.Tickets.Single();
        Assert.Null(entity.DoneOn);
    }

    [Fact]
    public async Task DeleteAsync_RemovesTicket()
    {
        using var factory = new TestDbFactory();
        var service = new TicketService(factory);

        var created = await service.CreateAsync(TicketType.Bug, "Remove", null, "user");

        var deleted = await service.DeleteAsync(created.Id);

        Assert.True(deleted);

        await using var db = factory.CreateDbContext();
        Assert.Empty(db.Tickets.ToList());
    }
}
