using System.ComponentModel.DataAnnotations;

namespace BoardGameMondays.Data.Entities;

public sealed class MemberEntity
{
    [Key]
    public Guid Id { get; set; }

    [Required]
    public bool IsBgmMember { get; set; } = true;

    [Required]
    [MaxLength(128)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(256)]
    public string Email { get; set; } = string.Empty;

    [MaxLength(512)]
    public string? Summary { get; set; }

    [MaxLength(128)]
    public string? ProfileTagline { get; set; }

    [MaxLength(128)]
    public string? FavoriteGame { get; set; }

    [MaxLength(128)]
    public string? PlayStyle { get; set; }

    [MaxLength(256)]
    public string? FunFact { get; set; }

    [MaxLength(512)]
    public string? AvatarUrl { get; set; }

    /// <summary>
    /// ELO-style rating for betting odds calculation. Default is 1200.
    /// </summary>
    public int EloRating { get; set; } = 1200;

    /// <summary>
    /// When the ELO rating was last updated.
    /// </summary>
    public DateTimeOffset? EloRatingUpdatedOn { get; set; }

    /// <summary>
    /// DateKey (YYYYMMDD) for the last Monday attendance coins claim.
    /// </summary>
    public int? LastMondayCoinsClaimedDateKey { get; set; }
}
