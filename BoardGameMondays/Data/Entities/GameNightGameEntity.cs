using System.ComponentModel.DataAnnotations;

namespace BoardGameMondays.Data.Entities;

public sealed class GameNightGameEntity
{
    [Key]
    public int Id { get; set; }

    [Required]
    public Guid GameNightId { get; set; }

    public GameNightEntity GameNight { get; set; } = null!;

    [Required]
    public Guid GameId { get; set; }

    public BoardGameEntity Game { get; set; } = null!;

    // Future-proofing: allow distinguishing planned vs actually played.
    [Required]
    public bool IsPlayed { get; set; }

    public Guid? WinnerMemberId { get; set; }

    public MemberEntity? WinnerMember { get; set; }

    public List<GameNightGamePlayerEntity> Players { get; set; } = new();

    [Required]
    public DateTimeOffset CreatedOn { get; set; }
}
