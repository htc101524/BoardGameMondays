using System.ComponentModel.DataAnnotations;

namespace BoardGameMondays.Data.Entities;

public sealed class GameNightEntity
{
    [Key]
    public Guid Id { get; set; }

    // Stored as YYYYMMDD for easy querying in SQLite.
    [Required]
    public int DateKey { get; set; }

    public string? Recap { get; set; }

    public List<GameNightAttendeeEntity> Attendees { get; set; } = new();

    public List<GameNightGameEntity> Games { get; set; } = new();
}
