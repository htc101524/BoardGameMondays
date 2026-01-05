using System.ComponentModel.DataAnnotations;

namespace BoardGameMondays.Data.Entities;

public sealed class GameNightGameOddsEntity
{
    [Key]
    public int Id { get; set; }

    [Required]
    public int GameNightGameId { get; set; }

    public GameNightGameEntity GameNightGame { get; set; } = null!;

    [Required]
    public Guid MemberId { get; set; }

    public MemberEntity Member { get; set; } = null!;

    // Decimal odds stored as an integer multiplier x100 (e.g. 175 => 1.75)
    [Required]
    public int OddsTimes100 { get; set; }

    [Required]
    public DateTimeOffset CreatedOn { get; set; }
}
