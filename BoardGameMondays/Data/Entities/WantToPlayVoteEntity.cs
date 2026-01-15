using System.ComponentModel.DataAnnotations;

namespace BoardGameMondays.Data.Entities;

public sealed class WantToPlayVoteEntity
{
    [Key]
    public int Id { get; set; }

    [Required]
    public Guid GameId { get; set; }

    public BoardGameEntity Game { get; set; } = null!;

    [Required]
    [MaxLength(200)]
    public string UserId { get; set; } = string.Empty;

    [Required]
    public int WeekKey { get; set; }

    [Required]
    public DateTimeOffset CreatedOn { get; set; }
}
