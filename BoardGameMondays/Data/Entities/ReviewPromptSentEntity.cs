using System.ComponentModel.DataAnnotations;

namespace BoardGameMondays.Data.Entities;

/// <summary>
/// Tracks which member-game combinations have been prompted to write a review.
/// Prevents duplicate review prompt emails for the same game played by the same member.
/// </summary>
public sealed class ReviewPromptSentEntity
{
    [Key]
    public Guid Id { get; set; }

    [Required]
    public Guid MemberId { get; set; }

    public MemberEntity Member { get; set; } = null!;

    [Required]
    public Guid GameId { get; set; }

    public BoardGameEntity Game { get; set; } = null!;

    [Required]
    public DateTimeOffset SentOn { get; set; }
}
