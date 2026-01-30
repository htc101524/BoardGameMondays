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

    // When confirmed, the players are locked and betting can open.
    [Required]
    public bool IsConfirmed { get; set; }

    public Guid? WinnerMemberId { get; set; }

    public MemberEntity? WinnerMember { get; set; }

    // Optional: for team-based games, the winning team name.
    [MaxLength(64)]
    public string? WinnerTeamName { get; set; }

    public int? Score { get; set; }

    [Required]
    public bool IsHighScore { get; set; }

    public List<GameNightGamePlayerEntity> Players { get; set; } = new();

    public List<GameNightGameTeamEntity> Teams { get; set; } = new();

    public List<GameNightGameOddsEntity> Odds { get; set; } = new();

    public List<GameNightGameBetEntity> Bets { get; set; } = new();

    public List<GameNightGameVictoryRouteValueEntity> VictoryRouteValues { get; set; } = new();

    [Required]
    public DateTimeOffset CreatedOn { get; set; }
}
