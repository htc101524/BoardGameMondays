using System.ComponentModel.DataAnnotations;

namespace BoardGameMondays.Data.Entities;

public sealed class GameNightGamePlayerEntity
{
    [Key]
    public int Id { get; set; }

    [Required]
    public int GameNightGameId { get; set; }

    public GameNightGameEntity GameNightGame { get; set; } = null!;

    [Required]
    public Guid MemberId { get; set; }

    public MemberEntity Member { get; set; } = null!;

    // Optional: team label for this player within a game (e.g. "Team A").
    [MaxLength(64)]
    public string? TeamName { get; set; }

    [Required]
    public DateTimeOffset CreatedOn { get; set; }
}
