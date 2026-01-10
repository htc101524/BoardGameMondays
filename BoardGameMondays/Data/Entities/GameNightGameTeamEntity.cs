using System.ComponentModel.DataAnnotations;

namespace BoardGameMondays.Data.Entities;

public sealed class GameNightGameTeamEntity
{
    [Key]
    public int Id { get; set; }

    [Required]
    public int GameNightGameId { get; set; }

    public GameNightGameEntity GameNightGame { get; set; } = null!;

    [Required]
    [MaxLength(64)]
    public string TeamName { get; set; } = null!;

    // Optional: admin-selected team colour (e.g. "#RRGGBB").
    [MaxLength(16)]
    public string? ColorHex { get; set; }
}
