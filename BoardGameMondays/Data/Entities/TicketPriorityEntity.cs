namespace BoardGameMondays.Data.Entities;

public sealed class TicketPriorityEntity
{
    public int Id { get; set; }

    public Guid TicketId { get; set; }

    public TicketEntity Ticket { get; set; } = null!;

    public string AdminUserId { get; set; } = string.Empty;

    // (int)TicketType; duplicated to make indexing/enforcement easy.
    public int Type { get; set; }

    // 1 = highest, 3 = lowest (within the top-3 list)
    public int Rank { get; set; }
}
