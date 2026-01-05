using System.ComponentModel.DataAnnotations;

namespace BoardGameMondays.Data.Entities;

public sealed class GameNightGameBetEntity
{
    [Key]
    public int Id { get; set; }

    [Required]
    public int GameNightGameId { get; set; }

    public GameNightGameEntity GameNightGame { get; set; } = null!;

    [Required]
    [MaxLength(450)]
    public string UserId { get; set; } = string.Empty;

    [Required]
    public Guid PredictedWinnerMemberId { get; set; }

    public MemberEntity PredictedWinnerMember { get; set; } = null!;

    [Required]
    public int Amount { get; set; }

    [Required]
    public int OddsTimes100 { get; set; }

    [Required]
    public bool IsResolved { get; set; }

    // Amount credited back to the user when resolved. (Losing bets pay 0.)
    [Required]
    public int Payout { get; set; }

    public DateTimeOffset? ResolvedOn { get; set; }

    [Required]
    public DateTimeOffset CreatedOn { get; set; }
}
