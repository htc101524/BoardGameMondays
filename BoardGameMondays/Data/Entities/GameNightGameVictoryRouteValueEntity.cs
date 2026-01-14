using System.ComponentModel.DataAnnotations;

namespace BoardGameMondays.Data.Entities;

public sealed class GameNightGameVictoryRouteValueEntity
{
    [Key]
    public int Id { get; set; }

    [Required]
    public int GameNightGameId { get; set; }

    public GameNightGameEntity GameNightGame { get; set; } = null!;

    [Required]
    public Guid VictoryRouteId { get; set; }

    public VictoryRouteEntity VictoryRoute { get; set; } = null!;

    [MaxLength(256)]
    public string? ValueString { get; set; }

    public bool? ValueBool { get; set; }
}
