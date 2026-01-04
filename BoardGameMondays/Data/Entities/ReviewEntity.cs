using System.ComponentModel.DataAnnotations;

namespace BoardGameMondays.Data.Entities;

public sealed class ReviewEntity
{
    [Key]
    public Guid Id { get; set; }

    [Required]
    public Guid GameId { get; set; }

    public BoardGameEntity Game { get; set; } = null!;

    [Required]
    public Guid ReviewerId { get; set; }

    public MemberEntity Reviewer { get; set; } = null!;

    [Required]
    public double Rating { get; set; }

    [Required]
    public int TimesPlayed { get; set; }

    [Required]
    [MaxLength(4000)]
    public string Description { get; set; } = string.Empty;

    [Required]
    public DateTimeOffset CreatedOn { get; set; }
}
