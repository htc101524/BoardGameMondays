using BoardGameMondays.Data;
using BoardGameMondays.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace BoardGameMondays.Core;

public sealed class TicketService
{
    private readonly IDbContextFactory<ApplicationDbContext> _dbFactory;

    public TicketService(IDbContextFactory<ApplicationDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public sealed record TicketListItem(
        Guid Id,
        TicketType Type,
        string Title,
        string? Description,
        DateTimeOffset CreatedOn,
        int PriorityScore,
        DateTimeOffset? DoneOn);

    public async Task<TicketListItem> CreateAsync(
        TicketType type,
        string title,
        string? description,
        string? createdByUserId,
        CancellationToken ct = default)
    {
        title = InputGuards.RequireTrimmed(title, maxLength: 120, nameof(title), "Title is required.");
        description = InputGuards.OptionalTrimToNull(description, maxLength: 2_000, nameof(description));

        var entity = new TicketEntity
        {
            Id = Guid.NewGuid(),
            Type = (int)type,
            Title = title,
            Description = description,
            CreatedOn = DateTimeOffset.UtcNow,
            CreatedByUserId = createdByUserId
        };

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        db.Tickets.Add(entity);
        await db.SaveChangesAsync(ct);

        return new TicketListItem(entity.Id, type, entity.Title, entity.Description, entity.CreatedOn, PriorityScore: 0, DoneOn: entity.DoneOn);
    }

    public async Task<bool> MarkDoneAsync(Guid ticketId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var entity = await db.Tickets.FirstOrDefaultAsync(t => t.Id == ticketId, ct);
        if (entity is null)
        {
            return false;
        }

        if (entity.DoneOn is not null)
        {
            return true;
        }

        entity.DoneOn = DateTimeOffset.UtcNow;

        await db.TicketPriorities
            .Where(p => p.TicketId == ticketId)
            .ExecuteDeleteAsync(ct);

        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> ReopenAsync(Guid ticketId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var entity = await db.Tickets.FirstOrDefaultAsync(t => t.Id == ticketId, ct);
        if (entity is null)
        {
            return false;
        }

        if (entity.DoneOn is null)
        {
            return true;
        }

        entity.DoneOn = null;
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> DeleteAsync(Guid ticketId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var entity = await db.Tickets.FirstOrDefaultAsync(t => t.Id == ticketId, ct);
        if (entity is null)
        {
            return false;
        }

        db.Tickets.Remove(entity);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<IReadOnlyList<TicketListItem>> GetOrderedAsync(TicketType type, CancellationToken ct = default)
    {
        var typeInt = (int)type;

        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var tickets = await db.Tickets
            .AsNoTracking()
            .Where(t => t.Type == typeInt && t.DoneOn == null)
            .ToListAsync(ct);

        if (tickets.Count == 0)
        {
            return Array.Empty<TicketListItem>();
        }

        var priorities = await db.TicketPriorities
            .AsNoTracking()
            .Where(p => p.Type == typeInt)
            .ToListAsync(ct);

        var scores = priorities
            .GroupBy(p => p.TicketId)
            .ToDictionary(g => g.Key, g => g.Sum(p => WeightForRank(p.Rank)));

        return tickets
            .Select(t => new TicketListItem(
                Id: t.Id,
                Type: (TicketType)t.Type,
                Title: t.Title,
                Description: t.Description,
                CreatedOn: t.CreatedOn,
                PriorityScore: scores.TryGetValue(t.Id, out var s) ? s : 0,
                DoneOn: t.DoneOn))
            .OrderByDescending(x => x.PriorityScore)
            .ThenBy(x => x.CreatedOn)
            .ToArray();
    }

    public async Task<IReadOnlyList<TicketListItem>> GetDoneOrderedAsync(TicketType type, CancellationToken ct = default)
    {
        var typeInt = (int)type;

        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var tickets = await db.Tickets
            .AsNoTracking()
            .Where(t => t.Type == typeInt && t.DoneOn != null)
            .OrderByDescending(t => t.DoneOn)
            .ThenBy(t => t.CreatedOn)
            .Select(t => new TicketListItem(
                Id: t.Id,
                Type: (TicketType)t.Type,
                Title: t.Title,
                Description: t.Description,
                CreatedOn: t.CreatedOn,
                PriorityScore: 0,
                DoneOn: t.DoneOn))
            .ToListAsync(ct);

        return tickets;
    }

    public async Task<(Guid? First, Guid? Second, Guid? Third)> GetAdminTop3Async(
        string adminUserId,
        TicketType type,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(adminUserId))
        {
            return (null, null, null);
        }

        var typeInt = (int)type;

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var priorities = await db.TicketPriorities
            .AsNoTracking()
            .Where(p => p.AdminUserId == adminUserId && p.Type == typeInt)
            .ToListAsync(ct);

        Guid? GetRank(int rank) => priorities.FirstOrDefault(p => p.Rank == rank)?.TicketId;

        return (GetRank(1), GetRank(2), GetRank(3));
    }

    public async Task SetAdminTop3Async(
        string adminUserId,
        TicketType type,
        Guid? first,
        Guid? second,
        Guid? third,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(adminUserId))
        {
            throw new InvalidOperationException("Admin user id is required.");
        }

        var selected = new[] { first, second, third }
            .Where(x => x is not null)
            .Select(x => x!.Value)
            .ToArray();

        if (selected.Distinct().Count() != selected.Length)
        {
            throw new InvalidOperationException("You can't select the same ticket more than once.");
        }

        var typeInt = (int)type;

        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        // Replace entire set for this admin+type.
        await db.TicketPriorities
            .Where(p => p.AdminUserId == adminUserId && p.Type == typeInt)
            .ExecuteDeleteAsync(ct);

        var toAdd = new List<TicketPriorityEntity>();
        AddIfNotNull(first, 1);
        AddIfNotNull(second, 2);
        AddIfNotNull(third, 3);

        void AddIfNotNull(Guid? ticketId, int rank)
        {
            if (ticketId is null)
            {
                return;
            }

            toAdd.Add(new TicketPriorityEntity
            {
                TicketId = ticketId.Value,
                AdminUserId = adminUserId,
                Type = typeInt,
                Rank = rank
            });
        }

        if (toAdd.Count > 0)
        {
            db.TicketPriorities.AddRange(toAdd);
            await db.SaveChangesAsync(ct);
        }
    }

    public async Task<IReadOnlyList<(Guid Id, string Title)>> GetOpenChoicesAsync(TicketType type, CancellationToken ct = default)
    {
        var typeInt = (int)type;

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.Tickets
            .AsNoTracking()
            .Where(t => t.Type == typeInt && t.DoneOn == null)
            .OrderBy(t => t.Title)
            .Select(t => new ValueTuple<Guid, string>(t.Id, t.Title))
            .ToListAsync(ct);
    }

    private static int WeightForRank(int rank) => rank switch
    {
        1 => 3,
        2 => 2,
        3 => 1,
        _ => 0
    };
}
