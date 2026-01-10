namespace BoardGameMondays.Data.Entities;

public sealed class TicketEntity
{
    public Guid Id { get; set; }

    // (int)TicketType
    public int Type { get; set; }

    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    public DateTimeOffset CreatedOn { get; set; }

    public DateTimeOffset? DoneOn { get; set; }

    public string? CreatedByUserId { get; set; }

    public ICollection<TicketPriorityEntity> Priorities { get; set; } = new List<TicketPriorityEntity>();
}
