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

    [Required]
    public DateTimeOffset CreatedOn { get; set; }
}
