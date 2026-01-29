using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BoardGameMondays.Data.Entities;

public sealed class GameResultReactionEntity
{
    [Key]
    public Guid Id { get; set; }

    [Required]
    [ForeignKey(nameof(GameNightGame))]
    public int GameNightGameId { get; set; }

    public GameNightGameEntity? GameNightGame { get; set; }

    /// <summary>
    /// The user who reacted (AspNetUsers.Id).
    /// </summary>
    [Required]
    [MaxLength(450)]
    public string UserId { get; set; } = string.Empty;

    /// <summary>
    /// The emoji reaction (single emoji character).
    /// </summary>
    [Required]
    [MaxLength(10)]
    public string Emoji { get; set; } = string.Empty;

    public DateTimeOffset CreatedOn { get; set; } = DateTimeOffset.UtcNow;
}
