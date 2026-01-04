using System.ComponentModel.DataAnnotations;

namespace BoardGameMondays.Data.Entities;

public sealed class GameNightAttendeeEntity
{
    [Key]
    public int Id { get; set; }

    [Required]
    public Guid GameNightId { get; set; }

    public GameNightEntity GameNight { get; set; } = null!;

    [Required]
    public Guid MemberId { get; set; }

    public MemberEntity Member { get; set; } = null!;

    [Required]
    public DateTimeOffset CreatedOn { get; set; }
}
