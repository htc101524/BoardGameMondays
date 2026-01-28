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

    /// <summary>
    /// Whether the game night has started.
    /// When true, attendees cannot place new bets, but non-attendees can still bet.
    /// </summary>
    public bool HasStarted { get; set; }

    public List<GameNightAttendeeEntity> Attendees { get; set; } = new();

    public List<GameNightRsvpEntity> Rsvps { get; set; } = new();

    public List<GameNightGameEntity> Games { get; set; } = new();
}
