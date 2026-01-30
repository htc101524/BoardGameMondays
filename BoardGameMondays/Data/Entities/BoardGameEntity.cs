using System.ComponentModel.DataAnnotations;

namespace BoardGameMondays.Data.Entities;

public sealed class BoardGameEntity
{
    [Key]
    public Guid Id { get; set; }

    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    public int Status { get; set; }

    [MaxLength(512)]
    public string? Tagline { get; set; }

    [MaxLength(512)]
    public string? ImageUrl { get; set; }

    // Game stats (optional)
    public int? MinPlayers { get; set; }

    public int? MaxPlayers { get; set; }

    public int? RuntimeMinutes { get; set; }

    public int? FirstPlayRuntimeMinutes { get; set; }

    public double? Complexity { get; set; }

    public double? BoardGameGeekScore { get; set; }

    [MaxLength(512)]
    public string? BoardGameGeekUrl { get; set; }

    [Required]
    public bool AreScoresCountable { get; set; }

    public int? HighScore { get; set; }

    public Guid? HighScoreMemberId { get; set; }

    [MaxLength(128)]
    public string? HighScoreMemberName { get; set; }

    public DateTimeOffset? HighScoreAchievedOn { get; set; }

    public List<ReviewEntity> Reviews { get; set; } = new();

    public List<VictoryRouteEntity> VictoryRoutes { get; set; } = new();
}
